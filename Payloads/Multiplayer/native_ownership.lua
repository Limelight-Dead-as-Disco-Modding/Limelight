local M = {}
local running

local function valid(object)
    if object == nil then return false end
    local ok, result = pcall(function() return object:IsValid() end)
    return ok and result == true
end

local function property(object, name)
    if not valid(object) then return nil end
    local ok, result = pcall(function() return object[name] end)
    if ok then return result end
end

local function call(object, method)
    if not valid(object) then return nil end
    local ok, result = pcall(function() return object[method](object) end)
    if ok then return result end
end

local function name(object)
    return call(object, "GetFullName") or "<invalid>"
end

local function scalar(value)
    if value == nil then return "<unavailable>" end
    return tostring(value):gsub("[\r\n\t]", " ")
end

local function field(key, value)
    return key .. "=" .. string.format("%q", scalar(value))
end

local function mapValid(value)
    return type(value) == "string" and value:match("^/Game/[%w_/%-]+$") ~= nil
end

local function addressValid(value)
    if type(value) ~= "string" then return false end
    local port = value:match("^[%w%.%-]+:(%d+)$") or value:match("^%[[%x:%.]+%]:(%d+)$")
    port = tonumber(port)
    return port ~= nil and port >= 1 and port <= 65535 and port % 1 == 0
end

function M.start(config)
    if running then return running end
    config = config or {}
    local role = config.role
    local function report(message)
        print("[LimelightMPNativeOwnership][REPORT] " .. message .. "\n")
    end
    local function refuse(reason)
        report("startup=refused " .. field("reason", reason))
        return nil, reason
    end
    if role ~= "host" and role ~= "client" then return refuse("invalid-role") end
    if type(config.version) ~= "string" or config.version == "" then return refuse("missing-version") end
    local hostMap = config.hostMap or "/Game/Pagoda/Levels/DiveBar/L_DiveBar_V2"
    local arenaMap = config.arenaMap or "/Game/Pagoda/Levels/InfiniteDisco/L_InfiniteDisco_Persistent"
    local port = tonumber(config.listenPort or 7777)
    if not mapValid(hostMap) or not mapValid(arenaMap) then return refuse("invalid-map") end
    if not port or port < 1 or port > 65535 or port % 1 ~= 0 then return refuse("invalid-port") end
    if role == "client" and not addressValid(config.connectAddress) then return refuse("invalid-address") end
    for _, api in ipairs({ "ExecuteInGameThread", "ExecuteInGameThreadWithDelay",
        "RegisterLoadMapPreHook", "RegisterLoadMapPostHook" }) do
        if type(_G[api]) ~= "function" then return refuse("missing-api:" .. api) end
    end
    local helpersOk, helpers = pcall(require, "UEHelpers")
    if not helpersOk then return refuse("UEHelpers-unavailable") end

    local state = { active = true, generation = 0, transitioning = false, samples = 0,
        joinAttempts = 0, joinPending = false, pendingSamples = 0, hostRequested = false,
        lastFacts = nil, watermarkNames = {}, watermarkHook = false, sessionConfigured = nil,
        transportWaiting = false, sampleWindow = 0 }
    local public = {}
    local sampleLimit, sampleInterval = 90, 2000
    local sample, scheduleSamples, tryJoin

    local function world()
        local ok, result = pcall(helpers.GetWorld)
        if ok and valid(result) then return result end
    end
    local function sameWorld(object, currentWorld)
        local objectWorld = call(object, "GetWorld")
        return valid(currentWorld) and valid(objectWorld) and name(objectWorld) == name(currentWorld)
    end
    local function findObjects(class, limit)
        local ok, objects = pcall(function() return FindAllOf(class) or {} end)
        local result = {}
        if ok then
            for _, object in ipairs(objects) do
                if valid(object) then
                    result[#result + 1] = object
                    if #result >= limit then break end
                end
            end
        end
        return result
    end
    local function controllers(currentWorld)
        local result = {}
        for _, controller in ipairs(findObjects("PlayerController", 16)) do
            if sameWorld(controller, currentWorld) then
                result[#result + 1] = controller
                if #result == 8 then break end
            end
        end
        table.sort(result, function(left, right) return name(left) < name(right) end)
        return result
    end
    local function localController(currentWorld)
        for _, controller in ipairs(controllers(currentWorld)) do
            if call(controller, "IsLocalPlayerController") == true then return controller end
        end
    end
    local function driver(currentWorld)
        local result = property(currentWorld, "NetDriver")
        if valid(result) then return result end
        for _, candidate in ipairs(findObjects("NetDriver", 8)) do
            if sameWorld(candidate, currentWorld) and
                scalar(property(candidate, "NetDriverName")):find("GameNetDriver", 1, true) then
                return candidate
            end
        end
    end
    local function connection(currentWorld)
        local netDriver = driver(currentWorld)
        return netDriver, property(netDriver, "ServerConnection")
    end
    local function inMenu(currentWorld)
        local worldName = name(currentWorld):lower()
        return worldName:find("/main_menu/", 1, true) ~= nil
    end
    local function gameplay(currentWorld)
        local worldName = name(currentWorld):lower()
        return valid(currentWorld) and not inMenu(currentWorld) and
            worldName:find("/startup/", 1, true) == nil
    end
    local function delayed(delay, callback)
        local generation = state.generation
        ExecuteInGameThreadWithDelay(delay, function()
            if state.active and generation == state.generation and not state.transitioning then callback() end
        end)
    end
    local function onGameThread(callback)
        ExecuteInGameThread(function()
            if state.active and not state.transitioning then callback() end
        end)
    end
    local function execute(command, reason)
        local currentWorld = world()
        local controller = localController(currentWorld)
        local ok, system = pcall(helpers.GetKismetSystemLibrary)
        if not valid(currentWorld) or not valid(controller) or not ok or not valid(system) then
            report("command=deferred " .. field("reason", reason))
            return false
        end
        local issued, detail = pcall(function()
            system:ExecuteConsoleCommand(currentWorld, command, controller)
        end)
        report("command=" .. (issued and "issued" or "failed") .. " " .. field("reason", reason) ..
            " " .. field("value", command) .. " " .. field("detail", issued and "not-runtime-proof" or detail))
        return issued
    end

    local function configureSession()
        if role ~= "host" then return end
        local session = property(property(world(), "AuthorityGameMode"), "GameSession")
        if not valid(session) then return false end
        if state.sessionConfigured == name(session) then return tonumber(property(session, "MaxPlayers")) == 2 end
        -- I reserve two real network seats, not two seats plus a borrowed couch.
        local ok, detail = pcall(function() session.MaxPlayers = 2 end)
        local value = property(session, "MaxPlayers")
        state.sessionConfigured = name(session)
        report("session-limit=" .. (ok and tonumber(value) == 2 and "configured" or "failed") ..
            " " .. field("maxPlayers", value) .. " " .. field("detail", detail))
        return ok and tonumber(value) == 2
    end

    local function applyWatermark(layout)
        if not state.active or not valid(layout) or not sameWorld(layout, world()) then return end
        local buildId = property(layout, "BuildId")
        local widgetName = name(buildId)
        if not valid(buildId) or state.watermarkNames[widgetName] then return end
        local ok, textLibrary = pcall(helpers.GetKismetTextLibrary)
        if not ok or not valid(textLibrary) then return end
        local watermark = config.watermark or ("LimelightMP_" .. config.version .. " By Henreh <3")
        local applied = pcall(function() buildId:SetText(textLibrary:Conv_StringToText(watermark)) end)
        if applied then
            state.watermarkNames[widgetName] = true
            report("watermark=applied " .. field("text", watermark))
        end
    end
    local function refreshWatermark()
        if not state.watermarkHook and type(RegisterHook) == "function" then
            state.watermarkHook = pcall(function()
                RegisterHook("/Game/Pagoda/UI/Game/UI_Layout_Game.UI_Layout_Game_C:OnInitialized", function()
                    delayed(1, function()
                        for _, layout in ipairs(findObjects("UI_Layout_Game_C", 4)) do applyWatermark(layout) end
                    end)
                end)
            end)
        end
        for _, layout in ipairs(findObjects("UI_Layout_Game_C", 4)) do applyWatermark(layout) end
    end

    local function ownershipFacts(reason)
        local currentWorld = world()
        local netDriver, serverConnection = connection(currentWorld)
        local allControllers = controllers(currentWorld)
        local localCount, remoteCount = 0, 0
        local records = {}
        for _, controller in ipairs(allControllers) do
            local isLocal = call(controller, "IsLocalPlayerController")
            if isLocal == true then localCount = localCount + 1 elseif isLocal == false then remoteCount = remoteCount + 1 end
            local pawn = property(controller, "Pawn")
            local playerState = property(controller, "PlayerState")
            local pawnController = property(pawn, "Controller")
            local pawnState = property(pawn, "PlayerState")
            local matches = nil
            if valid(pawn) and valid(pawnController) then matches = name(pawnController) == name(controller) end
            local stateMatches = nil
            if valid(pawnState) and valid(playerState) then stateMatches = name(pawnState) == name(playerState) end
            records[#records + 1] = table.concat({
                field("controller", name(controller)), field("localController", isLocal),
                field("playerObject", name(property(controller, "Player"))),
                field("netConnection", name(property(controller, "NetConnection"))),
                field("netPlayerIndex", property(controller, "NetPlayerIndex")),
                field("pawn", name(pawn)), field("pawnClass", name(call(pawn, "GetClass"))),
                field("pawnRole", property(pawn, "Role")), field("pawnRemoteRole", property(pawn, "RemoteRole")),
                field("pawnLocallyControlled", call(pawn, "IsLocallyControlled")),
                field("pawnControllerMatches", matches), field("pawnPlayerStateMatches", stateMatches),
                field("acknowledgedPawn", name(property(controller, "AcknowledgedPawn"))),
                field("playerState", name(playerState)), field("playerId", property(playerState, "PlayerId")),
                field("playthroughData", name(property(playerState, "PlaythroughData"))),
                field("abilitySystem", name(property(playerState, "AbilitySystemComponent"))),
                field("moveDirectionMode", property(pawn, "MoveDirectionMode")),
                field("replicateMovement", property(pawn, "bReplicateMovement")),
                field("movementMode", property(property(pawn, "CharacterMovement"), "MovementMode")),
                field("inputComponent", name(property(pawn, "InputComponent"))),
                field("moveIgnored", call(controller, "IsMoveInputIgnored")),
                field("lookIgnored", call(controller, "IsLookInputIgnored")),
                field("moveDisabled", call(controller, "ShouldDisableMoveInput")),
                field("combatDisabled", call(controller, "ShouldDisableCombatAbilityInput"))
            }, " ")
        end
        local clientCount
        local clients = property(netDriver, "ClientConnections")
        local countOk, count = pcall(function() return #clients end)
        if countOk then clientCount = count end
        local gameMode = property(currentWorld, "AuthorityGameMode")
        local header = table.concat({ field("mode", "experimental-native"), field("version", config.version),
            field("role", role), field("world", name(currentWorld)), field("driver", name(netDriver)),
            field("serverConnection", name(serverConnection)), field("clientConnections", clientCount),
            field("localControllers", localCount), field("remoteControllers", remoteCount),
            field("maxPlayers", property(property(gameMode, "GameSession"), "MaxPlayers")) }, " ")
        local facts = header .. "\n" .. table.concat(records, "\n")
        if reason == "manual" or facts ~= state.lastFacts then
            report("snapshot " .. field("reason", reason) .. " " .. header)
            for _, record in ipairs(records) do report("ownership " .. record) end
            if localCount > 1 then report("gate=unexpected-extra-local-player action=no-repair") end
            if state.hostRequested and gameplay(currentWorld) and not valid(netDriver) then
                report("gate=listen-driver-missing action=manual-host-or-stock-servertravel")
            end
            state.lastFacts = facts
        end
        return { world = currentWorld, driver = netDriver, serverConnection = serverConnection,
            localControllers = localCount, remoteControllers = remoteCount }
    end

    tryJoin = function(reason, manual)
        if role ~= "client" or state.transitioning then return false end
        local currentWorld = world()
        local _, serverConnection = connection(currentWorld)
        if valid(serverConnection) then
            state.joinPending = false
            report("join=already-connected certification=unverified")
            return false
        end
        if manual then state.joinAttempts = 0; state.joinPending = false end
        if state.joinPending or state.joinAttempts >= 3 then return false end
        if not manual and not inMenu(currentWorld) then return false end
        if type(config.transportReady) == "function" then
            local readyOk, ready = pcall(config.transportReady)
            if not readyOk or ready ~= true then
                if not state.transportWaiting then report("join=waiting-for-transport") end
                state.transportWaiting = true
                return false
            end
        end
        state.transportWaiting = false
        state.joinPending = true
        state.pendingSamples = 0
        state.joinAttempts = state.joinAttempts + 1
        local issued = execute("open " .. config.connectAddress, reason)
        if not issued then
            state.joinPending = false
            state.joinAttempts = state.joinAttempts - 1
        end
        return issued
    end

    sample = function(reason)
        if not state.active or state.transitioning then return end
        configureSession()
        local facts = ownershipFacts(reason)
        refreshWatermark()
        if role == "client" then
            if valid(facts.serverConnection) then
                state.joinPending = false
            elseif state.joinPending then
                state.pendingSamples = state.pendingSamples + 1
                if state.pendingSamples >= 10 then
                    state.joinPending = false
                    report("join=connection-not-observed attempts=" .. tostring(state.joinAttempts) .. " certification=unverified")
                end
            elseif config.autoJoin ~= false then
                tryJoin("main-menu-auto-join", false)
            end
        end
    end
    scheduleSamples = function(delay)
        local generation = state.generation
        local sampleWindow = state.sampleWindow
        delayed(delay, function()
            if sampleWindow ~= state.sampleWindow then return end
            state.samples = state.samples + 1
            sample("periodic")
            if generation ~= state.generation or sampleWindow ~= state.sampleWindow then return end
            if state.samples < sampleLimit then
                scheduleSamples(sampleInterval)
            else
                report("diagnostics=sample-budget-ended action=Ctrl+Shift+F12-for-snapshot")
            end
        end)
    end

    function public.snapshot()
        onGameThread(function() ownershipFacts("manual"); refreshWatermark() end)
    end
    function public.host()
        onGameThread(function()
            if role ~= "host" then return end
            local currentWorld = world()
            local netDriver, serverConnection = connection(currentWorld)
            if not gameplay(currentWorld) then report("host=deferred reason=enter-Dive-Bar-first"); return end
            if valid(serverConnection) then report("host=refused reason=already-client"); return end
            if valid(netDriver) then report("host=driver-already-present certification=unverified"); public.snapshot(); return end
            if #controllers(currentWorld) > 1 then report("host=refused reason=extra-controller-restart-without-couch"); return end
            if not configureSession() then report("host=refused reason=session-limit-not-confirmed"); return end
            state.hostRequested = true
            if not execute(string.format("open %s?listen?Port=%d", hostMap, port), "host-native") then
                state.hostRequested = false
            end
        end)
    end
    function public.join()
        onGameThread(function()
            tryJoin("manual-retry", true)
            state.sampleWindow = state.sampleWindow + 1
            state.samples = 0
            scheduleSamples(sampleInterval)
        end)
    end
    function public.travel(destination)
        onGameThread(function()
            if role ~= "host" or (destination ~= hostMap and destination ~= arenaMap) then
                report("travel=refused reason=role-or-map"); return
            end
            local netDriver, serverConnection = connection(world())
            if not valid(netDriver) or valid(serverConnection) then report("travel=refused reason=no-host-driver"); return end
            -- I keep Unreal's own travel and possession chain intact for this test.
            execute("servertravel " .. destination, "native-servertravel")
        end)
    end
    function public.stop()
        state.active = false
        state.generation = state.generation + 1
        -- I leave the game session alone; stopping diagnostics is not a disconnect.
    end

    local hooksOk, detail = pcall(function()
        RegisterLoadMapPreHook(function()
            if not state.active then return end
            state.transitioning = true
            state.generation = state.generation + 1
            state.sampleWindow = state.sampleWindow + 1
            state.lastFacts = nil
            state.watermarkNames = {}
            state.sessionConfigured = nil
            report("map_transition=started")
        end)
        RegisterLoadMapPostHook(function()
            if not state.active then return end
            state.transitioning = false
            state.generation = state.generation + 1
            state.samples = 0
            state.pendingSamples = 0
            report("map_transition=finished certification=unverified")
            delayed(1, configureSession)
            scheduleSamples(1500)
        end)
    end)
    if not hooksOk then public.stop(); return refuse("map-hooks:" .. scalar(detail)) end
    local function bind(key, callback)
        if type(RegisterKeyBind) ~= "function" or not Key or not ModifierKey or not Key[key] then return end
        local ok = pcall(RegisterKeyBind, Key[key], { ModifierKey.CONTROL, ModifierKey.SHIFT }, callback)
        if not ok then report("hotkey=unavailable " .. field("key", key)) end
    end
    if role == "host" then
        bind("F5", public.host)
        bind("F7", function() public.travel(arenaMap) end)
        bind("F8", function() public.travel(hostMap) end)
    else
        bind("F6", public.join)
    end
    bind("F12", public.snapshot)
    running = public
    report("startup=experimental-native " .. field("role", role) .. " " .. field("version", config.version) ..
        " certification=unverified compatibility-hooks=not-installed")
    scheduleSamples(1500)
    return public
end

return M
