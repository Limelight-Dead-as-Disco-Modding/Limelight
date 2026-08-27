local UEHelpers = require("UEHelpers")

local MOD_NAME = "LimelightMPLocalRenderHost"
local MOD_VERSION = "0.1.4"
local VERSION_WATERMARK = "LimelightMP_0.1.4 By Henreh <3"
local HOST_MAP = "/Game/Pagoda/Levels/DiveBar/L_DiveBar_V2"
local INFINITE_DISCO_MAP = "/Game/Pagoda/Levels/InfiniteDisco/L_InfiniteDisco_Persistent"
local LISTEN_PORT = 7777 -- INSTALL_LISTEN_PORT
local CONNECT_ADDRESS = "127.0.0.1:7777" -- INSTALL_CONNECT_ADDRESS

local armed = false
local worldTransitioning = false
local monitorGeneration = 0
local configuredCouchPawnNames = {}
local neutralizedProxyName = nil
local orientationSourceControllerName = nil
local orientationSourceFailureReported = false
local controllerSeatsReported = false
local statusFailureReported = false
local hostMenuViewEnabled = false
local controllerCacheReady = false
local controllerCacheWorldName = nil
local cachedControllers = {}
local cachedLocalControllers = {}
local cachedRemoteControllers = {}
local automaticRescueCooldownChecks = 0
local dialogueFilterHookRegistered = false
local dialogueFilterFailureReported = false
local suppressedDialogueWidgets = {}
local observedDialogueComponents = {}
local focusedHostDialogueWidgets = {}
local activePlayerOneDialogueWidget = nil
local activePlayerOneDialogueController = nil
local movementOrientationHookRegistered = false
local movementOrientationFailureReported = false
local movementOrientationActiveReported = false
local couchSkillOverrideHookRegistered = false
local couchSkillOverrideFailureReported = false
local couchSkillOverrideActiveReported = false
local cachedMovementHostYaw = nil
local cachedMovementClientYaw = nil
local orientationSourcePausedForCameraOwnership = false
local supportedWorldCached = nil
local lastCharlieTransitionX = nil
local lastCharlieTransitionY = nil
local lastCharlieTransitionZ = nil
local versionWatermarkHookRegistered = false
local versionWatermarkObjectName = nil
local hazardReplicationHooksRegistered = false
local replicatedHazardActors = {}
local personalMenuHooksRegistered = false
local preparedPersonalMenus = {}
local hudPresentationHooksRegistered = false
local preparedHudWidgets = {}
local deferredHookPaths = {}
local deferredHookFailures = {}

local CHUCKLES_FALL_DISTANCE_Z = 900.0
local CHARLIE_TRANSITION_DISTANCE_SQUARED = 640000.0
local CHARLIE_TRANSITION_VERTICAL_DISTANCE = 350.0

local function log(message)
    print(string.format("[%s] %s\n", MOD_NAME, tostring(message)))
end

local function report(message)
    print(string.format("[%s][REPORT] %s\n", MOD_NAME, tostring(message)))
end

local function isValid(object)
    if object == nil then
        return false
    end

    local ok, valid = pcall(function()
        return object:IsValid()
    end)
    return ok and valid
end

local function objectName(object)
    if not isValid(object) then
        return "<invalid>"
    end

    local ok, name = pcall(function()
        return object:GetFullName()
    end)
    return ok and tostring(name) or "<name unavailable>"
end

local function className(object)
    if not isValid(object) then
        return "<invalid>"
    end

    local ok, name = pcall(function()
        return object:GetClass():GetFullName()
    end)
    return ok and tostring(name) or "<class unavailable>"
end

local function readProperty(object, propertyName)
    if not isValid(object) then
        return nil
    end

    local ok, value = pcall(function()
        return object[propertyName]
    end)
    return ok and value or nil
end

local function sameWorld(object, world)
    if not isValid(object) or not isValid(world) then
        return false
    end

    local ok, objectWorld = pcall(function()
        return object:GetWorld()
    end)
    return ok and isValid(objectWorld) and objectName(objectWorld) == objectName(world)
end

local function isLocalController(controller)
    if not isValid(controller) then
        return false
    end

    local ok, localController = pcall(function()
        return controller:IsLocalPlayerController()
    end)
    return ok and localController
end

local function controllerId(controller)
    local player = readProperty(controller, "Player")
    local id = readProperty(player, "ControllerId")
    return tonumber(id) or -1
end

local function clearControllerCache()
    controllerCacheReady = false
    controllerCacheWorldName = nil
    cachedControllers = {}
    cachedLocalControllers = {}
    cachedRemoteControllers = {}
end

local function refreshControllerCache()
    local world = UEHelpers.GetWorld()
    local result = {}
    local ok, controllers = pcall(function()
        return FindAllOf("PlayerController") or {}
    end)
    if not ok then
        return result
    end

    for _, controller in ipairs(controllers) do
        if isValid(controller) and sameWorld(controller, world) then
            table.insert(result, controller)
        end
    end

    table.sort(result, function(left, right)
        local leftLocal = isLocalController(left)
        local rightLocal = isLocalController(right)
        if leftLocal ~= rightLocal then
            return leftLocal
        end
        return controllerId(left) < controllerId(right)
    end)
    cachedControllers = result
    cachedLocalControllers = {}
    cachedRemoteControllers = {}
    for _, controller in ipairs(result) do
        if isLocalController(controller) then
            table.insert(cachedLocalControllers, controller)
        else
            table.insert(cachedRemoteControllers, controller)
        end
    end
    controllerCacheWorldName = objectName(world)
    controllerCacheReady = true
    return cachedControllers
end

local function controllerCacheIsCurrent()
    if not controllerCacheReady or controllerCacheWorldName ~= objectName(UEHelpers.GetWorld()) then
        return false
    end
    for _, controller in ipairs(cachedControllers) do
        if not isValid(controller) then
            return false
        end
    end
    return true
end

local function getControllers()
    if not controllerCacheIsCurrent() then
        refreshControllerCache()
    end
    return cachedControllers
end

local function getLocalControllers()
    getControllers()
    return cachedLocalControllers
end

local function getRemoteControllers()
    getControllers()
    return cachedRemoteControllers
end

local function showStatus(message, durationSeconds, color)
    local world = UEHelpers.GetWorld()
    local systemLibrary = UEHelpers.GetKismetSystemLibrary()
    if not isValid(world) or not isValid(systemLibrary) then
        return
    end

    local textColor = color or { R = 0.25, G = 0.90, B = 1.00, A = 1.00 }
    local ok, detail = pcall(function()
        systemLibrary:PrintString(
            world,
            "[LIMELIGHT MP] " .. tostring(message),
            true,
            false,
            textColor,
            durationSeconds or 6.0,
            UEHelpers.FindOrAddFName("LimelightMPLocalRenderHostStatus"))
    end)
    if not ok and not statusFailureReported then
        statusFailureReported = true
        report("status=failed detail=" .. tostring(detail))
    end
end

local function supportedWorld()
    if supportedWorldCached ~= nil then
        return supportedWorldCached
    end
    local worldName = string.lower(objectName(UEHelpers.GetWorld()))
    supportedWorldCached = string.find(worldName, "/startup/", 1, true) == nil and
        string.find(worldName, "/main_menu/", 1, true) == nil and
        worldName ~= "<invalid>"
    return supportedWorldCached
end

local function findNetDriver()
    local world = UEHelpers.GetWorld()
    local driver = readProperty(world, "NetDriver")
    if isValid(driver) then
        return driver
    end

    local ok, drivers = pcall(function()
        return FindAllOf("NetDriver") or {}
    end)
    if ok then
        for _, candidate in ipairs(drivers) do
            if isValid(candidate) and
               sameWorld(candidate, world) and
               string.find(tostring(readProperty(candidate, "NetDriverName")), "GameNetDriver", 1, true) then
                return candidate
            end
        end
    end
    return nil
end

local function configureViewport()
    local settings = UEHelpers.GetGameMapsSettings()
    if not isValid(settings) then
        return false
    end

    -- The host continues to run the game's real couch co-op simulation.
    -- Physical XInput slot 0 is Charlie and LimelightMP's virtual slot 1 is Chuckles.
    settings.bOffsetPlayerGamepadIds = false
    settings.bUseSplitscreen = true
    return true
end

local function pinControllerSeats(reason)
    local charlieController = nil
    local chucklesController = nil
    local nonChucklesController = nil
    for _, controller in ipairs(getLocalControllers()) do
        local pawn = readProperty(controller, "Pawn")
        local pawnIdentity = string.lower(objectName(pawn) .. " " .. className(pawn))
        if string.find(pawnIdentity, "chuckles", 1, true) then
            chucklesController = controller
        elseif isValid(pawn) then
            nonChucklesController = controller
            if string.find(pawnIdentity, "charlie", 1, true) then
                charlieController = controller
            end
        end
    end
    charlieController = charlieController or nonChucklesController

    if not isValid(charlieController) or not isValid(chucklesController) then
        return false
    end

    local ok, detail = pcall(function()
        local charliePlayer = readProperty(charlieController, "Player")
        local chucklesPlayer = readProperty(chucklesController, "Player")
        if not isValid(charliePlayer) or not isValid(chucklesPlayer) then
            error("local-player-missing")
        end
        charliePlayer.ControllerId = 0
        chucklesPlayer.ControllerId = 1
    end)
    if ok and not controllerSeatsReported then
        controllerSeatsReported = true
        report(string.format(
            "controller_seats=pinned reason=%s charlie=%s:0 chuckles=%s:1",
            tostring(reason),
            objectName(charlieController),
            objectName(chucklesController)))
    elseif not ok then
        report("controller_seats=failed detail=" .. tostring(detail))
    end
    return ok
end

local function findCouchCharacters()
    local charlie = nil
    local chuckles = nil
    local fallbackPrimary = nil
    for _, controller in ipairs(getLocalControllers()) do
        local pawn = readProperty(controller, "Pawn")
        if isValid(pawn) then
            local identity = string.lower(objectName(pawn) .. " " .. className(pawn))
            if string.find(identity, "chuckles", 1, true) then
                chuckles = pawn
            else
                fallbackPrimary = fallbackPrimary or pawn
                if string.find(identity, "charlie", 1, true) then
                    charlie = pawn
                end
            end
        end
    end
    return charlie or fallbackPrimary, chuckles
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

local function registerDeferredHook(path, callback)
    if deferredHookPaths[path] then
        return true, nil, nil, false
    end

    local ok, preId, postId = pcall(function()
        return RegisterHook(path, callback)
    end)
    if ok then
        deferredHookPaths[path] = true
        deferredHookFailures[path] = nil
        return true, preId, postId, true
    end

    local firstFailure = not deferredHookFailures[path]
    deferredHookFailures[path] = true
    return false, preId, postId, firstFailure
end

local function applyVersionWatermark(layout, reason)
    if not isValid(layout) then
        return false
    end
    local textLibrary = UEHelpers.GetKismetTextLibrary()
    if not isValid(textLibrary) then
        return false
    end
    local buildId = readProperty(layout, "BuildId")
    if not isValid(buildId) then
        return false
    end

    local ok, detail = pcall(function()
        buildId:SetText(textLibrary:Conv_StringToText(VERSION_WATERMARK))
    end)
    if ok then
        local widgetName = objectName(buildId)
        if versionWatermarkObjectName ~= widgetName then
            versionWatermarkObjectName = widgetName
            report(string.format(
                "watermark=applied reason=%s layout=%s widget=%s text=%s",
                tostring(reason),
                objectName(layout),
                widgetName,
                VERSION_WATERMARK))
        end
    else
        report("watermark=apply-failed detail=" .. tostring(detail))
    end
    return ok
end

local function installVersionWatermark()
    if versionWatermarkHookRegistered then
        return
    end

    local path = "/Game/Pagoda/UI/Game/UI_Layout_Game.UI_Layout_Game_C:OnInitialized"
    local ok, preId, postId, changed = registerDeferredHook(path, function(layoutParameter)
        local layout = parameterValue(layoutParameter)
        ExecuteInGameThreadWithDelay(1, function()
            applyVersionWatermark(layout, "game-layout-initialized")
        end)
    end)
    versionWatermarkHookRegistered = ok
    if changed then
        report(string.format(
            "%s pre=%s post=%s detail=%s",
            ok and "watermark=layout-hooked" or "watermark=layout-hook-deferred",
            tostring(preId),
            tostring(postId),
            ok and "nil" or tostring(preId)))
    end
end

local function applyVersionWatermarkToLoadedLayouts(reason)
    local ok, layouts = pcall(function()
        return FindAllOf("UI_Layout_Game_C") or {}
    end)
    if not ok then
        return false
    end

    local applied = false
    for _, layout in ipairs(layouts) do
        if applyVersionWatermark(layout, reason) then
            applied = true
        end
    end
    return applied
end

local function findChucklesController()
    for _, controller in ipairs(getLocalControllers()) do
        local pawn = readProperty(controller, "Pawn")
        local identity = string.lower(objectName(pawn) .. " " .. className(pawn))
        if isValid(pawn) and string.find(identity, "chuckles", 1, true) then
            return controller, pawn
        end
    end
    return nil, nil
end

local function rotationYaw(rotation)
    if rotation == nil then
        return nil
    end
    local ok, yaw = pcall(function()
        return tonumber(rotation.Yaw)
    end)
    return ok and yaw or nil
end

local function controllerCameraYaw(controller, preferCameraManager)
    if not isValid(controller) then
        return nil
    end

    if preferCameraManager then
        local manager = readProperty(controller, "PlayerCameraManager")
        if isValid(manager) then
            local ok, rotation = pcall(function()
                return manager:GetCameraRotation()
            end)
            local yaw = ok and rotationYaw(rotation) or nil
            if yaw ~= nil then
                return yaw
            end
        end
    end

    local ok, rotation = pcall(function()
        return controller:GetControlRotation()
    end)
    return ok and rotationYaw(rotation) or nil
end

local function orientChucklesMovement(pawnParameter, returnParameter)
    if not armed or worldTransitioning then
        return
    end

    local pawn = parameterValue(pawnParameter)
    if not isValid(pawn) then
        return
    end

    local pawnController = nil
    pcall(function()
        pawnController = pawn:GetController()
    end)
    if not isValid(pawnController) or controllerId(pawnController) ~= 1 then
        return
    end

    -- Disabling split-screen makes Pagoda convert Player 2's stick through
    -- Charlie's visible viewport direction. Undo that yaw, then apply the
    -- friend's independently rendered camera yaw before movement is consumed.
    local localControllers = cachedLocalControllers
    local remoteControllers = cachedRemoteControllers
    if #localControllers < 2 or #remoteControllers == 0 then
        return
    end

    local hostYaw = cachedMovementHostYaw
    local clientYaw = cachedMovementClientYaw
    if hostYaw == nil or clientYaw == nil then
        return
    end

    local movement = parameterValue(returnParameter)
    if movement == nil then
        return
    end

    local ok, detail = pcall(function()
        local x = tonumber(movement.X) or 0.0
        local y = tonumber(movement.Y) or 0.0
        if math.abs(x) + math.abs(y) < 0.0001 then
            return
        end

        local radians = math.rad(clientYaw - hostYaw)
        local cosine = math.cos(radians)
        local sine = math.sin(radians)
        movement.X = x * cosine - y * sine
        movement.Y = x * sine + y * cosine
        returnParameter:set(movement)

        if not movementOrientationActiveReported then
            movementOrientationActiveReported = true
            report(string.format(
                "movement_orientation=active hostYaw=%.2f clientYaw=%.2f delta=%.2f",
                hostYaw,
                clientYaw,
                clientYaw - hostYaw))
        end
    end)

    if not ok and not movementOrientationFailureReported then
        movementOrientationFailureReported = true
        report("movement_orientation=failed detail=" .. tostring(detail))
    end
end

local function enableChucklesSkillChecks(componentParameter, upgradeParameter, returnParameter)
    if not armed or worldTransitioning then
        return
    end

    local component = parameterValue(componentParameter)
    if not isValid(component) then
        return
    end

    local owner = nil
    pcall(function()
        owner = component:GetOwner()
    end)
    if not isValid(owner) then
        return
    end

    local ownerController = nil
    pcall(function()
        ownerController = owner:GetController()
    end)
    if not isValid(ownerController) or controllerId(ownerController) ~= 1 then
        return
    end

    local ok, detail = pcall(function()
        -- Chuckles is host-owned, so changing the friend's disk save cannot
        -- affect gameplay. Treat every regular skill-tree upgrade as equipped
        -- for couch Player 2 during this LimelightMP session instead.
        returnParameter:set(true)
    end)
    if ok and not couchSkillOverrideActiveReported then
        couchSkillOverrideActiveReported = true
        report("couch_skills=all-equipped-session-override controllerId=1")
    elseif not ok and not couchSkillOverrideFailureReported then
        couchSkillOverrideFailureReported = true
        report("couch_skills=override-failed detail=" .. tostring(detail))
    end
end

local function installCouchGameplayHooks()
    if not movementOrientationHookRegistered then
        local ok, preId, postId = pcall(function()
            return RegisterHook(
                "/Script/Pagoda.PagodaPlayerCharacter:GetMoveInputInWorldSpace",
                function()
                    -- Native return values require both pre- and post-hooks.
                end,
                function(pawnParameter, returnParameter)
                    orientChucklesMovement(pawnParameter, returnParameter)
                end)
        end)
        movementOrientationHookRegistered = ok
        report(string.format(
            "movement_orientation=hooked ok=%s pre=%s post=%s detail=%s",
            tostring(ok),
            tostring(preId),
            tostring(postId),
            ok and "nil" or tostring(preId)))
    end

    if not couchSkillOverrideHookRegistered then
        local ok, preId, postId = pcall(function()
            return RegisterHook(
                "/Script/Pagoda.PagodaPlayerUpgradeComponent:IsUpgradeEquipped",
                function()
                    -- Native return values require both pre- and post-hooks.
                end,
                function(componentParameter, upgradeParameter, returnParameter)
                    enableChucklesSkillChecks(
                        componentParameter,
                        upgradeParameter,
                        returnParameter)
                end)
        end)
        couchSkillOverrideHookRegistered = ok
        report(string.format(
            "couch_skills=hooked ok=%s pre=%s post=%s detail=%s",
            tostring(ok),
            tostring(preId),
            tostring(postId),
            ok and "nil" or tostring(preId)))
    end
end

local function menuOwningController(widget)
    if not isValid(widget) then
        return nil
    end
    local owner = nil
    pcall(function()
        owner = widget:GetOwningPlayer()
    end)
    return isValid(owner) and owner or nil
end

local function menuSaveContext(widget, owner)
    local localPlayer = nil
    pcall(function()
        localPlayer = widget:GetOwningLocalPlayer()
    end)
    if not isValid(localPlayer) and isValid(owner) then
        localPlayer = readProperty(owner, "Player")
    end
    local playthroughData = readProperty(localPlayer, "PlaythroughData")
    return localPlayer, playthroughData
end

local function desiredMenuFocus(widget)
    local focusTarget = nil
    pcall(function()
        focusTarget = widget:BP_GetDesiredFocusTarget()
    end)
    if isValid(focusTarget) then
        return focusTarget
    end

    for _, propertyName in ipairs({
        "ResumeButton",
        "SkillTreeButton",
        "CosmeticsButton",
        "ExitAction",
        "W_ExitAction"
    }) do
        local candidate = readProperty(widget, propertyName)
        if isValid(candidate) then
            return candidate
        end
    end
    return widget
end

local function focusOwnedPlayerMenu(widget, reason)
    if not armed or worldTransitioning or not isValid(widget) then
        return false
    end
    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    local owner = menuOwningController(widget)
    if not isValid(owner) then
        return false
    end
    local ownerId = controllerId(owner)
    if ownerId ~= 0 and ownerId ~= 1 then
        return false
    end

    -- The host owns both real couch players. Player 1 is the friend's
    -- authoritative Chuckles menu mirror: keep it active and focused so its
    -- choices reach gameplay, but never draw it over Charlie's host screen.
    -- Render opacity deliberately preserves CommonUI input/hit testing.
    local ownerIsLocal = isLocalController(owner)
    local localPlayer, playthroughData = menuSaveContext(widget, owner)
    local visibleOnHost = ownerIsLocal and ownerId == 0 and isValid(playthroughData)
    local renderOk, renderDetail = pcall(function()
        widget:SetRenderOpacity(visibleOnHost and 1.0 or 0.0)
    end)

    if not ownerIsLocal then
        if not preparedPersonalMenus[widgetName] then
            preparedPersonalMenus[widgetName] = true
            report(string.format(
                "personal_menu=remote-suppressed reason=%s widget=%s owner=%s render=%s detail=%s",
                tostring(reason),
                widgetName,
                objectName(owner),
                tostring(renderOk),
                renderOk and "nil" or tostring(renderDetail)))
        end
        return renderOk
    end
    if ownerId == 0 and not isValid(playthroughData) then
        -- Never show a host menu against an unbound/default profile. A later
        -- delayed activation pass makes it visible once the save is attached.
        return false
    end

    local inputOk = pcall(function()
        widget.bUsePlayerInput = true
        widget:UpdateInputType()
    end)
    local refreshOk = pcall(function()
        widget:RequestRefreshFocus()
    end)
    local focusTarget = desiredMenuFocus(widget)
    local focusOk, focusDetail = pcall(function()
        focusTarget:SetUserFocus(owner)
    end)
    if focusOk then
        if not preparedPersonalMenus[widgetName] then
            preparedPersonalMenus[widgetName] = true
            report(string.format(
                "personal_menu=%s reason=%s widget=%s controllerId=%d localPlayer=%s save=%s focus=%s render=%s input=%s refresh=%s",
                visibleOnHost and "host-visible" or "client-mirror-hidden",
                tostring(reason),
                widgetName,
                ownerId,
                objectName(localPlayer),
                isValid(playthroughData) and objectName(playthroughData) or "<missing>",
                objectName(focusTarget),
                tostring(renderOk),
                tostring(inputOk),
                tostring(refreshOk)))
        end
        return true
    end

    report(string.format(
        "personal_menu=focus-failed reason=%s widget=%s controllerId=%d detail=%s",
        tostring(reason),
        widgetName,
        ownerId,
        tostring(focusDetail)))
    return false
end

local function installPersonalMenuHooks()
    local widgetClasses = {
        "/Game/Pagoda/UI/Game/WBP_PauseMenu_Main.WBP_PauseMenu_Main_C",
        "/Game/Pagoda/UI/SkillTree/WBP_SkillTree_Main.WBP_SkillTree_Main_C",
        "/Game/Pagoda/UI/SkillTree/WBP_SkillTree_EquipAbilities_Panel.WBP_SkillTree_EquipAbilities_Panel_C",
        "/Game/Pagoda/UI/Cosmetics/WBP_Cosmetics_Main.WBP_Cosmetics_Main_C",
        "/Game/Pagoda/UI/Cosmetics/WBP_DanceMoveEquip_Panel.WBP_DanceMoveEquip_Panel_C"
    }
    local registered = 0
    local changed = false
    for _, classPath in ipairs(widgetClasses) do
        for _, eventName in ipairs({ "OnInitialized", "BP_OnActivated" }) do
            local hookPath = classPath .. ":" .. eventName
            local path = hookPath
            local ok, preId, postId, pathChanged = registerDeferredHook(path, function(widgetParameter, ...)
                local widget = parameterValue(widgetParameter)
                focusOwnedPlayerMenu(widget, path .. ":immediate")
                for _, delayMs in ipairs({ 1, 75, 250, 750 }) do
                    local delay = delayMs
                    ExecuteInGameThreadWithDelay(delay, function()
                        focusOwnedPlayerMenu(widget, path .. ":" .. tostring(delay) .. "ms")
                    end)
                end
            end)
            if ok then
                registered = registered + 1
            elseif pathChanged then
                report(string.format("personal_menu=hook-deferred path=%s detail=%s", path, tostring(preId)))
            end
            changed = changed or pathChanged
        end
    end

    personalMenuHooksRegistered = registered == #widgetClasses * 2
    if changed then
        report(string.format("personal_menu=hooks registered=%d total=%d", registered, #widgetClasses * 2))
    end
end

local function restoreHostHudWidget(widget, reason)
    if not isValid(widget) then
        return false
    end
    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    local owner = menuOwningController(widget)
    if isValid(owner) and (not isLocalController(owner) or controllerId(owner) ~= 0) then
        return false
    end

    local ok, detail = pcall(function()
        widget:SetRenderOpacity(1.0)
    end)
    pcall(function()
        widget:SetVisibility(0)
    end)
    for _, propertyName in ipairs({
        "HUDCanvasMain",
        "HUDElementsOverlay",
        "MainHUD",
        "WBP_GameHUD"
    }) do
        local element = readProperty(widget, propertyName)
        if isValid(element) then
            pcall(function()
                element:SetRenderOpacity(1.0)
                element:SetVisibility(0)
            end)
        end
    end
    pcall(function()
        widget:ActivateWidget()
    end)

    if not preparedHudWidgets[widgetName] then
        preparedHudWidgets[widgetName] = true
        report(string.format(
            "hud=host-restored reason=%s widget=%s ok=%s detail=%s",
            tostring(reason),
            widgetName,
            tostring(ok),
            ok and "nil" or tostring(detail)))
    end
    return ok
end

local function restoreHostHud(reason)
    local restoredSubsystems = 0
    local subsystemOk, subsystems = pcall(function()
        return FindAllOf("PagodaUISubsystem") or {}
    end)
    if subsystemOk then
        for _, subsystem in ipairs(subsystems) do
            if isValid(subsystem) then
                local localPlayer = nil
                pcall(function()
                    localPlayer = subsystem:GetOuter()
                end)
                local localPlayerId = tonumber(readProperty(localPlayer, "ControllerId")) or -1
                if localPlayerId == 0 then
                    local ok = pcall(function()
                        subsystem:SetHideHUDForCapture(false)
                        subsystem:SetUILayoutVisible(true)
                        subsystem:SetHUDElementsVisible(true)
                    end)
                    if ok then
                        restoredSubsystems = restoredSubsystems + 1
                    end
                end
            end
        end
    end

    local restoredWidgets = 0
    for _, classNameToFind in ipairs({
        "WBP_HUDCanvas_C",
        "WBP_InfiniteDiscoHUD_C"
    }) do
        local ok, widgets = pcall(function()
            return FindAllOf(classNameToFind) or {}
        end)
        if ok then
            for _, widget in ipairs(widgets) do
                if restoreHostHudWidget(widget, reason) then
                    restoredWidgets = restoredWidgets + 1
                end
            end
        end
    end
    return restoredSubsystems > 0 or restoredWidgets > 0
end

local function installHudPresentationHooks()
    local hookPaths = {
        "/Game/Pagoda/UI/Game/WBP_HUDCanvas.WBP_HUDCanvas_C:OnInitialized",
        "/Game/Pagoda/UI/Game/InfiniteDisco/WBP_InfiniteDiscoHUD.WBP_InfiniteDiscoHUD_C:OnInitialized",
        "/Game/Pagoda/UI/Game/InfiniteDisco/WBP_InfiniteDiscoHUD.WBP_InfiniteDiscoHUD_C:BP_OnActivated"
    }
    local registered = 0
    local changed = false
    for _, hookPath in ipairs(hookPaths) do
        local path = hookPath
        local ok, preId, _, pathChanged = registerDeferredHook(path, function(widgetParameter, ...)
            local widget = parameterValue(widgetParameter)
            ExecuteInGameThreadWithDelay(1, function()
                restoreHostHudWidget(widget, path)
                restoreHostHud(path)
            end)
        end)
        if ok then
            registered = registered + 1
        elseif pathChanged then
            report(string.format("hud=hook-deferred path=%s detail=%s", path, tostring(preId)))
        end
        changed = changed or pathChanged
    end
    hudPresentationHooksRegistered = registered == #hookPaths
    if changed then
        report(string.format("hud=hooks registered=%d total=%d", registered, #hookPaths))
    end
end

local function focusPlayerOneDialogue(widget, ownerController, reason)
    if not armed or not isValid(widget) or not isValid(ownerController) or
        controllerId(ownerController) ~= 0 then
        return false
    end

    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    -- Removing the top Player 2 CommonUI widget deactivates the Player 1 layer
    -- below it. Reactivate Charlie's real dialogue and explicitly return user
    -- focus to its invisible advance button so confirm is consumed by dialogue
    -- instead of falling through to Charlie's attack action.
    local inputOk = pcall(function()
        widget.bUsePlayerInput = true
        widget:UpdateInputType()
    end)
    local activateOk = pcall(function()
        widget:ActivateWidget()
        widget:RequestRefreshFocus()
    end)
    local focusTarget = readProperty(widget, "AdvanceInvisibleButton") or widget
    local focusOk, focusDetail = pcall(function()
        focusTarget:SetUserFocus(ownerController)
    end)

    if activateOk and focusOk then
        activePlayerOneDialogueWidget = widget
        activePlayerOneDialogueController = ownerController
        if not focusedHostDialogueWidgets[widgetName] then
            focusedHostDialogueWidgets[widgetName] = true
            report(string.format(
                "dialogue_filter=focused-player-one reason=%s widget=%s focus=%s inputConfigured=%s",
                tostring(reason),
                widgetName,
                objectName(focusTarget),
                tostring(inputOk)))
        end
        return true
    end

    if not dialogueFilterFailureReported then
        dialogueFilterFailureReported = true
        report(string.format(
            "dialogue_filter=focus-player-one-failed reason=%s activate=%s focus=%s detail=%s",
            tostring(reason),
            tostring(activateOk),
            tostring(focusOk),
            tostring(focusDetail)))
    end
    return false
end

local function suppressPlayerTwoDialogue(widget, ownerController, reason)
    if not armed or not isValid(widget) then
        return false
    end

    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    -- Animated intros create three dialogue roots: local Player 1, local couch
    -- Player 2, and the render client's network PlayerController. Keep only the
    -- host's local controller 0 root. Asking the widget for its owning player is
    -- unreliable because CommonUI can report Player 0 for every root.
    local chucklesController = findChucklesController()
    local ownerIsNonPrimary = isValid(ownerController) and
        (controllerId(ownerController) ~= 0 or not isLocalController(ownerController))
    if not ownerIsNonPrimary and isValid(ownerController) and isValid(chucklesController) then
        ownerIsNonPrimary = objectName(ownerController) == objectName(chucklesController)
    end
    if not ownerIsNonPrimary then
        return false
    end

    -- Deactivation releases CommonUI's input capture before the duplicate root
    -- is detached. Keep removal in its own protected call so an already
    -- inactive widget cannot prevent the actual detach.
    pcall(function()
        widget:DeactivateWidget()
    end)
    local removed, detail = pcall(function()
        widget:RemoveFromParent()
    end)
    if removed then
        if not suppressedDialogueWidgets[widgetName] then
            suppressedDialogueWidgets[widgetName] = true
            report(string.format(
                "dialogue_filter=suppressed reason=%s widget=%s owner=%s controllerId=%d local=%s",
                tostring(reason),
                widgetName,
                objectName(ownerController),
                controllerId(ownerController),
                tostring(isLocalController(ownerController))))
        end
        return true
    end

    if not dialogueFilterFailureReported then
        dialogueFilterFailureReported = true
        report("dialogue_filter=remove-failed detail=" .. tostring(detail))
    end
    return false
end

local function filterDialogueComponent(component, reason)
    if not armed or not isValid(component) then
        return false
    end

    local componentName = objectName(component)
    if string.find(componentName, "Default__", 1, true) or
        string.find(componentName, "GEN_VARIABLE", 1, true) then
        return false
    end

    local ownerController = nil
    pcall(function()
        ownerController = component:GetOwner()
    end)

    if not observedDialogueComponents[componentName] then
        observedDialogueComponents[componentName] = true
        report(string.format(
            "dialogue_filter=component-event reason=%s component=%s owner=%s controllerId=%d",
            tostring(reason),
            componentName,
            objectName(ownerController),
            isValid(ownerController) and controllerId(ownerController) or -1))
    end

    local widget = readProperty(component, "DIalogueWidget")
    if isValid(ownerController) and controllerId(ownerController) == 0 then
        return focusPlayerOneDialogue(widget, ownerController, reason)
    end

    local suppressed = suppressPlayerTwoDialogue(widget, ownerController, reason)
    if suppressed and isValid(activePlayerOneDialogueWidget) and
        isValid(activePlayerOneDialogueController) then
        focusPlayerOneDialogue(
            activePlayerOneDialogueWidget,
            activePlayerOneDialogueController,
            reason .. ":after-player-two-removal")
    end
    return suppressed
end

local function installDialogueFilter()
    local hookPaths = {
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:PushDialogueWidget",
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:GetOrPushWidget",
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:HandleDialogStarted"
    }
    local registered = 0
    local changed = false

    for _, hookPath in ipairs(hookPaths) do
        local path = hookPath
        local ok, preId, postId, pathChanged = registerDeferredHook(path, function(componentParameter, ...)
            local component = parameterValue(componentParameter)
            -- These Blueprint functions populate DIalogueWidget during the
            -- call. Check shortly afterwards instead of filtering every
            -- frame; the later check also catches CommonUI reactivation.
            ExecuteInGameThreadWithDelay(1, function()
                filterDialogueComponent(component, path .. ":1ms")
            end)
            ExecuteInGameThreadWithDelay(75, function()
                filterDialogueComponent(component, path .. ":75ms")
            end)
        end)

        if ok then
            registered = registered + 1
            if pathChanged then
                report(string.format(
                    "dialogue_filter=hooked path=%s pre=%s post=%s",
                    path,
                    tostring(preId),
                    tostring(postId)))
            end
        elseif pathChanged then
            report(string.format(
                "dialogue_filter=hook-deferred path=%s detail=%s",
                path,
                tostring(preId)))
        end
        changed = changed or pathChanged
    end

    dialogueFilterHookRegistered = registered == #hookPaths
    return changed
end

local function filterLoadedDialogueComponents(reason)
    local ok, components = pcall(function()
        return FindAllOf("BP_PlayerDialogueComponent_C") or {}
    end)
    if not ok then
        return false
    end

    local filtered = false
    for _, component in ipairs(components) do
        if filterDialogueComponent(component, reason) then
            filtered = true
        end
    end
    return filtered
end

local function vectorLabel(vector)
    if vector == nil then
        return "unavailable"
    end
    return string.format(
        "{x=%.1f y=%.1f z=%.1f}",
        tonumber(vector.X) or 0.0,
        tonumber(vector.Y) or 0.0,
        tonumber(vector.Z) or 0.0)
end

local function restoreChucklesPresentation(chuckles)
    if not isValid(chuckles) then
        return false
    end

    local ok = pcall(function()
        chuckles:SetActorHiddenInGame(false)
        chuckles:SetActorEnableCollision(true)
        local mesh = readProperty(chuckles, "Mesh")
        if isValid(mesh) then
            mesh:SetHiddenInGame(false, true)
            mesh:SetVisibility(true, true)
            mesh.bOwnerNoSee = false
            mesh.bOnlyOwnerSee = false
        end
        local movement = readProperty(chuckles, "CharacterMovement")
        if isValid(movement) then
            movement:SetMovementMode(1, 0)
        end
    end)
    return ok
end

local function restoreCouchInputWhenPrimaryReady(reason)
    local localControllers = cachedLocalControllers
    if #localControllers < 2 then
        return false
    end

    local charlieController = localControllers[1]
    local chucklesController = localControllers[2]
    local charlieMoveIgnored = false
    local charlieLookIgnored = false
    local chucklesMoveIgnored = false
    local chucklesLookIgnored = false
    local stateOk = pcall(function()
        charlieMoveIgnored = charlieController:IsMoveInputIgnored()
        charlieLookIgnored = charlieController:IsLookInputIgnored()
        chucklesMoveIgnored = chucklesController:IsMoveInputIgnored()
        chucklesLookIgnored = chucklesController:IsLookInputIgnored()
    end)
    if not stateOk then
        return false
    end

    -- Do not break a real shared cinematic. Recover only a stale Player 2 flag
    -- after vanilla has already returned the corresponding input to Charlie.
    local resetMove = not charlieMoveIgnored and chucklesMoveIgnored
    local resetLook = not charlieLookIgnored and chucklesLookIgnored
    if not resetMove and not resetLook then
        return false
    end

    local resetOk, detail = pcall(function()
        if resetMove then
            chucklesController:ResetIgnoreMoveInput()
        end
        if resetLook then
            chucklesController:ResetIgnoreLookInput()
        end
        local chuckles = readProperty(chucklesController, "Pawn")
        restoreChucklesPresentation(chuckles)
    end)
    report(string.format(
        "couch_input=recovered reason=%s move=%s look=%s ok=%s detail=%s",
        tostring(reason),
        tostring(resetMove),
        tostring(resetLook),
        tostring(resetOk),
        resetOk and "nil" or tostring(detail)))
    return resetOk
end

local function prepareHazardReplication(actor, reason)
    if not armed or not isValid(actor) then
        return false
    end

    local actorName = objectName(actor)
    if string.find(actorName, "Default__", 1, true) then
        return false
    end

    local first = not replicatedHazardActors[actorName]
    local ok, detail = pcall(function()
        actor.bAlwaysRelevant = true
        actor.bOnlyRelevantToOwner = false
        actor.bNetLoadOnClient = true
        actor.NetCullDistanceSquared = 1000000000000.0
        actor:SetReplicates(true)
        actor:SetReplicateMovement(true)
    end)
    local niagaraOk, niagaraDetail = pcall(function()
        local niagara = readProperty(actor, "Niagara")
        if isValid(niagara) then
            niagara:SetIsReplicated(true)
        end
    end)
    local forceOk, forceDetail = pcall(function()
        actor:ForceNetUpdate()
    end)
    if first then
        replicatedHazardActors[actorName] = true
        report(string.format(
            "hazard_replication=prepared reason=%s actor=%s class=%s actorOk=%s niagaraOk=%s forceOk=%s detail=%s",
            tostring(reason),
            actorName,
            className(actor),
            tostring(ok),
            tostring(niagaraOk),
            tostring(forceOk),
            ok and niagaraOk and forceOk and "nil" or table.concat({
                ok and "actor=nil" or "actor=" .. tostring(detail),
                niagaraOk and "niagara=nil" or "niagara=" .. tostring(niagaraDetail),
                forceOk and "force=nil" or "force=" .. tostring(forceDetail)
            }, ";")))
    end
    return ok
end

local function installHazardReplicationHooks()
    local hookPaths = {
        "/Game/Pagoda/Common/BP_GroundImpactIndicator.BP_GroundImpactIndicator_C:ReceiveBeginPlay",
        "/Game/Pagoda/Common/BP_GroundImpactIndicator.BP_GroundImpactIndicator_C:SetupIndicator",
        "/Game/Pagoda/Common/BP_GroundImpactIndicator.BP_GroundImpactIndicator_C:UpdateIndicator",
        "/Game/Pagoda/Common/BP_TraceImpactIndicator.BP_TraceImpactIndicator_C:PooledBeginPlay",
        "/Game/Pagoda/Common/BP_TraceImpactIndicator.BP_TraceImpactIndicator_C:SetDestination",
        "/Game/Pagoda/Common/BP_TraceImpactIndicator.BP_TraceImpactIndicator_C:SetIsReadyToShoot"
    }
    local registered = 0
    local changed = false
    for _, hookPath in ipairs(hookPaths) do
        local path = hookPath
        local ok, preId, postId, pathChanged = registerDeferredHook(path, function(actorParameter, ...)
            local actor = parameterValue(actorParameter)
            prepareHazardReplication(actor, path)
            ExecuteInGameThreadWithDelay(1, function()
                if prepareHazardReplication(actor, path .. ":post") then
                    pcall(function()
                        actor:ForceNetUpdate()
                    end)
                end
            end)
        end)
        if ok then
            registered = registered + 1
        elseif pathChanged then
            report(string.format(
                "hazard_replication=hook-deferred path=%s detail=%s",
                path,
                tostring(preId)))
        end
        changed = changed or pathChanged
    end
    hazardReplicationHooksRegistered = registered == #hookPaths
    if changed then
        report(string.format(
            "hazard_replication=hooks registered=%d total=%d",
            registered,
            #hookPaths))
    end
end

local function teleportChucklesBesideCharlie(reason, visibleStatus, safeVerticalArrival)
    local charlie, chuckles = findCouchCharacters()
    if not isValid(charlie) or not isValid(chuckles) then
        report(string.format(
            "chuckles_rescue=blocked reason=%s charlie=%s chuckles=%s",
            tostring(reason),
            objectName(charlie),
            objectName(chuckles)))
        return false
    end

    local beforeOk, beforeLocation = pcall(function()
        return chuckles:K2_GetActorLocation()
    end)
    local transformOk, destination, rotation = pcall(function()
        local location = charlie:K2_GetActorLocation()
        if safeVerticalArrival then
            -- Train corridors are narrow enough that a blind sideways offset
            -- can put Chuckles through the carriage. I arrive above Charlie's
            -- known-good floor point and let vanilla falling choose the floor.
            location.Z = (tonumber(location.Z) or 0.0) + 120.0
        else
            location.Y = (tonumber(location.Y) or 0.0) + 150.0
            location.Z = (tonumber(location.Z) or 0.0) + 35.0
        end
        return location, charlie:K2_GetActorRotation()
    end)
    if not transformOk then
        report("chuckles_rescue=failed reason=charlie-transform-unavailable")
        return false
    end

    local teleportOk, teleportResult = pcall(function()
        local movement = readProperty(chuckles, "CharacterMovement")
        if isValid(movement) then
            movement:StopMovementImmediately()
        end
        local hitResult = {}
        local moved = chuckles:K2_SetActorLocationAndRotation(
            destination,
            rotation,
            false,
            hitResult,
            true)
        restoreChucklesPresentation(chuckles)
        if safeVerticalArrival then
            local movement = readProperty(chuckles, "CharacterMovement")
            if isValid(movement) then
                movement:SetMovementMode(3, 0)
            end
        end
        chuckles:ForceNetUpdate()
        return moved
    end)
    if teleportOk and teleportResult ~= false then
        automaticRescueCooldownChecks = 20
        report(string.format(
            "chuckles_rescue=succeeded reason=%s before=%s destination=%s",
            tostring(reason),
            beforeOk and vectorLabel(beforeLocation) or "unavailable",
            vectorLabel(destination)))
        if visibleStatus then
            showStatus("Chuckles teleported beside Charlie.", 4.0, {
                R = 0.25, G = 1.00, B = 0.45, A = 1.00
            })
        end
        return true
    end

    report(string.format(
        "chuckles_rescue=failed reason=%s callOk=%s detail=%s",
        tostring(reason),
        tostring(teleportOk),
        tostring(teleportResult)))
    return false
end

local function resetCharlieTransitionTracker()
    lastCharlieTransitionX = nil
    lastCharlieTransitionY = nil
    lastCharlieTransitionZ = nil
end

local function rescueChucklesAfterCharlieTransition()
    local charlie, chuckles = findCouchCharacters()
    if not isValid(charlie) or not isValid(chuckles) then
        return false
    end

    local ok, location = pcall(function()
        return charlie:K2_GetActorLocation()
    end)
    if not ok or location == nil then
        return false
    end

    local x = tonumber(location.X) or 0.0
    local y = tonumber(location.Y) or 0.0
    local z = tonumber(location.Z) or 0.0
    if lastCharlieTransitionX == nil then
        lastCharlieTransitionX = x
        lastCharlieTransitionY = y
        lastCharlieTransitionZ = z
        return false
    end

    local dx = x - lastCharlieTransitionX
    local dy = y - lastCharlieTransitionY
    local dz = z - lastCharlieTransitionZ
    lastCharlieTransitionX = x
    lastCharlieTransitionY = y
    lastCharlieTransitionZ = z

    local horizontalDistanceSquared = dx * dx + dy * dy
    if horizontalDistanceSquared >= CHARLIE_TRANSITION_DISTANCE_SQUARED or
       math.abs(dz) >= CHARLIE_TRANSITION_VERTICAL_DISTANCE then
        report(string.format(
            "sublevel_transition=detected charlieDelta={x=%.1f y=%.1f z=%.1f}",
            dx,
            dy,
            dz))
        return teleportChucklesBesideCharlie("automatic-sublevel-transition", false, true)
    end
    return false
end

local function rescueChucklesIfFalling()
    if automaticRescueCooldownChecks > 0 then
        automaticRescueCooldownChecks = automaticRescueCooldownChecks - 1
        return false
    end

    local charlie, chuckles = findCouchCharacters()
    if not isValid(charlie) or not isValid(chuckles) then
        return false
    end

    local ok, charlieLocation, chucklesLocation = pcall(function()
        return charlie:K2_GetActorLocation(), chuckles:K2_GetActorLocation()
    end)
    if not ok then
        return false
    end

    local charlieZ = tonumber(charlieLocation.Z) or 0.0
    local chucklesZ = tonumber(chucklesLocation.Z) or 0.0
    if charlieZ - chucklesZ >= CHUCKLES_FALL_DISTANCE_Z then
        return teleportChucklesBesideCharlie("automatic-fall", false, true)
    end
    return false
end

local function setHostMenuView(enabled, reason)
    local world = UEHelpers.GetWorld()
    local gameplayStatics = UEHelpers.GetGameplayStatics()
    if not isValid(world) or not isValid(gameplayStatics) then
        return false
    end

    local ok, detail = pcall(function()
        gameplayStatics:SetForceDisableSplitscreen(world, enabled)
    end)
    if not ok then
        report("menu_view=failed detail=" .. tostring(detail))
        return false
    end

    hostMenuViewEnabled = enabled
    report(string.format(
        "menu_view enabled=%s reason=%s",
        tostring(enabled),
        tostring(reason)))
    if enabled then
        showStatus("Online host view: Charlie is full-screen. Ctrl+Shift+F6 shows the split debug view.", 8.0)
    else
        showStatus("Split debug view enabled. Ctrl+Shift+F6 returns to the online host view.", 7.0)
    end
    return true
end

local function configureAdmission()
    local gameMode = UEHelpers.GetGameModeBase()
    local gameSession = readProperty(gameMode, "GameSession")
    if not isValid(gameSession) then
        return false
    end

    local ok, detail = pcall(function()
        -- Charlie + couch Chuckles are local. The third connection is a render-only client.
        gameSession.MaxPlayers = 3
        gameSession.MaxPartySize = 3
    end)
    if not ok then
        report("admission=failed detail=" .. tostring(detail))
    end
    return ok
end

local function configureCouchReplication(pawn)
    if not isValid(pawn) then
        return false
    end

    local pawnName = objectName(pawn)
    if configuredCouchPawnNames[pawnName] then
        return true
    end
    local first = not configuredCouchPawnNames[pawnName]
    local ok, detail = pcall(function()
        pawn.bAlwaysRelevant = true
        pawn.bOnlyRelevantToOwner = false
        pawn.bNetUseOwnerRelevancy = false
        pawn.bReplicates = true
        pawn.bReplicateMovement = true
        pawn.NetDormancy = 0
        -- 60/30 is enough for responsive movement without making a modded
        -- listen server spend most of its frame producing actor updates.
        pawn.NetUpdateFrequency = 60.0
        pawn.MinNetUpdateFrequency = 30.0
        pawn.NetPriority = 3.0
        pawn:SetReplicates(true)
        pawn:SetReplicateMovement(true)
        pawn:FlushNetDormancy()
        pawn:ForceNetUpdate()
    end)

    if first then
        configuredCouchPawnNames[pawnName] = true
        report(string.format(
            "couch_replication=configured pawn=%s class=%s ok=%s detail=%s",
            pawnName,
            className(pawn),
            tostring(ok),
            ok and "nil" or tostring(detail)))
    end
    return ok
end

local function configureAllCouchReplication()
    local configured = 0
    for _, controller in ipairs(getLocalControllers()) do
        if configureCouchReplication(readProperty(controller, "Pawn")) then
            configured = configured + 1
        end
    end
    return configured
end

local function configuredCouchPawnLabel()
    local names = {}
    for name, _ in pairs(configuredCouchPawnNames) do
        table.insert(names, name)
    end
    table.sort(names)
    return table.concat(names, " | ")
end

local function ensureCouchPlayerTwo(reason)
    if worldTransitioning or not supportedWorld() then
        return nil
    end

    local controllers = getLocalControllers()
    if #controllers >= 2 then
        configureViewport()
        pinControllerSeats(reason)
        -- Seat pinning usually preserves the cached order. Re-scan reflected
        -- controllers only if their old order no longer matches the new IDs.
        if controllerId(controllers[1]) ~= 0 or controllerId(controllers[2]) ~= 1 then
            refreshControllerCache()
            controllers = getLocalControllers()
        end
        configureAllCouchReplication()
        return controllers[2]
    end
    if #controllers == 0 then
        return nil
    end

    local gameplayStatics = UEHelpers.GetGameplayStatics()
    if not isValid(gameplayStatics) then
        return nil
    end

    configureViewport()
    local ok, controller = pcall(function()
        return gameplayStatics:CreatePlayer(controllers[1], 1, true)
    end)
    configureViewport()
    report(string.format(
        "couch_player=create reason=%s ok=%s controller=%s pawn=%s",
        tostring(reason),
        tostring(ok),
        objectName(controller),
        objectName(readProperty(controller, "Pawn"))))

    if ok and isValid(controller) then
        refreshControllerCache()
        pinControllerSeats(reason)
        refreshControllerCache()
        configureAllCouchReplication()
        return controller
    end
    return nil
end

local function neutralizeRenderProxy(controller)
    local pawn = readProperty(controller, "Pawn")
    if not isValid(pawn) then
        return false
    end

    local proxyName = objectName(pawn)
    if neutralizedProxyName == proxyName then
        return true
    end
    local first = neutralizedProxyName ~= proxyName
    local ok, detail = pcall(function()
        -- The friend's spawned pawn supplies a local camera rig only. It must not
        -- become a third fighter or interfere with the host-owned couch session.
        pawn:SetActorHiddenInGame(true)
        pawn:SetActorEnableCollision(false)
        pawn.bCanBeDamaged = false
        pawn.bAlwaysRelevant = false
        pawn.bOnlyRelevantToOwner = true
        pawn.bReplicateMovement = false
        pawn:SetReplicateMovement(false)

        local movement = readProperty(pawn, "CharacterMovement")
        if isValid(movement) then
            movement:StopMovementImmediately()
            movement:DisableMovement()
            movement.GravityScale = 0.0
        end

    end)

    if first then
        neutralizedProxyName = proxyName
        report(string.format(
            "render_proxy=neutralized controller=%s pawn=%s class=%s ok=%s detail=%s",
            objectName(controller),
            proxyName,
            className(pawn),
            tostring(ok),
            ok and "nil" or tostring(detail)))
    end
    return ok
end

local function sampleFriendCameraOrientation()
    -- The maintenance pass validates and refreshes these references. Avoid
    -- revalidating the entire reflected controller list on every look update.
    local localControllers = cachedLocalControllers
    local remoteControllers = cachedRemoteControllers
    if #localControllers < 2 or #remoteControllers == 0 then
        return false
    end

    local couchController = localControllers[2]
    local renderController = remoteControllers[1]

    -- Read the friend's camera only to orient their movement. The authenticated
    -- native relay already supplies Chuckles' right-stick input; writing this
    -- rotation into the couch controller as well applied camera input twice and
    -- produced the apparent stick drift reported by both players.
    local hostLookIgnored = false
    local couchLookIgnored = false
    local renderLookIgnored = false
    pcall(function()
        hostLookIgnored = localControllers[1]:IsLookInputIgnored()
    end)
    pcall(function()
        couchLookIgnored = couchController:IsLookInputIgnored()
    end)
    pcall(function()
        renderLookIgnored = renderController:IsLookInputIgnored()
    end)
    if hostLookIgnored or couchLookIgnored or renderLookIgnored then
        -- Dodges, attacks, menus, and cinematics briefly ignore look input.
        -- Preserve the last sampled camera basis during that window; clearing it
        -- made Chuckles instantly fall back to Charlie's camera orientation.
        if not orientationSourcePausedForCameraOwnership then
            orientationSourcePausedForCameraOwnership = true
            report(string.format(
                "orientation_source=paused-camera-ownership hostIgnored=%s couchIgnored=%s renderIgnored=%s",
                tostring(hostLookIgnored),
                tostring(couchLookIgnored),
                tostring(renderLookIgnored)))
        end
        return false
    end

    if orientationSourcePausedForCameraOwnership then
        orientationSourcePausedForCameraOwnership = false
        report("orientation_source=resumed-camera-ownership")
    end
    local ok, detail = pcall(function()
        local clientRotation = renderController:GetControlRotation()
        cachedMovementClientYaw = rotationYaw(clientRotation)
        cachedMovementHostYaw = controllerCameraYaw(localControllers[1], true)
    end)
    if ok and orientationSourceControllerName == nil then
        orientationSourceControllerName = objectName(renderController)
        report(string.format(
            "orientation_source=active source=%s movementTarget=%s mode=read-only",
            orientationSourceControllerName,
            objectName(couchController)))
    elseif not ok and not orientationSourceFailureReported then
        orientationSourceFailureReported = true
        report("orientation_source=failed detail=" .. tostring(detail))
    end
    return ok
end

local function snapshot(reason)
    local driver = findNetDriver()
    local serverConnection = readProperty(driver, "ServerConnection")
    local clients = readProperty(driver, "ClientConnections")
    local clientCount = 0
    pcall(function()
        clientCount = #clients
    end)

    report(string.format(
        "snapshot=%s version=%s world=%s driver=%s listenServer=%s clientConnections=%d localControllers=%d remoteControllers=%d couchPawn=%s renderProxy=%s",
        tostring(reason),
        MOD_VERSION,
        objectName(UEHelpers.GetWorld()),
        objectName(driver),
        tostring(isValid(driver) and not isValid(serverConnection)),
        clientCount,
        #getLocalControllers(),
        #getRemoteControllers(),
        configuredCouchPawnLabel(),
        tostring(neutralizedProxyName)))
end

local function executeCommand(command, label)
    local world = UEHelpers.GetWorld()
    local controller = getLocalControllers()[1]
    local systemLibrary = UEHelpers.GetKismetSystemLibrary()
    if not isValid(world) or not isValid(controller) or not isValid(systemLibrary) then
        report("command=" .. tostring(label) .. " failed=prerequisite")
        return false
    end

    local ok, detail = pcall(function()
        systemLibrary:ExecuteConsoleCommand(world, command, controller)
    end)
    report(string.format(
        "command=%s value=%s ok=%s detail=%s",
        tostring(label),
        tostring(command),
        tostring(ok),
        ok and "nil" or tostring(detail)))
    return ok
end

local function refreshDeferredMultiplayerHooks(reason)
    -- Most Pagoda Blueprint functions do not exist when UE4SS first starts.
    -- I retry each path independently as maps stream in, then repair any UI
    -- instance whose creation event happened before its hook became available.
    installVersionWatermark()
    installPersonalMenuHooks()
    installHudPresentationHooks()
    installDialogueFilter()
    installHazardReplicationHooks()
    applyVersionWatermarkToLoadedLayouts(reason)
    filterLoadedDialogueComponents(reason)
end

local function startMonitor(reason)
    monitorGeneration = monitorGeneration + 1
    local generation = monitorGeneration
    report(string.format("monitor=started reason=%s generation=%d", tostring(reason), generation))

    local ticks = 0
    local tick
    tick = function()
        if generation ~= monitorGeneration then
            return
        end

        if armed and not worldTransitioning and supportedWorld() then
            ticks = ticks + 1
            if ticks == 1 or ticks % 3 == 0 then
                -- Ten checks a second still catches an abrupt sublevel jump
                -- before Chuckles falls away, without asking two reflected
                -- actors for transforms on every monitor tick.
                rescueChucklesAfterCharlieTransition()
            end
            -- Reflection-wide controller discovery is intentionally kept out
            -- of the fast path. Refresh once per second, then use the cached
            -- controller references for the friend's look bridge. Pawn and
            -- proxy configuration are idempotent per map, so this pass no
            -- longer flushes dormancy or disables movement every interval.
            if ticks == 1 or ticks % 30 == 0 then
                refreshControllerCache()
                configureViewport()
                configureAdmission()
                ensureCouchPlayerTwo("monitor")
                for _, controller in ipairs(getRemoteControllers()) do
                    neutralizeRenderProxy(controller)
                end
                rescueChucklesIfFalling()
                restoreCouchInputWhenPrimaryReady("monitor")
            end
            if ticks == 150 then
                refreshDeferredMultiplayerHooks("monitor:" .. tostring(ticks))
            end
            sampleFriendCameraOrientation()
        end
        ExecuteInGameThreadWithDelay(33, tick)
    end
    ExecuteInGameThreadWithDelay(33, tick)
end

installCouchGameplayHooks()

local function startHosting()
    ExecuteInGameThread(function()
        armed = true
        if not supportedWorld() then
            showStatus("Enter the Dive Bar, then press Ctrl+Shift+F5 again.", 8.0, {
                R = 1.00, G = 0.75, B = 0.20, A = 1.00
            })
            return
        end

        local driver = findNetDriver()
        local serverConnection = readProperty(driver, "ServerConnection")
        if isValid(driver) and not isValid(serverConnection) then
            ensureCouchPlayerTwo("host-already-active")
            setHostMenuView(true, "host-already-active")
            startMonitor("host-already-active")
            showStatus("LimelightMP local-render host is already ready.", 7.0)
            return
        end

        configuredCouchPawnNames = {}
        neutralizedProxyName = nil
        activePlayerOneDialogueWidget = nil
        activePlayerOneDialogueController = nil
        orientationSourceControllerName = nil
        orientationSourceFailureReported = false
        movementOrientationFailureReported = false
        movementOrientationActiveReported = false
        couchSkillOverrideFailureReported = false
        couchSkillOverrideActiveReported = false
        cachedMovementHostYaw = nil
        cachedMovementClientYaw = nil
        orientationSourcePausedForCameraOwnership = false
        controllerSeatsReported = false
        automaticRescueCooldownChecks = 0
        resetCharlieTransitionTracker()
        clearControllerCache()
        local command = string.format("open %s?listen?Port=%d", HOST_MAP, LISTEN_PORT)
        if executeCommand(command, "host-local-render") then
            showStatus("Starting LimelightMP local-render host...", 8.0, {
                R = 0.25, G = 1.00, B = 0.45, A = 1.00
            })
        end
    end)
end

local function serverTravel(map, label)
    ExecuteInGameThread(function()
        local driver = findNetDriver()
        if not isValid(driver) or isValid(readProperty(driver, "ServerConnection")) then
            showStatus("Start hosting before changing level for both players.", 7.0)
            return
        end
        executeCommand("servertravel " .. map, "travel-" .. label)
        showStatus("Moving both games to " .. label .. "...", 7.0, {
            R = 1.00, G = 0.80, B = 0.20, A = 1.00
        })
    end)
end

RegisterLoadMapPreHook(function()
    worldTransitioning = true
    supportedWorldCached = nil
    hostMenuViewEnabled = false
    configuredCouchPawnNames = {}
    neutralizedProxyName = nil
    activePlayerOneDialogueWidget = nil
    activePlayerOneDialogueController = nil
    orientationSourceControllerName = nil
    orientationSourceFailureReported = false
    movementOrientationFailureReported = false
    movementOrientationActiveReported = false
    couchSkillOverrideFailureReported = false
    couchSkillOverrideActiveReported = false
    cachedMovementHostYaw = nil
    cachedMovementClientYaw = nil
    orientationSourcePausedForCameraOwnership = false
    controllerSeatsReported = false
    automaticRescueCooldownChecks = 0
    replicatedHazardActors = {}
    preparedHudWidgets = {}
    resetCharlieTransitionTracker()
    clearControllerCache()
    report("map_transition=started")
end)

RegisterLoadMapPostHook(function()
    worldTransitioning = false
    report("map_transition=finished world=" .. objectName(UEHelpers.GetWorld()))
    if supportedWorld() then
        for _, delayMs in ipairs({ 100, 1200 }) do
            local delay = delayMs
            ExecuteInGameThreadWithDelay(delay, function()
                refreshDeferredMultiplayerHooks("map-load:" .. tostring(delay) .. "ms")
            end)
        end
    end
    if armed then
        ExecuteInGameThreadWithDelay(1200, function()
            ensureCouchPlayerTwo("map-load")
            -- I establish a known-good spawn after Unreal moves the existing
            -- listen session, just in case Chuckles has explored the void again.
            teleportChucklesBesideCharlie("post-map-entry", false, true)
            -- Chuckles remains a complete vanilla couch player, but an online
            -- friend has their own rendered view. Keeping only Player 0's
            -- viewport visible prevents the game's per-player HUD/dialogue
            -- roots from drawing twice on the host.
            setHostMenuView(true, "online-host-map-load")
            restoreHostHud("online-host-map-load")
            ExecuteInGameThreadWithDelay(1800, function()
                restoreHostHud("online-host-map-load-delayed")
            end)
            startMonitor("map-load")
            snapshot("post-map-load")
        end)
    end
end)

RegisterKeyBind(Key.F5, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, startHosting)

RegisterKeyBind(Key.F7, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    serverTravel(INFINITE_DISCO_MAP, "Infinite Disco")
end)

RegisterKeyBind(Key.F6, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    ExecuteInGameThread(function()
        setHostMenuView(not hostMenuViewEnabled, "hotkey")
    end)
end)

RegisterKeyBind(Key.F8, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    serverTravel(HOST_MAP, "the Dive Bar")
end)

RegisterKeyBind(Key.F10, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    ExecuteInGameThread(function()
        teleportChucklesBesideCharlie("manual-hotkey", true)
    end)
end)

RegisterKeyBind(Key.F12, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    ExecuteInGameThread(function()
        snapshot("manual")
        showStatus("Local-render network snapshot written to the log.", 5.0)
    end)
end)

log(string.format(
    "Version %s loaded. The host keeps vanilla couch ownership; the friend's game renders its replicated world locally. In the Dive Bar press Ctrl+Shift+F5 once. Port=%d address=%s.",
    MOD_VERSION,
    LISTEN_PORT,
    CONNECT_ADDRESS))
