local json = {}
json.null = {}

local function decode(text)
    local position = 1
    local length = #text
    local parseValue

    local function fail(message)
        error(string.format("%s at byte %d", message, position), 0)
    end

    local function skipWhitespace()
        while position <= length do
            local byte = text:byte(position)
            if byte ~= 32 and byte ~= 9 and byte ~= 10 and byte ~= 13 then
                return
            end
            position = position + 1
        end
    end

    local function parseString()
        if text:sub(position, position) ~= '"' then
            fail("expected string")
        end

        position = position + 1
        local result = {}
        local start = position

        while position <= length do
            local character = text:sub(position, position)
            if character == '"' then
                result[#result + 1] = text:sub(start, position - 1)
                position = position + 1
                return table.concat(result)
            end

            if character == "\\" then
                result[#result + 1] = text:sub(start, position - 1)
                position = position + 1
                local escape = text:sub(position, position)
                local simple = {
                    ['"'] = '"',
                    ["\\"] = "\\",
                    ["/"] = "/",
                    ["b"] = "\b",
                    ["f"] = "\f",
                    ["n"] = "\n",
                    ["r"] = "\r",
                    ["t"] = "\t"
                }

                if simple[escape] ~= nil then
                    result[#result + 1] = simple[escape]
                    position = position + 1
                elseif escape == "u" then
                    local hexadecimal =
                        text:sub(position + 1, position + 4)
                    if #hexadecimal ~= 4 or
                       hexadecimal:find("[^0-9a-fA-F]") then
                        fail("invalid unicode escape")
                    end

                    local codepoint = tonumber(hexadecimal, 16)
                    position = position + 5

                    if codepoint >= 0xD800 and
                       codepoint <= 0xDBFF and
                       text:sub(position, position + 1) == "\\u" then
                        local lowHex =
                            text:sub(position + 2, position + 5)
                        local low = tonumber(lowHex, 16)
                        if low ~= nil and
                           low >= 0xDC00 and
                           low <= 0xDFFF then
                            codepoint =
                                0x10000 +
                                (codepoint - 0xD800) * 0x400 +
                                (low - 0xDC00)
                            position = position + 6
                        end
                    end

                    result[#result + 1] =
                        utf8.char(codepoint)
                else
                    fail("invalid string escape")
                end
                start = position
            elseif character:byte() < 32 then
                fail("control character in string")
            else
                position = position + 1
            end
        end

        fail("unterminated string")
    end

    local function parseNumber()
        local start = position
        local numberText =
            text:sub(position):match(
                "^-?%d+%.?%d*[eE]?[+-]?%d*")

        if numberText == nil or numberText == "" then
            fail("invalid number")
        end

        position = position + #numberText
        local value = tonumber(numberText)
        if value == nil then
            position = start
            fail("invalid number")
        end
        return value
    end

    local function parseArray()
        position = position + 1
        skipWhitespace()
        local result = {}

        if text:sub(position, position) == "]" then
            position = position + 1
            return result
        end

        while true do
            result[#result + 1] = parseValue()
            skipWhitespace()

            local character = text:sub(position, position)
            if character == "]" then
                position = position + 1
                return result
            end
            if character ~= "," then
                fail("expected comma or closing bracket")
            end
            position = position + 1
            skipWhitespace()
        end
    end

    local function parseObject()
        position = position + 1
        skipWhitespace()
        local result = {}

        if text:sub(position, position) == "}" then
            position = position + 1
            return result
        end

        while true do
            local key = parseString()
            skipWhitespace()
            if text:sub(position, position) ~= ":" then
                fail("expected colon")
            end

            position = position + 1
            skipWhitespace()
            result[key] = parseValue()
            skipWhitespace()

            local character = text:sub(position, position)
            if character == "}" then
                position = position + 1
                return result
            end
            if character ~= "," then
                fail("expected comma or closing brace")
            end
            position = position + 1
            skipWhitespace()
        end
    end

    parseValue = function()
        skipWhitespace()
        local character = text:sub(position, position)

        if character == '"' then
            return parseString()
        end
        if character == "{" then
            return parseObject()
        end
        if character == "[" then
            return parseArray()
        end
        if character == "-" or character:match("%d") then
            return parseNumber()
        end
        if text:sub(position, position + 3) == "true" then
            position = position + 4
            return true
        end
        if text:sub(position, position + 4) == "false" then
            position = position + 5
            return false
        end
        if text:sub(position, position + 3) == "null" then
            position = position + 4
            return json.null
        end

        fail("unexpected token")
    end

    local result = parseValue()
    skipWhitespace()
    if position <= length then
        fail("trailing data")
    end
    return result
end

function json.decode(text)
    if type(text) ~= "string" then
        return nil, "JSON input must be a string"
    end

    local ok, result = pcall(decode, text)
    if not ok then
        return nil, tostring(result)
    end
    return result
end

return json
