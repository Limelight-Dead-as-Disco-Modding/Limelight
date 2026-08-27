package.path =
    ".\\Mods\\LimelightArenaSlotLoader\\Scripts\\?.lua;" ..
    ".\\Mods\\LimelightArenaSlotLoader\\Scripts\\?\\init.lua;" ..
    package.path

local UEHelpers = require("UEHelpers")
local json = require("arena_slot_loader.json")

local MOD_NAME = "LimelightArenaSlotLoader"
local LOADER_ACTOR_CLASS =
    "BlueprintGeneratedClass /Game/Mods/LimelightArenaSlotLoader/ModActor.ModActor_C"
local ARENA_SELECTION_REQUEST =
    "/Game/Pagoda/Levels/InfiniteDisco/BP_DataLayerHelper_InfiniteDisco.BP_DataLayerHelper_InfiniteDisco_C:RequestArenaInternal"
local ARENA_SELECTION_COMMITTED =
    "/Game/Pagoda/Levels/InfiniteDisco/BP_DataLayerHelper_InfiniteDisco.BP_DataLayerHelper_InfiniteDisco_C:OnGameplayTagChanged"
local ARENA_SELECTION_COMMIT_TAG =
    "GameMode.Type.InfiniteDisco.Challenge"
local ARENA_CATALOGUE =
    "/Game/Pagoda/Levels/Arenas/DA_Arenas.DA_Arenas"
local RUNTIME_REGISTRATION_ENABLED = true

local manifests = {}
local manifestsByArenaId = {}
local rejectedManifestCount = 0
local catalogueRegistered = false
local registrationScheduled = false
local activeArenaId = nil
local arenaSelectionHookInstalled = false
local arenaSelectionHookPreId = nil
local arenaSelectionHookPostId = nil
local arenaCommitHookInstalled = false
local arenaCommitHookPreId = nil
local arenaCommitHookPostId = nil
local arenaRequestGeneration = 0

local function report(level, message)
    print(string.format(
        "[%s][%s] %s\n",
        MOD_NAME,
        level,
        tostring(message)))
end

local function trim(value)
    if type(value) ~= "string" then
        return nil
    end
    return value:match("^%s*(.-)%s*$")
end

local function isValid(object)
    if object == nil then
        return false
    end

    local ok, valid = pcall(function()
        return object:IsValid()
    end)
    return ok and valid == true
end

local function objectName(object)
    if not isValid(object) then
        return nil
    end

    local ok, value = pcall(function()
        return object:GetFullName()
    end)
    return ok and tostring(value) or nil
end

local function parameterValue(parameter)
    if parameter == nil then
        return nil
    end

    local ok, value = pcall(function()
        return parameter:get()
    end)
    return ok and value or parameter
end

local function booleanParameter(parameter)
    local value = parameterValue(parameter)
    return value == true or value == 1
end

local function memberValue(value, memberName)
    if value == nil then
        return nil
    end

    local ok, member = pcall(function()
        return value[memberName]
    end)
    return ok and member or nil
end

local function gameplayTagName(tagParameter)
    local tag = parameterValue(tagParameter)
    local tagName = memberValue(tag, "TagName")
    if tagName == nil then
        return nil
    end

    local ok, value = pcall(function()
        return tagName:ToString()
    end)
    if ok and value ~= nil then
        return tostring(value)
    end
    return tostring(tagName)
end

local function childDirectory(directory, name)
    if type(directory) ~= "table" then
        return nil
    end
    if type(directory[name]) == "table" then
        return directory[name]
    end

    local requested = name:lower()
    for key, value in pairs(directory) do
        if type(key) == "string" and
           type(value) == "table" and
           key:lower() == requested then
            return value
        end
    end
    return nil
end

local function findModsDirectory()
    local ok, directories = pcall(IterateGameDirectories)
    if not ok or type(directories) ~= "table" then
        return nil, "UE4SS could not enumerate the game directories"
    end

    local game = childDirectory(directories, "Game")
    local content = childDirectory(game, "Content")
    local paks = childDirectory(content, "Paks")
    local mods = childDirectory(paks, "~mods")
    if mods == nil then
        return nil, "Pagoda/Content/Paks/~mods was not found"
    end
    return mods
end

local function collectInfoFiles(directory, result)
    if type(directory) ~= "table" then
        return
    end

    for _, file in pairs(directory.__files or {}) do
        if type(file) == "table" and
           type(file.__name) == "string" and
           type(file.__absolute_path) == "string" and
           file.__name:lower() == "info.json" then
            result[#result + 1] = file.__absolute_path
        end
    end

    for name, child in pairs(directory) do
        if type(name) == "string" and
           name:sub(1, 2) ~= "__" and
           type(child) == "table" then
            collectInfoFiles(child, result)
        end
    end
end

local function readFile(path)
    local file, detail = io.open(path, "rb")
    if file == nil then
        return nil, detail
    end

    local contents = file:read("*a")
    file:close()
    return contents
end

local function validArenaId(arenaId)
    if arenaId == nil or #arenaId > 128 then
        return false
    end

    local segments = {}
    for segment in arenaId:gmatch("[^.]+") do
        segments[#segments + 1] = segment
        if not segment:match("^[A-Za-z0-9_]+$") then
            return false
        end
    end

    return #segments >= 5 and
        segments[1] == "Environment" and
        segments[2] == "Arena" and
        segments[3] == "Mod"
end

local function validDefinitionPath(path)
    if path == nil or #path > 256 then
        return false
    end

    local packagePath, objectName =
        path:match("^(/Game/[A-Za-z0-9_/]+)%.([A-Za-z0-9_]+)$")
    if packagePath == nil then
        return false
    end

    local packageName = packagePath:match("([^/]+)$")
    return packageName == objectName
end

local function validMapPath(path)
    return path ~= nil and
        #path <= 256 and
        path:match("^/Game/[A-Za-z0-9_/]+$") ~= nil and
        path:sub(-1) ~= "/"
end

local function isReservedPath(path)
    local lowered = path:lower()
    return lowered ==
        "/game/pagoda/levels/arenas/da_arenas.da_arenas" or
        lowered == "/game/pagoda/levels/arenas/da_arenas" or
        lowered == "/game/pagoda/levels/arenas/li_arenas" or
        lowered ==
        "/game/pagoda/levels/arenas/default/li_arena_default"
end

local function validateManifest(value, sourcePath)
    if type(value) ~= "table" then
        return nil, "the root value is not an object"
    end

    local arenaName = trim(value.ArenaName)
    local arenaId = trim(value.ArenaId)
    local arenaDefinition = trim(value.ArenaDefinition)
    local arenaMap = trim(value.ArenaMap)

    if arenaName == nil or
       arenaName == "" or
       #arenaName > 96 or
       arenaName:find("[%z\1-\31]") then
        return nil, "ArenaName must be a printable string of 1-96 bytes"
    end
    if not validArenaId(arenaId) then
        return nil,
            "ArenaId must be a unique Environment.Arena.Mod.Creator.Arena tag"
    end
    if not validDefinitionPath(arenaDefinition) then
        return nil,
            "ArenaDefinition must be a full /Game package.object path"
    end
    if not validMapPath(arenaMap) then
        return nil,
            "ArenaMap must be a /Game long package path without an object suffix"
    end
    if isReservedPath(arenaDefinition) or isReservedPath(arenaMap) then
        return nil, "stock arena packages cannot be used by an arena slot"
    end

    return {
        name = arenaName,
        id = arenaId,
        definition = arenaDefinition,
        map = arenaMap,
        mapObject = arenaMap .. "." .. arenaMap:match("([^/]+)$"),
        source = sourcePath
    }
end

local function discoverManifests()
    manifests = {}
    manifestsByArenaId = {}
    rejectedManifestCount = 0

    local modsDirectory, detail = findModsDirectory()
    if modsDirectory == nil then
        report("INFO", detail)
        return
    end

    local files = {}
    collectInfoFiles(modsDirectory, files)
    table.sort(files, function(left, right)
        return left:lower() < right:lower()
    end)

    local claimedDefinitions = {}
    local claimedMaps = {}

    for _, path in ipairs(files) do
        local contents, readDetail = readFile(path)
        local decoded = nil
        local decodeDetail = nil
        if contents ~= nil then
            decoded, decodeDetail = json.decode(contents)
        end
        local manifest, validationDetail =
            validateManifest(decoded, path)

        local rejection =
            readDetail or
            decodeDetail or
            validationDetail

        if manifest ~= nil then
            if manifestsByArenaId[manifest.id] ~= nil then
                rejection = "ArenaId is already claimed"
            elseif claimedDefinitions[
                    manifest.definition:lower()] then
                rejection = "ArenaDefinition is already claimed"
            elseif claimedMaps[manifest.map:lower()] then
                rejection = "ArenaMap is already claimed"
            end
        end

        if rejection ~= nil then
            if decoded ~= nil and
               type(decoded) == "table" and
               decoded.ArenaName ~= nil then
                rejectedManifestCount =
                    rejectedManifestCount + 1
                report(
                    "WARN",
                    string.format(
                        "Rejected %s: %s",
                        path,
                        tostring(rejection)))
            end
        else
            manifests[#manifests + 1] = manifest
            manifestsByArenaId[manifest.id] = manifest
            claimedDefinitions[
                manifest.definition:lower()] = true
            claimedMaps[manifest.map:lower()] = true
        end
    end

    table.sort(manifests, function(left, right)
        return left.id:lower() < right.id:lower()
    end)

    report(
        "INFO",
        string.format(
            "Discovered %d arena slot(s); rejected %d manifest(s).",
            #manifests,
            rejectedManifestCount))
end

local function isLoaderActor(actor)
    if not isValid(actor) then
        return false
    end

    -- I identify only our generated class before touching loader state because
    -- querying an arbitrary actor's Blueprint fields can outlive its metadata.
    local ok, actorClass = pcall(function()
        return actor:GetClass()
    end)
    return ok and objectName(actorClass) == LOADER_ACTOR_CLASS
end

local function belongsToCurrentWorld(actor)
    if not isValid(actor) then
        return false
    end

    local currentWorld = UEHelpers.GetWorld()
    if not isValid(currentWorld) then
        return true
    end

    local ok, actorWorld = pcall(function()
        return actor:GetWorld()
    end)
    if not ok or not isValid(actorWorld) then
        return true
    end

    return actorWorld:GetFullName() ==
        currentWorld:GetFullName()
end

local function findLoaderActor()
    local actors = FindAllOf("ModActor_C")
    if type(actors) ~= "table" then
        return nil
    end

    for _, actor in ipairs(actors) do
        if isLoaderActor(actor) and
           belongsToCurrentWorld(actor) then
            return actor
        end
    end
    return nil
end

local function findLoadedObject(path)
    local ok, object = pcall(function()
        return StaticFindObject(path)
    end)
    if ok and isValid(object) then
        return object
    end
    return nil
end

local function arenaCatalogueDefinitions()
    local catalogue = findLoadedObject(ARENA_CATALOGUE)
    if catalogue == nil then
        return nil
    end

    local definitions = memberValue(
        catalogue,
        "ArenaDefinitions")
    if definitions == nil then
        return nil
    end
    return definitions
end

local function addArenaDefinition(loaderActor, manifest)
    local definitionsBefore = arenaCatalogueDefinitions()
    local countBefore = definitionsBefore ~= nil and
        #definitionsBefore or 0

    -- I let the Logic Mod load container assets because UE4SS's asset
    -- registry does not index standalone mod containers.
    loaderActor:AddArenaDefinition(manifest.definition)

    local definitions = arenaCatalogueDefinitions()
    if definitions == nil or #definitions <= countBefore then
        return false,
            "the Logic Mod did not append an arena catalogue entry"
    end

    -- I repair the appended copy because cooked assets discard gameplay tags
    -- that are not part of the base game's tag dictionary.
    local definition = definitions[#definitions]
    local arenaTag = memberValue(definition, "ArenaIdTag")
    if arenaTag == nil then
        return false, "the catalogue entry does not expose ArenaIdTag"
    end

    arenaTag.TagName = FName(manifest.id)
    local restoredId = gameplayTagName(arenaTag)
    if restoredId ~= manifest.id then
        return false,
            string.format(
                "ArenaId resolved as %s instead of %s",
                tostring(restoredId),
                manifest.id)
    end

    return true
end

local function registerManifests(loaderActor)
    if catalogueRegistered or
       not isLoaderActor(loaderActor) or
       #manifests == 0 then
        return
    end

    local registeredCount = 0

    for _, manifest in ipairs(manifests) do
        local ok, detail = pcall(function()
            local restored, restoreDetail =
                addArenaDefinition(loaderActor, manifest)
            if not restored then
                error(restoreDetail)
            end
        end)

        if not ok then
            report(
                "ERROR",
                string.format(
                    "Could not register %s: %s",
                    manifest.name,
                    tostring(detail)))
        else
            registeredCount = registeredCount + 1
        end
    end

    catalogueRegistered = registeredCount == #manifests
    report(
        catalogueRegistered and "INFO" or "WARN",
        string.format(
            "Registered %d of %d arena slot(s) for this game session.",
            registeredCount,
            #manifests))
end

local function scheduleManifestRegistration(delayMilliseconds)
    if catalogueRegistered or
       registrationScheduled or
       #manifests == 0 then
        return
    end

    registrationScheduled = true

    -- I defer catalogue mutation until UGameEngine has completed its initial
    -- tick so the loader never races the game's arena subsystem startup.
    ExecuteWithDelay(delayMilliseconds, function()
        ExecuteInGameThread(function()
            registrationScheduled = false

            local loaderActor = findLoaderActor()
            if loaderActor ~= nil then
                registerManifests(loaderActor)
            end
        end)
    end)
end

local function scheduleArenaSelection(
    contextParameter,
    arenaIdParameter)
    local arenaId = gameplayTagName(arenaIdParameter)
    report(
        "INFO",
        "Observed arena request for " .. tostring(arenaId) .. ".")

    local manifest = arenaId ~= nil and
        manifestsByArenaId[arenaId] or nil
    if manifest == nil and activeArenaId == nil then
        return
    end

    local worldContext = parameterValue(contextParameter)
    if not isValid(worldContext) then
        report("ERROR", "Arena selection did not provide a valid world context.")
        return
    end

    local worldOk, requestWorld = pcall(function()
        return worldContext:GetWorld()
    end)
    local requestWorldName = worldOk and objectName(requestWorld) or nil
    if requestWorldName == nil or
       not requestWorldName:find(
           "L_InfiniteDisco_Persistent",
           1,
           true) then
        return
    end

    local loader = findLoaderActor()
    local activeStream = loader ~= nil and
        memberValue(loader, "ActiveArenaStream") or nil
    if manifest ~= nil and
       activeArenaId == manifest.id and
       isValid(activeStream) then
        return
    end

    arenaRequestGeneration = arenaRequestGeneration + 1
    local requestGeneration = arenaRequestGeneration

    -- I defer stream changes until the game's original request has returned
    -- so its transition state is never re-entered or replayed by the loader.
    ExecuteWithDelay(1, function()
        ExecuteInGameThread(function()
            if requestGeneration ~= arenaRequestGeneration or
               not isValid(worldContext) then
                return
            end

            local currentLoader = findLoaderActor()
            if currentLoader == nil then
                if manifest ~= nil then
                    report(
                        "ERROR",
                        "The Arena Slot Loader actor was unavailable after selection.")
                end
                return
            end

            local ok, detail = pcall(function()
                currentLoader:DeactivateArenaMap(worldContext)

                if manifest ~= nil then
                    currentLoader:ActivateArenaMap(
                        manifest.mapObject,
                        worldContext)

                    local stream = memberValue(
                        currentLoader,
                        "ActiveArenaStream")
                    if not isValid(stream) then
                        error("LoadLevelInstance did not create a streaming level")
                    end

                    local ready, loaded, visible = pcall(function()
                        return stream:IsLevelLoaded(),
                            stream:IsLevelVisible()
                    end)
                    if not ready or not loaded or not visible then
                        error("the arena stream did not become loaded and visible")
                    end
                end
            end)

            if not ok then
                activeArenaId = nil
                report(
                    "ERROR",
                    string.format(
                        "Could not apply arena selection %s: %s",
                        tostring(arenaId),
                        tostring(detail)))
                return
            end

            activeArenaId = manifest ~= nil and manifest.id or nil
            if manifest == nil then
                report(
                    "INFO",
                    "Deactivated the custom arena stream for a stock arena.")
            else
                report(
                    "INFO",
                    string.format(
                        "Loaded and made visible %s from %s.",
                        manifest.name,
                        manifest.map))
            end
        end)
    end)
end

local function scheduleCommittedArenaSelection(contextParameter)
    local arenaHelper = parameterValue(contextParameter)
    if not isValid(arenaHelper) then
        report("ERROR", "Arena commit did not provide a valid helper actor.")
        return
    end

    arenaRequestGeneration = arenaRequestGeneration + 1
    local commitGeneration = arenaRequestGeneration

    -- I wait for ShowSelectedStaticArena to update LastRequestedArena before
    -- I mirror the game's committed choice into the custom streaming level.
    ExecuteWithDelay(25, function()
        ExecuteInGameThread(function()
            if commitGeneration ~= arenaRequestGeneration or
               not isValid(arenaHelper) then
                return
            end

            local selectedArena = memberValue(
                arenaHelper,
                "LastRequestedArena")
            if gameplayTagName(selectedArena) == nil then
                report(
                    "ERROR",
                    "The committed arena did not expose LastRequestedArena.")
                return
            end

            scheduleArenaSelection(arenaHelper, selectedArena)
        end)
    end)
end

local function unregisterArenaSelectionHook()
    -- I remove both Blueprint hooks before their package unloads so UE4SS
    -- never retains a UFunction from the outgoing Infinite Disco world.
    if arenaSelectionHookInstalled then
        local unregistered, detail = pcall(function()
            UnregisterHook(
                ARENA_SELECTION_REQUEST,
                arenaSelectionHookPreId,
                arenaSelectionHookPostId)
        end)

        if not unregistered then
            report(
                "WARN",
                "Arena-selection hook could not be removed: " ..
                tostring(detail))
        end

        arenaSelectionHookInstalled = false
        arenaSelectionHookPreId = nil
        arenaSelectionHookPostId = nil
    end

    if arenaCommitHookInstalled then
        local unregistered, detail = pcall(function()
            UnregisterHook(
                ARENA_SELECTION_COMMITTED,
                arenaCommitHookPreId,
                arenaCommitHookPostId)
        end)

        if not unregistered then
            report(
                "WARN",
                "Arena-commit hook could not be removed: " ..
                tostring(detail))
        end

        arenaCommitHookInstalled = false
        arenaCommitHookPreId = nil
        arenaCommitHookPostId = nil
    end
end

local function deactivateArenaBeforeTravel()
    local loader = findLoaderActor()
    if loader == nil then
        return
    end

    local activeStream = memberValue(loader, "ActiveArenaStream")
    if not isValid(activeStream) then
        return
    end

    -- I drain the outgoing stream while its world is still valid so no custom
    -- level package remains pending during travel to the Dive Bar.
    local deactivated, detail = pcall(function()
        loader:DeactivateArenaMap(loader)
    end)
    if not deactivated then
        report(
            "WARN",
            "The custom arena stream could not be removed before travel: " ..
            tostring(detail))
        return
    end

    report("INFO", "Removed the custom arena stream before map travel.")
end

local function tryRegisterArenaSelectionHook(gameStateParameter)
    local gameState = parameterValue(gameStateParameter)
    if not isValid(gameState) then
        return
    end

    local worldOk, world = pcall(function()
        return gameState:GetWorld()
    end)
    local worldName = worldOk and objectName(world) or nil
    if worldName == nil or
       not worldName:find("L_InfiniteDisco_Persistent", 1, true) then
        return
    end

    if arenaSelectionHookInstalled or
       findLoadedObject(ARENA_SELECTION_REQUEST) == nil then
        return
    end

    -- I install this script hook only after Infinite Disco has loaded its
    -- helper class so menu and Dive Bar data-layer traffic stays untouched.
    local hookOk, preId, postId = pcall(function()
        return RegisterHook(
            ARENA_SELECTION_REQUEST,
            function(
                contextParameter,
                arenaIdParameter)
                scheduleArenaSelection(
                    contextParameter,
                    arenaIdParameter)
            end)
    end)

    if not hookOk then
        report(
            "ERROR",
            "RequestArenaInternal hook could not be installed: " ..
            tostring(preId))
        return
    end

    arenaSelectionHookPreId = preId
    arenaSelectionHookPostId = postId
    arenaSelectionHookInstalled = true
    report("INFO", "Infinite Disco arena-selection hook installed.")
end

local function tryRegisterArenaCommitHook(gameStateParameter)
    local gameState = parameterValue(gameStateParameter)
    if not isValid(gameState) then
        return
    end

    local worldOk, world = pcall(function()
        return gameState:GetWorld()
    end)
    local worldName = worldOk and objectName(world) or nil
    if worldName == nil or
       not worldName:find("L_InfiniteDisco_Persistent", 1, true) then
        return
    end

    if arenaCommitHookInstalled or
       findLoadedObject(ARENA_SELECTION_COMMITTED) == nil then
        return
    end

    local hookOk, preId, postId = pcall(function()
        return RegisterHook(
            ARENA_SELECTION_COMMITTED,
            function(
                contextParameter,
                tagParameter,
                tagExistsParameter)
                local tagName = gameplayTagName(tagParameter)
                if tagName == ARENA_SELECTION_COMMIT_TAG and
                   not booleanParameter(tagExistsParameter) then
                    -- I use the challenge-tag exit because it is the game's
                    -- stable arena handoff, after browsing has finished.
                    scheduleCommittedArenaSelection(contextParameter)
                end
            end)
    end)

    if not hookOk then
        report(
            "ERROR",
            "Arena-commit hook could not be installed: " ..
            tostring(preId))
        return
    end

    arenaCommitHookPreId = preId
    arenaCommitHookPostId = postId
    arenaCommitHookInstalled = true
    report("INFO", "Infinite Disco arena-commit hook installed.")
end

if RUNTIME_REGISTRATION_ENABLED then
    discoverManifests()

    RegisterInitGameStatePostHook(function(gameStateParameter)
        tryRegisterArenaSelectionHook(gameStateParameter)
        tryRegisterArenaCommitHook(gameStateParameter)
    end)

    RegisterBeginPlayPostHook(function(contextParameter)
        local actor = parameterValue(contextParameter)
        if isLoaderActor(actor) then
            scheduleManifestRegistration(250)
        end
    end)

    RegisterLoadMapPreHook(function()
        arenaRequestGeneration = arenaRequestGeneration + 1
        deactivateArenaBeforeTravel()
        activeArenaId = nil
        unregisterArenaSelectionHook()
    end)

    RegisterLoadMapPostHook(function()
        discoverManifests()
        scheduleManifestRegistration(500)
    end)
else
    report(
        "WARN",
        "Runtime registration is disabled.")
end
