local UEHelpers = require("UEHelpers")

local MOD_NAME = "LimelightMPLocalRenderClient"
local MOD_VERSION = "0.1.4"
local VERSION_WATERMARK = "LimelightMP_0.1.4 By Henreh <3"
local CONNECT_ADDRESS = "127.0.0.1:7777" -- INSTALL_CONNECT_ADDRESS
-- I keep the curtain on while Unreal changes outfits, but one switch puts the
-- old honest mess back if a future game build teaches it a new dance.
local ENABLE_CLIENT_TRAVEL_CURTAIN = true
-- I let the handmade screen stand in front of the reliable black curtain. If
-- the asset has a dramatic episode, one switch sends us back to plain black.
local ENABLE_CLIENT_LOADING_WIDGET = true
local CLIENT_LOADING_WIDGET_CLASS =
    "/Game/LimeLightMP/UI/WBP_LimelightMPLoading.WBP_LimelightMPLoading_C"
local WIDGET_BLUEPRINT_LIBRARY =
    "/Script/UMG.Default__WidgetBlueprintLibrary"

local joinIssued = false
local worldTransitioning = false
local cameraGeneration = 0
local cameraProxyPawnName = nil
local cameraTargetName = nil
local cameraUpdates = 0
local cameraLastReferenceRefreshTick = -1000
local statusFailureReported = false
local cachedController = nil
local cachedProxy = nil
local cachedTarget = nil
local cachedFollowCamera = nil
local cachedProxyMesh = nil
local cachedTargetMesh = nil
local cachedTargetIdentity = nil
local cachedProxyAnchorOffsetX = 0.0
local cachedProxyAnchorOffsetY = 0.0
local cachedProxyAnchorOffsetZ = 0.0
local cachedUseSmoothedTargetMesh = false
local cameraViewBound = false
local smoothedTargetName = nil
local smoothedTargetX = nil
local smoothedTargetY = nil
local smoothedTargetZ = nil
local musicRecoveryWorldName = nil
local musicRecoveryAttempts = 0
local musicRecoverySucceeded = false
local musicRecoveryLastAttemptTick = -1000
local musicRecoveryPendingUntilTick = -1000
local dialogueInputHookRegistered = false
local preparedDialogueWidgets = {}
local rhythmSyncHookRegistered = false
local rhythmSyncArmedReported = false
local cameraSweepHitResult = {}
local versionWatermarkHookRegistered = false
local versionWatermarkObjectName = nil
local hazardPresentationHooksRegistered = false
local preparedHazardActors = {}
local transitionRecoveryGeneration = 0
local personalMenuHooksRegistered = false
local preparedPersonalMenus = {}
local hudPresentationHooksRegistered = false
local preparedHudWidgets = {}
local deferredHookPaths = {}
local deferredHookFailures = {}

-- Replicated Character roots arrive in discrete network updates. Unreal applies
-- visual smoothing to the skeletal mesh, so use that as the camera anchor and
-- lightly filter any remaining corrections without delaying look rotation.
local CAMERA_ANCHOR_BLEND = 0.35
local CAMERA_ANCHOR_SNAP_DISTANCE_SQUARED = 640000.0
local CAMERA_ANCHOR_SNAP_VERTICAL_DISTANCE = 350.0
-- Pagoda's replicated song clock arrives in steps. A small tolerance makes the
-- native correction seek on nearly every frame between network updates. Keep
-- that failsafe wide and align the local song once when it becomes playable.
local MAX_CLIENT_RHYTHM_DEVIATION = 2.50
local MUSIC_RECOVERY_MAX_ATTEMPTS = 6
local MUSIC_RECOVERY_RETRY_TICKS = 300

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

local function resetCameraSmoothing()
    smoothedTargetName = nil
    smoothedTargetX = nil
    smoothedTargetY = nil
    smoothedTargetZ = nil
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

local function readMember(value, memberName)
    if value == nil then
        return nil
    end

    local ok, member = pcall(function()
        return value[memberName]
    end)
    return ok and member or nil
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

local function restoreHazardPresentation(actor, reason)
    if not isValid(actor) then
        return false
    end

    local actorName = objectName(actor)
    if string.find(actorName, "Default__", 1, true) then
        return false
    end

    local first = not preparedHazardActors[actorName]
    local ok, detail = pcall(function()
        actor:SetActorHiddenInGame(false)
        local niagara = readProperty(actor, "Niagara")
        if isValid(niagara) then
            niagara:SetHiddenInGame(false, true)
            niagara:SetVisibility(true, true)
        end
    end)
    if first then
        preparedHazardActors[actorName] = true
        report(string.format(
            "hazard_presentation=restored reason=%s actor=%s class=%s ok=%s detail=%s",
            tostring(reason),
            actorName,
            className(actor),
            tostring(ok),
            ok and "nil" or tostring(detail)))
    end
    return ok
end

local function installHazardPresentationHooks()
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
        local ok, preId, _, pathChanged = registerDeferredHook(path, function(actorParameter, ...)
            local actor = parameterValue(actorParameter)
            ExecuteInGameThreadWithDelay(1, function()
                restoreHazardPresentation(actor, path)
            end)
        end)
        if ok then
            registered = registered + 1
        elseif pathChanged then
            report(string.format(
                "hazard_presentation=hook-deferred path=%s detail=%s",
                path,
                tostring(preId)))
        end
        changed = changed or pathChanged
    end
    hazardPresentationHooksRegistered = registered == #hookPaths
    if changed then
        report(string.format(
            "hazard_presentation=hooks registered=%d total=%d",
            registered,
            #hookPaths))
    end
end

local function prepareDialogueWidget(widget, reason)
    if not isValid(widget) then
        return false
    end

    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    -- A network-joined render client does not always receive CommonUI's normal
    -- local-player focus handoff. Restore the same active/input state used by a
    -- vanilla local dialogue widget so confirm can clear it. Each operation is
    -- isolated because a cosmetic input-type refresh must not block focus.
    local inputOk, inputDetail = pcall(function()
        widget.bUsePlayerInput = true
        widget:UpdateInputType()
    end)
    local activateOk, activateDetail = pcall(function()
        widget:ActivateWidget()
        widget:RequestRefreshFocus()
    end)
    local focusTarget = readProperty(widget, "AdvanceInvisibleButton") or widget
    local focusOk, focusDetail = pcall(function()
        focusTarget:SetUserFocus(UEHelpers.GetPlayerController())
    end)
    if activateOk and focusOk then
        if not preparedDialogueWidgets[widgetName] then
            preparedDialogueWidgets[widgetName] = true
            report(string.format(
                "dialogue_input=prepared reason=%s widget=%s focus=%s inputConfigured=%s",
                tostring(reason),
                widgetName,
                objectName(focusTarget),
                tostring(inputOk)))
        end
        return true
    end

    report(string.format(
        "dialogue_input=prepare-failed reason=%s widget=%s input=%s activate=%s focus=%s",
        tostring(reason),
        widgetName,
        tostring(inputDetail),
        tostring(activateDetail),
        tostring(focusDetail)))
    return false
end

local function prepareDialogueComponent(component, reason)
    if not isValid(component) then
        return false
    end
    return prepareDialogueWidget(readProperty(component, "DIalogueWidget"), reason)
end

local function installDialogueInputHooks()
    local componentPaths = {
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:PushDialogueWidget",
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:GetOrPushWidget",
        "/Game/Pagoda/Characters/Player/Components/BP_PlayerDialogueComponent.BP_PlayerDialogueComponent_C:HandleDialogStarted"
    }
    local registered = 0
    local changed = false

    for _, hookPath in ipairs(componentPaths) do
        local path = hookPath
        local ok, preId, postId, pathChanged = registerDeferredHook(path, function(componentParameter, ...)
            local component = parameterValue(componentParameter)
            ExecuteInGameThreadWithDelay(1, function()
                prepareDialogueComponent(component, path .. ":1ms")
            end)
            ExecuteInGameThreadWithDelay(75, function()
                prepareDialogueComponent(component, path .. ":75ms")
            end)
        end)
        if ok then
            registered = registered + 1
            if pathChanged then
                report(string.format(
                    "dialogue_input=hooked path=%s pre=%s post=%s",
                    path,
                    tostring(preId),
                    tostring(postId)))
            end
        elseif pathChanged then
            report(string.format(
                "dialogue_input=hook-deferred path=%s detail=%s",
                path,
                tostring(preId)))
        end
        changed = changed or pathChanged
    end

    local widgetPath = "/Game/Pagoda/UI/Dialogue/WBP_Dialogue_Main.WBP_Dialogue_Main_C:HandleDialogueStart"
    local widgetOk, widgetPreId, widgetPostId, widgetChanged = registerDeferredHook(widgetPath, function(widgetParameter, ...)
        local widget = parameterValue(widgetParameter)
        ExecuteInGameThreadWithDelay(1, function()
            prepareDialogueWidget(widget, widgetPath)
        end)
    end)
    if widgetOk then
        registered = registered + 1
        if widgetChanged then
            report(string.format(
                "dialogue_input=hooked path=%s pre=%s post=%s",
                widgetPath,
                tostring(widgetPreId),
                tostring(widgetPostId)))
        end
    elseif widgetChanged then
        report(string.format(
            "dialogue_input=hook-deferred path=%s detail=%s",
            widgetPath,
            tostring(widgetPreId)))
    end
    changed = changed or widgetChanged

    dialogueInputHookRegistered = registered == #componentPaths + 1
    return changed
end

local function prepareLoadedDialogue(reason)
    local prepared = false
    local componentOk, components = pcall(function()
        return FindAllOf("BP_PlayerDialogueComponent_C") or {}
    end)
    if componentOk then
        for _, component in ipairs(components) do
            if prepareDialogueComponent(component, reason) then
                prepared = true
            end
        end
    end

    local widgetOk, widgets = pcall(function()
        return FindAllOf("WBP_Dialogue_Main_C") or {}
    end)
    if widgetOk then
        for _, widget in ipairs(widgets) do
            if prepareDialogueWidget(widget, reason) then
                prepared = true
            end
        end
    end
    return prepared
end

local function owningPlayerController(widget)
    if not isValid(widget) then
        return nil
    end
    local owner = nil
    pcall(function()
        owner = widget:GetOwningPlayer()
    end)
    if isValid(owner) then
        return owner
    end
    return nil
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

    local fallbackNames = {
        "ResumeButton",
        "SkillTreeButton",
        "CosmeticsButton",
        "ExitAction",
        "W_ExitAction"
    }
    for _, propertyName in ipairs(fallbackNames) do
        local candidate = readProperty(widget, propertyName)
        if isValid(candidate) then
            return candidate
        end
    end
    return widget
end

local function preparePersonalMenu(widget, reason)
    if not isValid(widget) then
        return false
    end
    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
        return false
    end

    local owner = owningPlayerController(widget)
    if not isValid(owner) then
        return false
    end

    -- UMG is local, but enforce that invariant explicitly: a widget owned by
    -- any replicated/remote controller must never appear on the friend's PC.
    local localController = UEHelpers.GetPlayerController()
    local ownedByThisClient = isValid(localController) and
        objectName(owner) == objectName(localController)
    local localPlayer, playthroughData = menuSaveContext(widget, owner)
    local visibleOnClient = ownedByThisClient and isValid(playthroughData)
    local renderOk, renderDetail = pcall(function()
        widget:SetRenderOpacity(visibleOnClient and 1.0 or 0.0)
    end)
    if not ownedByThisClient then
        if not preparedPersonalMenus[widgetName] then
            preparedPersonalMenus[widgetName] = true
            report(string.format(
                "personal_menu=remote-suppressed reason=%s widget=%s owner=%s local=%s render=%s detail=%s",
                tostring(reason),
                widgetName,
                objectName(owner),
                objectName(localController),
                tostring(renderOk),
                renderOk and "nil" or tostring(renderDetail)))
        end
        return renderOk
    end
    if not isValid(playthroughData) then
        -- Do not display class-default values while the local save is still
        -- attaching. The delayed activation passes retry automatically.
        return false
    end

    -- This client renders its own vanilla menu while the same physical input
    -- is authenticated and relayed to couch Player 2 on the host. Keeping the
    -- local CommonUI widget focused gives the friend an independent skills /
    -- cosmetics view without turning either screen into a video stream.
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
                "personal_menu=client-visible reason=%s widget=%s owner=%s localPlayer=%s save=%s focus=%s render=%s input=%s refresh=%s",
                tostring(reason),
                widgetName,
                objectName(owner),
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
        "personal_menu=focus-failed reason=%s widget=%s detail=%s",
        tostring(reason),
        widgetName,
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
                preparePersonalMenu(widget, path .. ":immediate")
                for _, delayMs in ipairs({ 1, 75, 250, 750 }) do
                    local delay = delayMs
                    ExecuteInGameThreadWithDelay(delay, function()
                        preparePersonalMenu(widget, path .. ":" .. tostring(delay) .. "ms")
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

local function restoreHudWidget(widget, reason)
    if not isValid(widget) then
        return false
    end
    local widgetName = objectName(widget)
    if string.find(widgetName, "Default__", 1, true) then
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
            "hud=restored reason=%s widget=%s ok=%s detail=%s",
            tostring(reason),
            widgetName,
            tostring(ok),
            ok and "nil" or tostring(detail)))
    end
    return ok
end

local function restoreLocalHud(reason)
    local restoredSubsystems = 0
    local subsystemOk, subsystems = pcall(function()
        return FindAllOf("PagodaUISubsystem") or {}
    end)
    if subsystemOk then
        for _, subsystem in ipairs(subsystems) do
            if isValid(subsystem) then
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
                if restoreHudWidget(widget, reason) then
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
                restoreHudWidget(widget, path)
                restoreLocalHud(path)
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

local clientTravelCurtainHeld = false
local clientTravelCurtainRecoveryReady = false
local clientTravelCurtainReadyChecks = 0
local clientTravelCurtainGeneration = 0
local clientLoadingWidget = nil
local clientLoadingWidgetStage = "CONNECTING TO LIMELIGHTMP"
local clientLoadingWidgetDetail = "KEEPING CHUCKLES ON THE RIGHT SIDE OF REALITY..."
local clientLoadingWidgetFailureGeneration = -1
local releaseClientTravelCurtain

local function setClientLoadingText(widget, propertyName, value)
    local textBlock = readProperty(widget, propertyName)
    local textLibrary = UEHelpers.GetKismetTextLibrary()
    if not isValid(textBlock) or not isValid(textLibrary) then
        return false
    end

    local ok = pcall(function()
        textBlock:SetText(textLibrary:Conv_StringToText(value))
    end)
    return ok
end

local function removeClientLoadingWidget(reason)
    local removed = false
    if isValid(clientLoadingWidget) then
        removed = pcall(function()
            clientLoadingWidget:RemoveFromParent()
        end)
    end
    clientLoadingWidget = nil
    if removed then
        report("loading_widget=removed reason=" .. tostring(reason))
    end
    return removed
end

local function updateClientLoadingWidget(stage, detail, reason)
    if not ENABLE_CLIENT_LOADING_WIDGET or not clientTravelCurtainHeld then
        return false
    end

    clientLoadingWidgetStage = stage or clientLoadingWidgetStage
    clientLoadingWidgetDetail = detail or clientLoadingWidgetDetail

    if not isValid(clientLoadingWidget) then
        clientLoadingWidget = nil
        local world = UEHelpers.GetWorld()
        local controller = UEHelpers.GetPlayerController()
        if not isValid(world) or not isValid(controller) then
            return false
        end

        local assetOk = true
        local assetDetail = "soft-class-blocking"
        local classOk, widgetClass = pcall(function()
            local systemLibrary = UEHelpers.GetKismetSystemLibrary()
            if not isValid(systemLibrary) then
                error("KismetSystemLibrary is unavailable")
            end
            -- I ask Unreal for the cooked class directly. The little sidecar pak
            -- is mounted just fine; it simply skipped the original guest list.
            local softPath = systemLibrary:MakeSoftClassPath(CLIENT_LOADING_WIDGET_CLASS)
            local softClass = systemLibrary:Conv_SoftClassPathToSoftClassRef(softPath)
            return systemLibrary:LoadClassAsset_Blocking(softClass)
        end)
        local libraryOk, widgetLibrary = pcall(function()
            return StaticFindObject(WIDGET_BLUEPRINT_LIBRARY)
        end)

        if not assetOk or not classOk or not isValid(widgetClass) or
           not libraryOk or not isValid(widgetLibrary) then
            if clientLoadingWidgetFailureGeneration ~= clientTravelCurtainGeneration then
                clientLoadingWidgetFailureGeneration = clientTravelCurtainGeneration
                report(string.format(
                    "loading_widget=unavailable reason=%s assetOk=%s asset=%s classOk=%s class=%s libraryOk=%s library=%s",
                    tostring(reason),
                    tostring(assetOk),
                    assetOk and objectName(assetDetail) or tostring(assetDetail),
                    tostring(classOk),
                    objectName(widgetClass),
                    tostring(libraryOk),
                    objectName(widgetLibrary)))
            end
            return false
        end

        local created, widget = pcall(function()
            return widgetLibrary:Create(world, widgetClass, controller)
        end)
        if not created or not isValid(widget) then
            if clientLoadingWidgetFailureGeneration ~= clientTravelCurtainGeneration then
                clientLoadingWidgetFailureGeneration = clientTravelCurtainGeneration
                report(string.format(
                    "loading_widget=create-failed reason=%s detail=%s",
                    tostring(reason),
                    tostring(widget)))
            end
            return false
        end

        local added, addDetail = pcall(function()
            widget:AddToViewport(10000)
        end)
        if not added then
            pcall(function()
                widget:RemoveFromParent()
            end)
            if clientLoadingWidgetFailureGeneration ~= clientTravelCurtainGeneration then
                clientLoadingWidgetFailureGeneration = clientTravelCurtainGeneration
                report(string.format(
                    "loading_widget=viewport-failed reason=%s detail=%s",
                    tostring(reason),
                    tostring(addDetail)))
            end
            return false
        end

        clientLoadingWidget = widget
        clientLoadingWidgetFailureGeneration = -1
        report(string.format(
            "loading_widget=created reason=%s widget=%s",
            tostring(reason),
            objectName(widget)))
    end

    local stageOk = setClientLoadingText(
        clientLoadingWidget,
        "LoadingStageText",
        clientLoadingWidgetStage)
    local detailOk = setClientLoadingText(
        clientLoadingWidget,
        "LoadingDetailText",
        clientLoadingWidgetDetail)
    local versionOk = setClientLoadingText(
        clientLoadingWidget,
        "VersionText",
        "LIMELIGHTMP " .. MOD_VERSION)

    report(string.format(
        "loading_widget=updated reason=%s stage=%s stageOk=%s detailOk=%s versionOk=%s",
        tostring(reason),
        clientLoadingWidgetStage,
        tostring(stageOk),
        tostring(detailOk),
        tostring(versionOk)))
    return stageOk and detailOk and versionOk
end

local function showClientTravelCurtain(reason)
    if not ENABLE_CLIENT_TRAVEL_CURTAIN or not clientTravelCurtainHeld then
        return false
    end

    local shown = 0
    local ok, layouts = pcall(function()
        return FindAllOf("UI_Layout_Game_C") or {}
    end)
    if ok then
        for _, layout in ipairs(layouts) do
            local panel = readProperty(layout, "FadeToBlackPanel")
            if isValid(panel) then
                local showOk = pcall(function()
                    panel:ShowInstantly()
                end)
                if showOk then
                    shown = shown + 1
                end
            end
        end
    end

    report(string.format(
        "travel_curtain=show reason=%s panels=%d",
        tostring(reason),
        shown))
    updateClientLoadingWidget(nil, nil, reason)
    return shown > 0
end

local function beginClientTravelCurtain(reason, stage, detail)
    if not ENABLE_CLIENT_TRAVEL_CURTAIN then
        return
    end

    clientTravelCurtainHeld = true
    clientTravelCurtainRecoveryReady = false
    clientTravelCurtainReadyChecks = 0
    clientTravelCurtainGeneration = clientTravelCurtainGeneration + 1
    clientLoadingWidgetStage = stage or "LOADING THE HOST WORLD"
    clientLoadingWidgetDetail = detail or
        "UNREAL IS MOVING EVERYONE WITHOUT DROPPING CHUCKLES..."
    clientLoadingWidgetFailureGeneration = -1
    local generation = clientTravelCurtainGeneration
    showClientTravelCurtain(reason)
    -- I take the curtain down if networking spends twenty seconds looking for
    -- its other shoe. A visible retry is kinder than an immaculate black void.
    ExecuteInGameThreadWithDelay(20000, function()
        if clientTravelCurtainHeld and generation == clientTravelCurtainGeneration then
            releaseClientTravelCurtain("safety-timeout")
        end
    end)
end

local function refreshClientTravelCurtain(reason)
    if clientTravelCurtainHeld then
        showClientTravelCurtain(reason)
    end
end

releaseClientTravelCurtain = function(reason)
    if not clientTravelCurtainHeld then
        return false
    end

    clientTravelCurtainHeld = false
    clientTravelCurtainRecoveryReady = false
    clientTravelCurtainReadyChecks = 0
    removeClientLoadingWidget(reason)
    local hidden = 0
    local ok, panels = pcall(function()
        return FindAllOf("WBP_FadeToBlack_C") or {}
    end)
    if ok then
        for _, panel in ipairs(panels) do
            local panelName = objectName(panel)
            if isValid(panel) and not string.find(panelName, "Default__", 1, true) then
                local hideOk = pcall(function()
                    panel:HideInstantly()
                end)
                if hideOk then
                    hidden = hidden + 1
                end
            end
        end
    end

    report(string.format(
        "travel_curtain=release reason=%s panels=%d",
        tostring(reason),
        hidden))
    return hidden > 0
end

local function hideStaleFadePanels(reason)
    if clientTravelCurtainHeld then
        report("travel_curtain=stale-fade-cleanup-deferred reason=" .. tostring(reason))
        return false
    end
    local hidden = 0
    local ok, panels = pcall(function()
        return FindAllOf("WBP_FadeToBlack_C") or {}
    end)
    if not ok then
        return false
    end
    for _, panel in ipairs(panels) do
        local panelName = objectName(panel)
        if isValid(panel) and not string.find(panelName, "Default__", 1, true) then
            local hideOk = pcall(function()
                panel:HideInstantly()
            end)
            if hideOk then
                hidden = hidden + 1
            end
        end
    end
    if hidden > 0 then
        report(string.format(
            "transition_recovery=fade-panels-hidden reason=%s count=%d",
            tostring(reason),
            hidden))
    end
    return hidden > 0
end

local function recoverClientTransitionPresentation(reason)
    if worldTransitioning then
        return false
    end

    local controller = UEHelpers.GetPlayerController()
    local proxy = readProperty(controller, "Pawn")
    if not isValid(controller) or not isValid(proxy) then
        return false
    end

    local ok, detail = pcall(function()
        local cameraManager = readProperty(controller, "PlayerCameraManager")
        if isValid(cameraManager) then
            cameraManager:StopCameraFade()
            cameraManager:SetManualCameraFade(
                0.0,
                { R = 0.0, G = 0.0, B = 0.0, A = 1.0 },
                false)
        end
        hideStaleFadePanels(reason)
        restoreLocalHud(reason)
        controller:ResetIgnoreMoveInput()
        controller:ResetIgnoreLookInput()
        controller.bAutoManageActiveCameraTarget = false
        controller:SetViewTargetWithBlend(proxy, 0.0, 0, 0.0, false)
        local followCamera = readProperty(proxy, "FollowCamera")
        if isValid(followCamera) then
            followCamera:SetActive(true, true)
            followCamera:Activate(true)
        end
        cameraViewBound = true
    end)
    report(string.format(
        "transition_recovery=client-presentation reason=%s ok=%s detail=%s",
        tostring(reason),
        tostring(ok),
        ok and "nil" or tostring(detail)))
    if ok and clientTravelCurtainHeld then
        clientTravelCurtainRecoveryReady = true
        updateClientLoadingWidget(
            "SETTING THE CLIENT CAMERA",
            "PUTTING CHUCKLES BACK IN THE RIGHT REALITY...",
            reason .. ":recovery-ready")
        report("travel_curtain=recovery-ready reason=" .. tostring(reason))
    end
    return ok
end

local function scheduleClientTransitionRecovery(reason)
    transitionRecoveryGeneration = transitionRecoveryGeneration + 1
    local generation = transitionRecoveryGeneration
    report(string.format(
        "transition_recovery=scheduled reason=%s generation=%d mode=stable",
        tostring(reason),
        generation))

    local checks = 0
    local stableChecks = 0
    local check
    check = function()
        if generation ~= transitionRecoveryGeneration or worldTransitioning then
            return
        end

        checks = checks + 1
        local controller = UEHelpers.GetPlayerController()
        local presentationReady = isValid(controller) and
            isValid(readProperty(controller, "Pawn"))
        stableChecks = presentationReady and (stableChecks + 1) or 0

        if stableChecks >= 3 then
            local ok = recoverClientTransitionPresentation(reason .. ":stable")
            report(string.format(
                "transition_recovery=settled reason=%s generation=%d checks=%d ok=%s",
                tostring(reason),
                generation,
                checks,
                tostring(ok)))
            return
        end

        if checks >= 32 then
            report(string.format(
                "transition_recovery=yield-timeout reason=%s generation=%d checks=%d",
                tostring(reason),
                generation,
                checks))
            return
        end

        ExecuteInGameThreadWithDelay(250, check)
    end

    ExecuteInGameThreadWithDelay(150, check)
end

local function smoothTargetAnchor(location, targetName)
    local x = tonumber(readMember(location, "X")) or 0.0
    local y = tonumber(readMember(location, "Y")) or 0.0
    local z = tonumber(readMember(location, "Z")) or 0.0

    if smoothedTargetName ~= targetName or smoothedTargetX == nil then
        smoothedTargetName = targetName
        smoothedTargetX = x
        smoothedTargetY = y
        smoothedTargetZ = z
        return x, y, z
    end

    local dx = x - smoothedTargetX
    local dy = y - smoothedTargetY
    local dz = z - smoothedTargetZ
    local distanceSquared = dx * dx + dy * dy + dz * dz
    if distanceSquared >= CAMERA_ANCHOR_SNAP_DISTANCE_SQUARED or
       math.abs(dz) >= CAMERA_ANCHOR_SNAP_VERTICAL_DISTANCE then
        smoothedTargetX = x
        smoothedTargetY = y
        smoothedTargetZ = z
        scheduleClientTransitionRecovery("replicated-chuckles-jump")
    else
        smoothedTargetX = smoothedTargetX + dx * CAMERA_ANCHOR_BLEND
        smoothedTargetY = smoothedTargetY + dy * CAMERA_ANCHOR_BLEND
        smoothedTargetZ = smoothedTargetZ + dz * CAMERA_ANCHOR_BLEND
    end
    return smoothedTargetX, smoothedTargetY, smoothedTargetZ
end

local function sameCurrentWorld(object)
    local world = UEHelpers.GetWorld()
    if not isValid(object) or not isValid(world) then
        return false
    end

    local ok, objectWorld = pcall(function()
        return object:GetWorld()
    end)
    return ok and isValid(objectWorld) and objectName(objectWorld) == objectName(world)
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
            UEHelpers.FindOrAddFName("LimelightMPLocalRenderClientStatus"))
    end)
    if not ok and not statusFailureReported then
        statusFailureReported = true
        report("status=failed detail=" .. tostring(detail))
    end
end

local function isGameplayWorld()
    local worldName = string.lower(objectName(UEHelpers.GetWorld()))
    return string.find(worldName, "/startup/", 1, true) == nil and
        string.find(worldName, "/main_menu/", 1, true) == nil and
        worldName ~= "<invalid>"
end

local function isConnectedToHost()
    local driver = readProperty(UEHelpers.GetWorld(), "NetDriver")
    return isValid(driver) and isValid(readProperty(driver, "ServerConnection"))
end

local function isConnectionRecoveryWorld()
    local worldName = string.lower(objectName(UEHelpers.GetWorld()))
    return string.find(worldName, "/main_menu/", 1, true) ~= nil or
        string.find(worldName, "/startup", 1, true) ~= nil
end

local function localController()
    return UEHelpers.GetPlayerController()
end

local function localPawn()
    return readProperty(localController(), "Pawn")
end

local function findReplicatedChuckles(proxyOverride)
    local proxy = proxyOverride or localPawn()
    local proxyName = objectName(proxy)
    local candidates = {}
    local seen = {}

    for _, classToFind in ipairs({ "PagodaPlayerCharacter", "PagodaCharacter" }) do
        local ok, found = pcall(function()
            return FindAllOf(classToFind) or {}
        end)
        if ok then
            for _, candidate in ipairs(found) do
                local name = objectName(candidate)
                if not seen[name] then
                    seen[name] = true
                    table.insert(candidates, candidate)
                end
            end
        end
    end

    for _, candidate in ipairs(candidates) do
        local candidateClass = string.lower(className(candidate))
        if isValid(candidate) and
           sameCurrentWorld(candidate) and
           objectName(candidate) ~= proxyName and
           string.find(candidateClass, "chuckles", 1, true) then
            return candidate
        end
    end
    return nil
end

local function findCurrentWorldObject(classToFind)
    local ok, found = pcall(function()
        return FindAllOf(classToFind) or {}
    end)
    if not ok then
        return nil
    end
    for _, candidate in ipairs(found) do
        if isValid(candidate) and sameCurrentWorld(candidate) then
            return candidate
        end
    end
    return nil
end

local function configureSongSyncComponent(component, reason)
    if not isValid(component) then
        return false
    end

    local ok, detail = pcall(function()
        -- Keep Pagoda's native replicated song correction, but leave enough
        -- tolerance that ordinary packet jitter does not cause audible seeks.
        component.MaxAllowedClientDiviation = MAX_CLIENT_RHYTHM_DEVIATION
    end)
    if ok and not rhythmSyncArmedReported then
        rhythmSyncArmedReported = true
        report(string.format(
            "rhythm_sync=armed reason=%s maxDeviation=%.3f",
            tostring(reason),
            MAX_CLIENT_RHYTHM_DEVIATION))
    end
    if not ok then
        report("rhythm_sync=configure-failed detail=" .. tostring(detail))
    end
    return ok
end

local function installRhythmSyncHook()
    if rhythmSyncHookRegistered then
        return
    end

    local ok, preId, postId = pcall(function()
        return RegisterHook(
            "/Script/Pagoda.PagodaSongPlayerComponent:OnRep_SongRuntimeData",
            function(componentParameter, oldDataParameter)
                configureSongSyncComponent(
                    parameterValue(componentParameter),
                    "replicated-song-update")
            end)
    end)
    rhythmSyncHookRegistered = ok
    report(string.format(
        "rhythm_sync=hooked ok=%s pre=%s post=%s detail=%s",
        tostring(ok),
        tostring(preId),
        tostring(postId),
        ok and "nil" or tostring(preId)))
end

local function resetMusicRecovery()
    musicRecoveryWorldName = objectName(UEHelpers.GetWorld())
    musicRecoveryAttempts = 0
    musicRecoverySucceeded = false
    musicRecoveryLastAttemptTick = -1000
    musicRecoveryPendingUntilTick = -1000
end

local function tryRecoverMusic(tickCount, reason)
    if musicRecoverySucceeded or musicRecoveryAttempts >= MUSIC_RECOVERY_MAX_ATTEMPTS or
       not isGameplayWorld() or not isConnectedToHost() then
        return false
    end
    -- Let the level's own audio startup finish before deciding music is absent.
    if tickCount < 180 then
        return false
    end
    if tickCount < musicRecoveryPendingUntilTick or
       tickCount - musicRecoveryLastAttemptTick < MUSIC_RECOVERY_RETRY_TICKS then
        return false
    end

    local currentWorldName = objectName(UEHelpers.GetWorld())
    if musicRecoveryWorldName ~= currentWorldName then
        resetMusicRecovery()
    end

    local musicSubsystem = findCurrentWorldObject("PagodaMusicSubsystem")
    local gameState = findCurrentWorldObject("PagodaGameState")
    local songComponent = readProperty(gameState, "SongPlayerComponent")
    local runtimeData = readProperty(songComponent, "SongRuntimeData")
    if not isValid(musicSubsystem) or not isValid(songComponent) or runtimeData == nil then
        return false
    end
    configureSongSyncComponent(songComponent, reason)

    local serverTime = tonumber(readMember(runtimeData, "CurrentSongTime")) or 0.0
    local serverPaused = readMember(runtimeData, "bPaused") == true
    if serverPaused or serverTime < 0.25 then
        return false
    end

    local stateOk, localPlaying, localSilent = pcall(function()
        return musicSubsystem:IsSongPlaying(), musicSubsystem:IsPlayingSilentSong()
    end)
    if stateOk and localPlaying and not localSilent then
        local alignOk = pcall(function()
            musicSubsystem:SetTimelinePosition(serverTime + 0.10)
        end)
        musicRecoverySucceeded = true
        report(string.format(
            "music=healthy-aligned reason=%s playing=%s silent=%s serverTime=%.2f aligned=%s",
            tostring(reason),
            tostring(localPlaying),
            tostring(localSilent),
            serverTime,
            tostring(alignOk)))
        return true
    end

    musicRecoveryAttempts = musicRecoveryAttempts + 1
    musicRecoveryLastAttemptTick = tickCount
    local serverSong = readMember(runtimeData, "CurrentSong")
    local song = isValid(serverSong) and serverSong or nil
    local source = "replicated-song"
    if not isValid(song) then
        local defaultOk, defaultSong = pcall(function()
            return musicSubsystem:GetLevelDefaultSong()
        end)
        if defaultOk and isValid(defaultSong) then
            song = defaultSong
            source = "level-default"
        end
    end
    if not isValid(song) then
        report(string.format(
            "music=recovery-waiting attempt=%d reason=no-local-song serverTime=%.2f",
            musicRecoveryAttempts,
            serverTime))
        return false
    end

    local recoverOk, recoverDetail = pcall(function()
        musicSubsystem:PlaySong(song, false)
    end)
    if recoverOk then
        -- FMOD/song initialization is asynchronous. Do not seek in the same
        -- call and do not mark the attempt healthy until the player reports a
        -- real, non-silent song. The next health check performs the one-time
        -- alignment, avoiding the repeated hard seeks that broke beat timing.
        musicRecoveryPendingUntilTick = tickCount + 30
    end
    report(string.format(
        "music=recovery attempt=%d source=%s song=%s serverTime=%.2f ok=%s alignment=delayed detail=%s",
        musicRecoveryAttempts,
        source,
        objectName(song),
        serverTime,
        tostring(recoverOk),
        recoverOk and "nil" or tostring(recoverDetail)))
    return recoverOk
end

local function resetCamera(reason)
    local oldProxyName = cameraProxyPawnName or "<none>"
    cameraProxyPawnName = nil
    cameraTargetName = nil
    cameraViewBound = false
    resetCameraSmoothing()
    report(string.format("camera=reset reason=%s proxy=%s", tostring(reason), oldProxyName))
end

local function ensureCamera(controller, proxy)
    if not isValid(controller) or not isValid(proxy) then
        return false, "controller-or-proxy-invalid"
    end

    local proxyName = objectName(proxy)
    if cameraProxyPawnName ~= nil and cameraProxyPawnName ~= proxyName then
        resetCamera("proxy-changed")
    end

    local followCamera = readProperty(proxy, "FollowCamera")
    local rootComponent = readProperty(proxy, "RootComponent")
    if not isValid(followCamera) or not isValid(rootComponent) then
        return false, "vanilla-camera-components-invalid"
    end

    local first = cameraProxyPawnName ~= proxyName
    cameraProxyPawnName = proxyName
    local ok, detail = pcall(function()
        -- This pawn is only a local vanilla camera rig. Prevent server movement
        -- corrections from tugging it away from the replicated Chuckles anchor.
        proxy:SetActorHiddenInGame(true)
        -- Keep local query collision available so Dive Bar interactables can
        -- construct this client's own skill/cosmetic widgets. The host keeps
        -- this render-only pawn non-colliding authoritatively.
        proxy:SetActorEnableCollision(true)
        followCamera:SetActive(true, true)
        followCamera:Activate(true)
    end)
    if not ok then
        return false, "vanilla-camera-setup-failed:" .. tostring(detail)
    end
    -- These are best-effort because some Blueprint pawn variants expose only
    -- a subset of the native Character properties.
    pcall(function()
        proxy.bCanBeDamaged = false
        proxy.bReplicateMovement = false
        proxy:SetReplicateMovement(false)
    end)
    pcall(function()
        local movement = readProperty(proxy, "CharacterMovement")
        if isValid(movement) then
            movement:StopMovementImmediately()
            movement:DisableMovement()
            movement.GravityScale = 0.0
        end
    end)
    if first then
        cameraViewBound = false
        report(string.format(
            "camera=vanilla-proxy-ready proxy=%s component=%s",
            proxyName,
            objectName(followCamera)))
    end
    return true, nil
end

local function bindCamera(controller, proxy, reason)
    if not isValid(controller) or not isValid(proxy) then
        cameraViewBound = false
        return false
    end

    local ok, detail = pcall(function()
        controller.bAutoManageActiveCameraTarget = false
        controller:SetViewTargetWithBlend(proxy, 0.0, 0, 0.0, false)
    end)
    if ok then
        if not cameraViewBound then
            report(string.format(
                "camera=vanilla-proxy-bound reason=%s proxy=%s component=%s",
                tostring(reason),
                objectName(proxy),
                objectName(readProperty(proxy, "FollowCamera"))))
        end
        cameraViewBound = true
    else
        cameraViewBound = false
        report("camera=bind-failed detail=" .. tostring(detail))
    end
    return ok
end

local function clearCameraReferences()
    cachedController = nil
    cachedProxy = nil
    cachedTarget = nil
    cachedFollowCamera = nil
    cachedProxyMesh = nil
    cachedTargetMesh = nil
    cachedTargetIdentity = nil
    cachedProxyAnchorOffsetX = 0.0
    cachedProxyAnchorOffsetY = 0.0
    cachedProxyAnchorOffsetZ = 0.0
    cachedUseSmoothedTargetMesh = false
    cameraViewBound = false
    resetCameraSmoothing()
end

local function refreshCameraReferences(reason)
    local controller = localController()
    local proxy = readProperty(controller, "Pawn")
    local target = findReplicatedChuckles(proxy)

    cachedController = controller
    cachedProxy = proxy
    cachedTarget = target
    cachedFollowCamera = readProperty(proxy, "FollowCamera")
    cachedProxyMesh = readProperty(proxy, "Mesh")
    cachedTargetMesh = readProperty(target, "Mesh")
    cachedTargetIdentity = isValid(target) and objectName(target) or nil
    cachedUseSmoothedTargetMesh = isValid(cachedTargetMesh)

    if isValid(proxy) and isValid(cachedProxyMesh) then
        pcall(function()
            local proxyActorLocation = proxy:K2_GetActorLocation()
            local proxyAnchorLocation = cachedProxyMesh:K2_GetComponentLocation()
            cachedProxyAnchorOffsetX =
                (tonumber(readMember(proxyAnchorLocation, "X")) or 0.0) -
                (tonumber(readMember(proxyActorLocation, "X")) or 0.0)
            cachedProxyAnchorOffsetY =
                (tonumber(readMember(proxyAnchorLocation, "Y")) or 0.0) -
                (tonumber(readMember(proxyActorLocation, "Y")) or 0.0)
            cachedProxyAnchorOffsetZ =
                (tonumber(readMember(proxyAnchorLocation, "Z")) or 0.0) -
                (tonumber(readMember(proxyActorLocation, "Z")) or 0.0)
        end)
    end

    if isValid(proxy) then
        -- Do this on maintenance refreshes, not every rendered frame.
        pcall(function()
            proxy:SetActorHiddenInGame(true)
            proxy:SetActorEnableCollision(true)
        end)
    end

    if isValid(target) then
        -- Some story segment transitions leave the replicated couch pawn's
        -- presentation flags in their cinematic state on the render client.
        pcall(function()
            target:SetActorHiddenInGame(false)
            if isValid(cachedTargetMesh) then
                cachedTargetMesh:SetHiddenInGame(false, true)
                cachedTargetMesh:SetVisibility(true, true)
                cachedTargetMesh.bOwnerNoSee = false
                cachedTargetMesh.bOnlyOwnerSee = false
                cachedTargetMesh.bEnableUpdateRateOptimizations = false
                cachedTargetMesh:SetComponentTickEnabled(true)
            end
            target:SetActorTickEnabled(true)
        end)
    end

    if isValid(controller) and isValid(proxy) and isValid(target) then
        local ensured = ensureCamera(controller, proxy)
        if ensured and (not cameraViewBound or reason == "periodic-health") then
            bindCamera(controller, proxy, reason)
        end
        return ensured
    end
    return false
end

local function proxyTransform(proxy, target)
    local ok, targetAnchorLocation, targetRotation = pcall(function()
        local targetAnchor
        if cachedUseSmoothedTargetMesh then
            targetAnchor = cachedTargetMesh:K2_GetComponentLocation()
        else
            targetAnchor = target:K2_GetActorLocation()
        end
        return targetAnchor, target:K2_GetActorRotation()
    end)
    if not ok then
        return false, nil, nil, tostring(targetAnchorLocation)
    end

    local transformOk, detail = pcall(function()
        local targetX, targetY, targetZ = smoothTargetAnchor(
            targetAnchorLocation,
            cachedTargetIdentity or "<target>")
        targetAnchorLocation.X = targetX - cachedProxyAnchorOffsetX
        targetAnchorLocation.Y = targetY - cachedProxyAnchorOffsetY
        targetAnchorLocation.Z = targetZ - cachedProxyAnchorOffsetZ
    end)
    if not transformOk then
        return false, nil, nil, tostring(detail)
    end
    return true, targetAnchorLocation, targetRotation, nil
end

local function updateCamera()
    local controller = cachedController
    local proxy = cachedProxy
    local target = cachedTarget
    if not isValid(controller) or not isValid(proxy) or not isValid(target) then
        return false, "waiting-for-controller-proxy-or-chuckles"
    end

    -- Camera activation, collision, and movement setup are maintenance work.
    -- Reapplying all of it every rendered frame caused regular frame-time
    -- spikes. The reference refresh performs that setup on acquisition and on
    -- the much slower health interval.
    if not isValid(cachedFollowCamera) then
        return false, "vanilla-camera-component-invalid"
    end

    local transformOk, location, rotation, transformDetail =
        proxyTransform(proxy, target)
    if not transformOk then
        return false, transformDetail
    end

    local placeOk, placeDetail = pcall(function()
        return proxy:K2_SetActorLocationAndRotation(
            location,
            rotation,
            false,
            cameraSweepHitResult,
            true)
    end)
    if not placeOk then
        return false, tostring(placeDetail)
    end

    local targetName = cachedTargetIdentity or "<target>"
    cameraUpdates = cameraUpdates + 1
    if cameraTargetName ~= targetName then
        cameraTargetName = targetName
        report(string.format(
            "camera=following-with-vanilla-rig target=%s class=%s proxy=%s",
            targetName,
            className(target),
            objectName(proxy)))
        report(string.format(
            "camera_anchor=%s blend=%.2f",
            cachedUseSmoothedTargetMesh and "smoothed-mesh" or "actor-root",
            CAMERA_ANCHOR_BLEND))
        showStatus("Rendering host-owned Chuckles locally.", 8.0, {
            R = 0.25, G = 1.00, B = 0.45, A = 1.00
        })
    elseif cameraUpdates % 600 == 0 then
        report(string.format("camera=healthy updates=%d target=%s", cameraUpdates, targetName))
    end
    if clientTravelCurtainHeld and clientTravelCurtainRecoveryReady then
        clientTravelCurtainReadyChecks = clientTravelCurtainReadyChecks + 1
        if clientTravelCurtainReadyChecks >= 3 then
            releaseClientTravelCurtain("recovery-and-camera-stable")
        end
    end
    return true, nil
end

local function refreshDeferredMultiplayerHooks(reason)
    -- Blueprint hooks are map assets, not startup assets. I keep retrying the
    -- individual missing paths as Pagoda streams them in, then repair widgets
    -- which were already born before their hook arrived at the party.
    installVersionWatermark()
    installDialogueInputHooks()
    installPersonalMenuHooks()
    installHudPresentationHooks()
    installRhythmSyncHook()
    installHazardPresentationHooks()
    applyVersionWatermarkToLoadedLayouts(reason)
    prepareLoadedDialogue(reason)
    restoreLocalHud(reason)

    for _, classToFind in ipairs({
        "BP_GroundImpactIndicator_C",
        "BP_TraceImpactIndicator_C"
    }) do
        local ok, actors = pcall(function()
            return FindAllOf(classToFind) or {}
        end)
        if ok then
            for _, actor in ipairs(actors) do
                restoreHazardPresentation(actor, reason)
            end
        end
    end
end

local function startCameraLoop(reason)
    cameraGeneration = cameraGeneration + 1
    local generation = cameraGeneration
    cameraUpdates = 0
    cameraLastReferenceRefreshTick = -1000
    report(string.format("camera_loop=started reason=%s generation=%d", tostring(reason), generation))

    local waitingReported = false
    local ticks = 0
    local tick
    tick = function()
        if generation ~= cameraGeneration then
            return
        end
        if not worldTransitioning then
            ticks = ticks + 1
            -- Full reflected character discovery and view-target rebinding are
            -- comparatively expensive. Valid cached objects can be followed
            -- directly; perform the health scan about every five seconds or
            -- immediately if any required reference becomes invalid.
            local referencesInvalid =
                not isValid(cachedController) or not isValid(cachedProxy) or
                not isValid(cachedTarget) or not isValid(cachedFollowCamera)
            local refreshDue = ticks == 1 or ticks % 300 == 0 or
                (referencesInvalid and ticks - cameraLastReferenceRefreshTick >= 15)
            if refreshDue then
                cameraLastReferenceRefreshTick = ticks
                refreshCameraReferences(ticks == 1 and reason or "periodic-health")
            end
            if ticks == 300 then
                refreshDeferredMultiplayerHooks("camera-loop:" .. tostring(ticks))
            end
            local ok, detail = updateCamera()
            if not ok and not waitingReported then
                waitingReported = true
                report("camera=waiting detail=" .. tostring(detail))
                updateClientLoadingWidget(
                    "FINDING CHUCKLES",
                    "CHECKING CONTROLLER, CAMERA, AND OWNERSHIP...",
                    "camera-waiting")
            elseif ok then
                waitingReported = false
            end
            if ticks == 180 or ticks % 300 == 0 then
                tryRecoverMusic(ticks, ticks == 180 and "camera-ready" or "periodic-check")
            end
        end
        ExecuteInGameThreadWithDelay(16, tick)
    end
    ExecuteInGameThreadWithDelay(16, tick)
end

local function snapshot(reason)
    local world = UEHelpers.GetWorld()
    local controller = localController()
    local proxy = localPawn()
    refreshCameraReferences("snapshot")
    local target = cachedTarget
    local driver = readProperty(world, "NetDriver")
    report(string.format(
        "snapshot=%s version=%s joinIssued=%s world=%s driver=%s serverConnection=%s controller=%s proxy=%s proxyClass=%s target=%s targetClass=%s camera=%s updates=%d",
        tostring(reason),
        MOD_VERSION,
        tostring(joinIssued),
        objectName(world),
        objectName(driver),
        objectName(readProperty(driver, "ServerConnection")),
        objectName(controller),
        objectName(proxy),
        className(proxy),
        objectName(target),
        className(target),
        objectName(cachedFollowCamera),
        cameraUpdates))
end

local function tryJoin(reason)
    if joinIssued or worldTransitioning then
        return
    end
    if not string.match(CONNECT_ADDRESS, "^[%w%.%-%[%]:]+$") then
        joinIssued = true
        report("join=failed reason=invalid-address address=" .. tostring(CONNECT_ADDRESS))
        return
    end

    local world = UEHelpers.GetWorld()
    local controller = localController()
    local systemLibrary = UEHelpers.GetKismetSystemLibrary()
    if not isValid(world) or not isValid(controller) or not isValid(systemLibrary) then
        report("join=deferred reason=" .. tostring(reason))
        return
    end

    joinIssued = true
    beginClientTravelCurtain(
        "connection-attempt",
        "CONNECTING TO LIMELIGHTMP",
        "OPENING A LOCAL DOOR TO THE HOST...")
    local command = "open " .. CONNECT_ADDRESS
    local ok, detail = pcall(function()
        systemLibrary:ExecuteConsoleCommand(world, command, controller)
    end)
    report(string.format(
        "join=issued reason=%s address=%s ok=%s detail=%s",
        tostring(reason),
        CONNECT_ADDRESS,
        tostring(ok),
        ok and "nil" or tostring(detail)))
    showStatus("Connecting to the LimelightMP host...", 8.0, {
        R = 1.00, G = 0.80, B = 0.20, A = 1.00
    })
end

installRhythmSyncHook()

RegisterLoadMapPreHook(function()
    beginClientTravelCurtain(
        "map-transition",
        "LOADING THE HOST WORLD",
        "UNREAL IS MOVING EVERYONE WITHOUT DROPPING CHUCKLES...")
    worldTransitioning = true
    transitionRecoveryGeneration = transitionRecoveryGeneration + 1
    cameraGeneration = cameraGeneration + 1
    cameraTargetName = nil
    cameraUpdates = 0
    cameraLastReferenceRefreshTick = -1000
    clearCameraReferences()
    resetCamera("map-transition")
    resetMusicRecovery()
    rhythmSyncArmedReported = false
    preparedHazardActors = {}
    preparedHudWidgets = {}
    report("map_transition=started")
end)

RegisterLoadMapPostHook(function()
    worldTransitioning = false
    updateClientLoadingWidget(
        "SYNCHRONISING THE ARENA",
        "CHECKING OWNERSHIP, CAMERA, AND CONTROLS...",
        "map-load:stage")
    refreshClientTravelCurtain("map-load:immediate")
    for _, delayMs in ipairs({ 1, 75, 250, 750, 1500 }) do
        local delay = delayMs
        ExecuteInGameThreadWithDelay(delay, function()
            refreshClientTravelCurtain("map-load:" .. tostring(delay) .. "ms")
        end)
    end
    local worldName = string.lower(objectName(UEHelpers.GetWorld()))
    report("map_transition=finished world=" .. objectName(UEHelpers.GetWorld()))
    if isGameplayWorld() then
        for _, delayMs in ipairs({ 100, 1200 }) do
            local delay = delayMs
            ExecuteInGameThreadWithDelay(delay, function()
                refreshDeferredMultiplayerHooks("map-load:" .. tostring(delay) .. "ms")
            end)
        end
    end
    if joinIssued and isConnectionRecoveryWorld() then
        -- An ordinary story OpenLevel returns this client to Startup/Main Menu.
        -- Retry automatically while the host reopens that story map as a
        -- listen server.
        joinIssued = false
        report("join=recovery-scheduled world=" .. objectName(UEHelpers.GetWorld()))
        ExecuteInGameThreadWithDelay(2500, function()
            tryJoin("connection-recovery")
        end)
    elseif not joinIssued and string.find(worldName, "/main_menu/", 1, true) then
        ExecuteInGameThreadWithDelay(1500, function()
            tryJoin("main-menu-post-load")
        end)
    elseif joinIssued and isGameplayWorld() then
        ExecuteInGameThreadWithDelay(750, function()
            if isConnectedToHost() then
                restoreLocalHud("gameplay-map-load")
                scheduleClientTransitionRecovery("gameplay-map-load")
                startCameraLoop("gameplay-map-load")
                snapshot("post-map-load")
            else
                -- A locally opened Dive Bar is not a multiplayer success.
                -- Reissue the actual Unreal connection instead of presenting
                -- the friend's unrelated local pawn as host-owned Chuckles.
                joinIssued = false
                report("join=retry reason=gameplay-world-without-server-connection")
                showStatus("Game world is not connected to the host; retrying...", 8.0, {
                    R = 1.00, G = 0.55, B = 0.20, A = 1.00
                })
                tryJoin("gameplay-world-without-server-connection")
            end
        end)
    end
end)

RegisterKeyBind(Key.F6, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    ExecuteInGameThread(function()
        joinIssued = false
        tryJoin("manual-retry")
    end)
end)

RegisterKeyBind(Key.F9, {
    ModifierKey.CONTROL,
    ModifierKey.SHIFT
}, function()
    ExecuteInGameThread(function()
        if isGameplayWorld() then
            startCameraLoop("manual-rebind")
            showStatus("Rebinding the local Chuckles camera...", 6.0)
        end
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
    "Version %s loaded. This PC renders the replicated host world locally; no video is streamed. Auto-join=%s. Ctrl+Shift+F6 retries and Ctrl+Shift+F9 rebinds the camera.",
    MOD_VERSION,
    CONNECT_ADDRESS))

local joinRetryGeneration = 1
local function retryJoinLoop()
    local generation = joinRetryGeneration
    local tick
    tick = function()
        if generation ~= joinRetryGeneration or joinIssued then
            return
        end
        local worldName = string.lower(objectName(UEHelpers.GetWorld()))
        if string.find(worldName, "/main_menu/", 1, true) then
            tryJoin("main-menu-retry-loop")
        end
        ExecuteInGameThreadWithDelay(5000, tick)
    end
    ExecuteInGameThreadWithDelay(5000, tick)
end

retryJoinLoop()
