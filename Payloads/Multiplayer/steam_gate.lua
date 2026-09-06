local gate = {}

local function read(path)
    local file = io.open(path, "r")
    if not file then return nil end
    local value = file:read(8192)
    file:close()
    return value
end

function gate.create(enabled, nativeRoot, reader, clock)
    if not enabled then return function() return true end end
    reader = reader or read
    clock = clock or function() return os.time() * 1000 end
    if not nativeRoot then
        local source = debug.getinfo(1, "S").source
        local directory = source:sub(1, 1) == "@" and source:sub(2):match("^(.*)[/\\][^/\\]+$")
        if not directory then return function() return false end end
        nativeRoot = directory .. "/../../LimelightMPNative"
    end
    local function timestamp(status)
        return status and tonumber(status:match('"updatedUnixMs"%s*:%s*(%d+)'))
    end
    local initialOk, initialStatus = pcall(reader, nativeRoot .. "/steam-status.json")
    local initialTimestamp = initialOk and timestamp(initialStatus) or nil
    local createdAt = clock()
    local expected
    return function()
        local ok, ready = pcall(function()
            local config = reader(nativeRoot .. "/steam-session.ini")
            local request = config and ("\n" .. config):match("\nrequest_id=([a-fA-F0-9]+)[\r\n]")
            if not request or #request ~= 32 then return false end
            expected = expected or request
            -- I never let a later launcher session unlock this already-running script.
            if request ~= expected then return false end
            local marker = reader(nativeRoot .. "/steam-ready.txt")
            if marker == nil or marker:match("^%s*(.-)%s*$") ~= expected then return false end
            local status = reader(nativeRoot .. "/steam-status.json")
            local updated = timestamp(status)
            local now = clock()
            -- I require a fresh heartbeat after this script started, not a crashed game's leftover marker.
            return updated ~= nil and updated >= createdAt and updated > (initialTimestamp or 0) and
                updated >= now - 5000 and updated <= now + 1500 and
                status:match('"requestId"%s*:%s*"([%x]+)"') == expected and
                status:match('"state"%s*:%s*"([%w_]+)"') == "connected" and
                status:find('"transportReady"%s*:%s*true%s*[,}]') ~= nil
        end)
        return ok and ready or false
    end
end

return gate
