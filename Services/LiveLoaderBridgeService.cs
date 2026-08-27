using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limelight.Services
{
    public sealed class LiveLoaderBridgeService
    {
        private const string BridgeName =
            "LimelightBridge";

        private const string BridgeScript =
     """
    local localAppData = os.getenv("LOCALAPPDATA")

    if localAppData == nil then
        print("[LimelightBridge] LOCALAPPDATA could not be found\n")
        return
    end

    local runtimeDirectory =
        localAppData .. "\\Limelight\\Runtime"

    local sessionBypassPath =
        runtimeDirectory .. "\\live-loader-disabled.txt"

    local sessionBypassFile =
        io.open(sessionBypassPath, "r")

    if sessionBypassFile ~= nil then
        local sessionBypassExpiry =
            tonumber(sessionBypassFile:read("*a"))

        sessionBypassFile:close()

        if sessionBypassExpiry ~= nil and
           sessionBypassExpiry >= os.time() then
            print("[LimelightBridge] Live Loader disabled for this session\n")
            return
        end

        os.remove(sessionBypassPath)
    end

    local heartbeatPath =
        runtimeDirectory .. "\\heartbeat.txt"

    local commandPath =
        runtimeDirectory .. "\\command.txt"

    local responsePath =
        runtimeDirectory .. "\\response.txt"

    local mountFunctionsPath =
        runtimeDirectory .. "\\mount-functions.txt"

    local lastHeartbeatSecond = 0
    local lastRequestId = nil
    local worldTransitioning = false
    local worldSettling = false
    local transitionGeneration = 0
    local automaticCharlieRefreshEnabled = false
    local defaultPlayerMeshPath =
        "/Game/Pagoda/Characters/Player/Meshes/SK_Charlie.SK_Charlie"
    local defaultBodyCosmeticPath =
        "/Game/Pagoda/Cosmetics/Charlie/BodyType/PlayerCosmetic_Charlie_BodyType_Default.PlayerCosmetic_Charlie_BodyType_Default"
    local activeCustomMeshApplied = false
    -- I prefer Limelight's loader while keeping the original actor ID as a
    -- fallback for people who already installed it.
    local characterLoaderActorIds =
    {
        26082401,
        35005383
    }
    local registeredCharacterSlotDefinitions = {}
    local characterSlotCataloguePath =
        runtimeDirectory .. "\\character-slot-catalogue.txt"
    local characterSlotLoaderModePath =
        runtimeDirectory .. "\\character-slot-loader-mode.txt"
    local characterSlotCatalogueInitialised = false
    local activeCharacterSlotDefinitionPath = nil
    local activeCharacterSlotMeshPath = nil
    local activeCharacterSlotName = nil
    local baseCharacterDefinition = nil
    local activeCharliePortraitPath = nil
    local activeObjectPathsText = nil
    local activeObjectPathsRollbackText = nil
    local activeStringTablePaths = {}
    local knownCharliePortraitResources =
    {
        ["dialog_charlie_01"] = true
    }
    local knownCharliePortraitWidgets = {}
    local portraitRefreshPassesRemaining = 0
    local lastPortraitRefreshSecond = 0
    local localizedTextRefreshPassesRemaining = 0
    local lastLocalizedTextRefreshSecond = 0

    local function writeHeartbeat()
        local heartbeatFile =
            io.open(heartbeatPath, "w")

        if heartbeatFile == nil then
            return
        end

        heartbeatFile:write(
            tostring(os.time()))

        heartbeatFile:close()
    end

    local function readValues(path)
        local file = io.open(path, "r")

        if file == nil then
            return nil
        end

        local values = {}

        for line in file:lines() do
            local key, value =
                line:match("^([^=]+)=(.*)$")

            if key ~= nil then
                values[key] = value
            end
        end

        file:close()
        return values
    end

    local function splitPipeSeparated(value)
        local values = {}

        if value == nil or value == "" then
            return values
        end

        for item in string.gmatch(value, "([^|]+)") do
            table.insert(values, item)
        end

        return values
    end

    local function loadMountedAsset(objectPath)
        -- I ask the live object table first because Character Slot packages
        -- arrive after Unreal finished taking attendance at startup.
        local findSucceeded,
              existingAsset =
            pcall(function()
                return StaticFindObject(
                    objectPath)
            end)

        if findSucceeded and
           existingAsset ~= nil and
           existingAsset:IsValid() then

            return existingAsset,
                true,
                true
        end

        local loadCallSucceeded,
              asset,
              assetWasFound,
              assetWasLoaded =
            pcall(function()
                return LoadAsset(objectPath)
            end)

        if loadCallSucceeded and
           assetWasFound and
           assetWasLoaded and
           asset ~= nil and
           asset:IsValid() then

            return asset,
                true,
                true
        end

        -- If UE4SS asks the stale Asset Registry and gets a blank stare, I use
        -- BPModLoader's proven FAssetData route for the late arrival instead.
        local packagePath,
              assetName =
            string.match(
                objectPath,
                "^(.*)%.([^%.]+)$")

        if packagePath == nil or
           assetName == nil then

            return nil,
                false,
                false
        end

        local directCallSucceeded,
              directAsset =
            pcall(function()
                local assetRegistryHelpers =
                    StaticFindObject(
                        "/Script/AssetRegistry.Default__AssetRegistryHelpers")

                if assetRegistryHelpers == nil or
                   not assetRegistryHelpers:IsValid() then

                    return nil
                end

                local assetData = nil

                if UnrealVersion.IsBelow(5, 1) then
                    assetData =
                    {
                        ObjectPath =
                            UEHelpers.FindOrAddFName(
                                objectPath)
                    }
                else
                    assetData =
                    {
                        PackageName =
                            UEHelpers.FindOrAddFName(
                                packagePath),
                        AssetName =
                            UEHelpers.FindOrAddFName(
                                assetName)
                    }
                end

                return assetRegistryHelpers:GetAsset(
                    assetData)
            end)

        if directCallSucceeded and
           directAsset ~= nil and
           directAsset:IsValid() then

            print(
                "[LimelightBridge] Loaded an unregistered mounted asset directly: " ..
                objectPath ..
                "\n")

            return directAsset,
                true,
                true
        end

        return nil,
            false,
            false
    end

    local function writeResponse(
        requestId,
        success,
        message)

        local temporaryPath =
            responsePath .. ".tmp"

        local responseFile =
            io.open(temporaryPath, "w")

        if responseFile == nil then
            return
        end

        responseFile:write(
            "requestId=" .. tostring(requestId) .. "\n")

        responseFile:write(
            "success=" ..
            (success and "true" or "false") ..
            "\n")

        responseFile:write(
            "message=" .. tostring(message) .. "\n")

        responseFile:close()

        os.remove(responsePath)
        os.rename(
            temporaryPath,
            responsePath)
    end

    local function isCharliePortraitPath(lowerPath)
        return
            string.find(
                lowerPath,
                "/ui/art/dialog/portraits/dialog_charlie_01.",
                1,
                true) ~= nil or
            (string.find(lowerPath, "charlie", 1, true) ~= nil and
             (string.find(lowerPath, "portrait", 1, true) ~= nil or
              string.find(lowerPath, "/ui/", 1, true) ~= nil))
    end

    local function isStringTablePath(lowerPath)
        return
            string.find(lowerPath, "/localization/", 1, true) ~= nil or
            string.find(lowerPath, ".st_", 1, true) ~= nil or
            string.find(lowerPath, "stringtable", 1, true) ~= nil or
            string.find(lowerPath, "_st_", 1, true) ~= nil
    end

    local function getObjectResourceName(objectPath)
        if objectPath == nil then
            return nil
        end

        local objectName =
            string.match(objectPath, "%.([^%.%/]+)$")

        if objectName == nil then
            objectName =
                string.match(objectPath, "/([^/]+)$")
        end

        if objectName == nil or objectName == "" then
            return nil
        end

        return string.lower(objectName)
    end

    local function matchesKnownPortraitResource(resourceFullName)
        if resourceFullName == nil then
            return false
        end

        for fragment, _ in
            pairs(knownCharliePortraitResources) do

            if string.find(
                resourceFullName,
                fragment,
                1,
                true) ~= nil then
                return true
            end
        end

        return false
    end

    local function applyActiveAssets(objectPathsText)
        activeObjectPathsText = objectPathsText
        activeCharliePortraitPath = nil
        activeStringTablePaths = {}
        local seenStringTables = {}

        for _, objectPath in
            ipairs(splitPipeSeparated(objectPathsText)) do

            local lowerObjectPath =
                string.lower(objectPath)

            if isCharliePortraitPath(lowerObjectPath) then
                activeCharliePortraitPath = objectPath
                portraitRefreshPassesRemaining = 20
                lastPortraitRefreshSecond = 0

                local portraitResourceName =
                    getObjectResourceName(objectPath)

                if portraitResourceName ~= nil then
                    knownCharliePortraitResources[
                        portraitResourceName] = true
                end
            end

            if isStringTablePath(lowerObjectPath) and
               not seenStringTables[lowerObjectPath] then

                table.insert(
                    activeStringTablePaths,
                    objectPath)
                seenStringTables[lowerObjectPath] = true
            end
        end

        -- A portrait or string table can be created after the switch response.
        -- These extra passes catch widgets as the Dive Bar finishes building.
        if activeCharliePortraitPath ~= nil then
            portraitRefreshPassesRemaining = 30
            lastPortraitRefreshSecond = 0
        end

        if #activeStringTablePaths > 0 then
            localizedTextRefreshPassesRemaining = 30
            lastLocalizedTextRefreshSecond = 0
        end
    end

    local function rememberActiveAssets(objectPathsText)
        if activeObjectPathsRollbackText == nil then
            activeObjectPathsRollbackText =
                activeObjectPathsText or ""
        end

        applyActiveAssets(objectPathsText)
    end

    local function commitActiveAssets()
        activeObjectPathsRollbackText = nil
    end

    local function rollbackActiveAssets()
        if activeObjectPathsRollbackText == nil then
            return false
        end

        local rollbackObjectPathsText =
            activeObjectPathsRollbackText

        activeObjectPathsRollbackText = nil
        applyActiveAssets(rollbackObjectPathsText)

        return true
    end

    local function refreshCharliePortraitWidgets()
        if activeCharliePortraitPath == nil or
           activeCharliePortraitPath == "" then

            return 0
        end

        local portraitLoadSucceeded,
              activeCharliePortrait,
              portraitWasFound,
              portraitWasLoaded =
            pcall(function()
                return loadMountedAsset(
                    activeCharliePortraitPath)
            end)

        if not portraitLoadSucceeded or
           not portraitWasFound or
           not portraitWasLoaded or
           activeCharliePortrait == nil or
           not activeCharliePortrait:IsValid() then

            return 0
        end

        local imageWidgets =
            FindAllOf("Image")

        if imageWidgets == nil then
            return 0
        end

        local refreshedCount = 0

        for _, imageWidget in
            pairs(imageWidgets) do

            if imageWidget ~= nil and
               imageWidget:IsValid() then

                local widgetNameSucceeded,
                      widgetFullName =
                    pcall(function()
                        return string.lower(
                            imageWidget:GetFullName())
                    end)

                local isCharliePortrait =
                    widgetNameSucceeded and
                    knownCharliePortraitWidgets[
                        widgetFullName] == true

                local resourceReadSucceeded,
                      resourceObject =
                    pcall(function()
                        local brush =
                            imageWidget.Brush

                        if brush == nil then
                            return nil
                        end

                        return brush.ResourceObject
                    end)

                if not isCharliePortrait and
                   resourceReadSucceeded and
                   resourceObject ~= nil and
                   resourceObject:IsValid() then

                    local nameReadSucceeded,
                          resourceFullName =
                        pcall(function()
                            return string.lower(
                                resourceObject:GetFullName())
                        end)

                    isCharliePortrait =
                        nameReadSucceeded and
                        matchesKnownPortraitResource(
                            resourceFullName)
                end

                if not isCharliePortrait and
                   widgetNameSucceeded then

                    -- Some portrait widgets are built before their brush is
                    -- assigned. Their own name is the only stable clue during
                    -- that short window, so I remember it for later passes.
                    isCharliePortrait =
                        string.find(
                            widgetFullName,
                            "charlie",
                            1,
                            true) ~= nil and
                        (string.find(
                            widgetFullName,
                            "portrait",
                            1,
                            true) ~= nil or
                         string.find(
                            widgetFullName,
                            "dialog",
                            1,
                            true) ~= nil)
                end

                if isCharliePortrait then
                    if widgetNameSucceeded then
                        knownCharliePortraitWidgets[
                            widgetFullName] = true
                    end

                    local setSucceeded =
                        pcall(function()
                            -- Keep the widget's existing layout size while
                            -- replacing only the texture behind its brush.
                            imageWidget:SetBrushFromTexture(
                                activeCharliePortrait,
                                false)
                        end)

                    if setSucceeded then
                        refreshedCount =
                            refreshedCount + 1
                    end
                end
            end
        end

        return refreshedCount
    end

    local function refreshLocalizedTextWidgets()
        local refreshedCount = 0

        local textLibrarySucceeded,
              textLibrary =
            pcall(function()
                -- A fresh default object avoids keeping localized text from
                -- the previous character switch in UEHelpers' cache.
                return UEHelpers.GetKismetTextLibrary(true)
            end)

        for _, stringTablePath in
            ipairs(activeStringTablePaths) do

            pcall(function()
                -- I reload the active tables before refreshing widgets so a
                -- newly created menu sees the replacement localization data.
                loadMountedAsset(stringTablePath)
            end)
        end

        local widgetClasses =
        {
            "TextBlock",
            "RichTextBlock"
        }

        for _, widgetClass in ipairs(widgetClasses) do
            local widgets = FindAllOf(widgetClass)

            if widgets ~= nil then
                for _, widget in pairs(widgets) do
                    if widget ~= nil and widget:IsValid() then
                        local textResetSucceeded =
                            pcall(function()
                                local currentText = widget.Text

                                if currentText == nil then
                                    return
                                end

                                if textLibrarySucceeded and
                                   textLibrary ~= nil and
                                   textLibrary:IsValid() then

                                    local metadataSucceeded,
                                          usesStringTable,
                                          tableId,
                                          stringKey =
                                        pcall(function()
                                            return textLibrary:
                                                StringTableIdAndKeyFromText(
                                                    currentText)
                                        end)

                                    if metadataSucceeded and
                                       usesStringTable and
                                       tableId ~= nil and
                                       stringKey ~= nil then

                                        local rebuiltText =
                                            textLibrary:TextFromStringTable(
                                                tableId,
                                                stringKey)

                                        if rebuiltText ~= nil then
                                            widget:SetText(rebuiltText)
                                            return
                                        end
                                    end
                                end

                                widget:SetText(currentText)
                            end)

                        local synchronizeSucceeded =
                            pcall(function()
                                widget:SynchronizeProperties()
                            end)

                        if synchronizeSucceeded or
                           textResetSucceeded then
                            refreshedCount =
                                refreshedCount + 1
                        end
                    end
                end
            end
        end

        return refreshedCount
    end

    local function reloadAssets(
        objectPathsText,
        requireEveryAsset)
        local objectPaths =
            splitPipeSeparated(objectPathsText)

        if #objectPaths == 0 then
            return false,
                "The refresh command did not include any asset paths."
        end

        local loadedCount = 0
        local failures = {}

        for _, objectPath in ipairs(objectPaths) do
            local callSucceeded,
                  asset,
                  assetWasFound,
                  assetWasLoaded =
                pcall(function()
                    return loadMountedAsset(objectPath)
                end)

            if callSucceeded and
               assetWasFound and
               assetWasLoaded and
               asset ~= nil and
               asset:IsValid() then

                loadedCount = loadedCount + 1

                local lowerObjectPath =
                    string.lower(objectPath)

                if isCharliePortraitPath(
                        lowerObjectPath) then

                    -- I save the path instead of keeping this UObject across
                    -- map loads. Unreal may retire the old package while the
                    -- Lua bridge is still alive, so a cached pointer can no
                    -- longer be trusted after a level change.
                    activeCharliePortraitPath = objectPath
                    portraitRefreshPassesRemaining = 20
                    lastPortraitRefreshSecond = 0

                    local refreshedCount =
                        refreshCharliePortraitWidgets()

                    print(
                        "[LimelightBridge] Charlie portrait loaded; refreshed " ..
                        tostring(refreshedCount) ..
                        " existing image widget(s).\n")
                end

                if isStringTablePath(lowerObjectPath) then
                    localizedTextRefreshPassesRemaining = 20
                    lastLocalizedTextRefreshSecond = 0

                    local refreshedCount =
                        refreshLocalizedTextWidgets()

                    print(
                        "[LimelightBridge] StringTables loaded; refreshed " ..
                        tostring(refreshedCount) ..
                        " text widget(s).\n")
                end
            else
                table.insert(
                    failures,
                    objectPath)
            end
        end

        if #failures > 0 and
           requireEveryAsset then

            return false,
                "The mounted character is still missing " ..
                tostring(#failures) ..
                " required asset package(s): " ..
                table.concat(failures, " | ")
        end

        if #failures > 0 then
            -- New textures and materials are often absent from the base Asset
            -- Registry. Unreal still loads them normally when the active player
            -- mesh asks for its cooked dependencies from the mounted container.
            print(
                "[LimelightBridge] Preloaded " ..
                tostring(loadedCount) ..
                " registered assets. " ..
                tostring(#failures) ..
                " dependency packages will load through the active player mesh.\n")

            return true,
                "Preloaded " .. tostring(loadedCount) ..
                " registered assets. " .. tostring(#failures) ..
                " dependency packages will load with the character."
        end

        return true,
            "Preloaded " .. tostring(loadedCount) ..
            " mounted assets, including interface and localization content."
    end

    local function scanCharlie()
        local playerControllers =
            FindAllOf("PlayerController")

        if playerControllers == nil then
            return false,
                "No player controllers were found. Enter a playable stage and try again."
        end

        local playerController = nil
        local pawn = nil

        -- Find the controller that currently owns a valid playable pawn.
        for _, candidateController in
            pairs(playerControllers) do

            if candidateController ~= nil and
               candidateController:IsValid() then

                local pawnReadSucceeded,
                      candidatePawn =
                    pcall(function()
                        return candidateController.Pawn
                    end)

                if pawnReadSucceeded and
                   candidatePawn ~= nil and
                   candidatePawn:IsValid() then

                    playerController =
                        candidateController

                    pawn =
                        candidatePawn

                    break
                end
            end
        end

        if playerController == nil or
           pawn == nil then

            return false,
                "No player controller currently owns a valid pawn."
        end

        local meshReadSucceeded, mesh =
            pcall(function()
                return pawn.Mesh
            end)

        if not meshReadSucceeded or
           mesh == nil or
           not mesh:IsValid() then

            return false,
                "The player pawn was found, but its Mesh component was unavailable."
        end

        local assetReadSucceeded, meshAsset =
            pcall(function()
                return mesh:GetSkeletalMeshAsset()
            end)

        if not assetReadSucceeded or
           meshAsset == nil or
           not meshAsset:IsValid() then

            return false,
                "The player mesh component was found, but its skeletal mesh asset was unavailable."
        end

        local message =
            "Pawn: " .. pawn:GetFullName() ..
            " | Component: " .. mesh:GetFullName() ..
            " | Asset: " .. meshAsset:GetFullName() ..
            " | active player mesh target confirmed"

        return true, message
    end
    local function findActiveCharlieMeshComponent()
        local controllers =
            FindAllOf("PlayerController")

        if controllers == nil then
            return nil,
                "No active Charlie pawn is available yet."
        end

        for _, controller in pairs(controllers) do
            if controller ~= nil and
               controller:IsValid() then

                local pawnReadSucceeded,
                      pawn =
                    pcall(function()
                        return controller.Pawn
                    end)

                if pawnReadSucceeded and
                   pawn ~= nil and
                   pawn:IsValid() then

                    local pawnName =
                        string.lower(
                            pawn:GetFullName())

                    if string.find(
                           pawnName,
                           "bp_pagodaplayercharacter_charlie",
                           1,
                           true) ~= nil then

                        local meshReadSucceeded,
                              meshComponent =
                            pcall(function()
                                return pawn.Mesh
                            end)

                        if meshReadSucceeded and
                           meshComponent ~= nil and
                           meshComponent:IsValid() then

                            return meshComponent,
                                pawn:GetFullName()
                        end
                    end
                end
            end
        end

        return nil,
            "No active Charlie pawn is available yet."
    end

    local function keepMaterialTexturesResident(
        material)

        if material == nil or
           not material:IsValid() then

            return false
        end

        local residencySucceeded =
            pcall(function()
                -- I ask the material interface itself to keep every inherited
                -- and overridden texture awake. Poking the mesh component did
                -- nothing useful besides giving the Lua log a small tantrum.
                material:SetForceMipLevelsToBeResident(
                    true,
                    true,
                    600.0,
                    0,
                    false)
            end)

        return residencySucceeded
    end

    local function inspectCharlieMaterials(
        meshComponent)

        local inspectionSucceeded,
              materialsReady,
              materialSummary =
            pcall(function()
                local materialCount =
                    meshComponent:GetNumMaterials()

                if materialCount == nil or
                   materialCount <= 0 then

                    return false,
                        "the replacement mesh has no material slots"
                end

                local validMaterialCount = 0
                local residentMaterialCount = 0
                local fallbackSlots = {}
                local activeMaterials = {}

                for materialIndex = 0,
                    materialCount - 1 do

                    local material =
                        meshComponent:GetMaterial(
                            materialIndex)

                    if material ~= nil and
                       material:IsValid() then

                        local fullName =
                            material:GetFullName()

                        local lowerName =
                            string.lower(fullName)

                        table.insert(
                            activeMaterials,
                            fullName)

                        local isFallbackMaterial =
                            string.find(
                                lowerName,
                                "worldgridmaterial",
                                1,
                                true) ~= nil or
                            string.find(
                                lowerName,
                                "defaultmaterial",
                                1,
                                true) ~= nil or
                            string.find(
                                lowerName,
                                "defaultsurfacematerial",
                                1,
                                true) ~= nil or
                            string.find(
                                lowerName,
                                "/engine/",
                                1,
                                true) ~= nil

                        if isFallbackMaterial then
                            table.insert(
                                fallbackSlots,
                                tostring(materialIndex))
                        else
                            validMaterialCount =
                                validMaterialCount + 1

                            if keepMaterialTexturesResident(
                                   material) then

                                residentMaterialCount =
                                    residentMaterialCount + 1
                            end
                        end
                    else
                        table.insert(
                            activeMaterials,
                            "<empty slot " ..
                            tostring(materialIndex) ..
                            ">")
                    end
                end

                if #fallbackSlots > 0 then
                    return false,
                        "Unreal assigned a fallback material to slot(s) " ..
                        table.concat(fallbackSlots, ", ")
                end

                if validMaterialCount == 0 then
                    return false,
                        "the replacement mesh has no valid mod materials"
                end

                return true,
                    table.concat(
                        activeMaterials,
                        " | ") ..
                    " (texture residency requested for " ..
                    tostring(residentMaterialCount) ..
                    " of " ..
                    tostring(validMaterialCount) ..
                    " materials)"
            end)

        if not inspectionSucceeded then
            return false,
                "material inspection failed: " ..
                tostring(materialsReady)
        end

        return materialsReady,
            materialSummary
    end

    local function restorePlayerMeshVisibility()
        local meshComponent,
              pawnName =
            findActiveCharlieMeshComponent()

        if meshComponent == nil then
            return false,
                pawnName
        end

        local visibilityRestored =
            pcall(function()
                meshComponent:SetVisibility(
                    true,
                    true)
            end)

        local hiddenStateRestored =
            pcall(function()
                meshComponent:SetHiddenInGame(
                    false,
                    true)
            end)

        if not visibilityRestored and
           not hiddenStateRestored then

            return false,
                "The refreshed player mesh could not be made visible again."
        end

        return true,
            "The refreshed player mesh is visible on " ..
            pawnName .. "."
    end

    local function reapplyCharlie()
        local meshComponent,
              pawnName =
            findActiveCharlieMeshComponent()

        if meshComponent == nil then
            return false,
                pawnName
        end

        local currentAssetReadSucceeded,
              currentMeshAsset =
            pcall(function()
                return meshComponent:GetSkeletalMeshAsset()
            end)

        if not currentAssetReadSucceeded or
           currentMeshAsset == nil or
           not currentMeshAsset:IsValid() then

            return false,
                "The active player mesh asset was unavailable."
        end

        -- I only replace SK_Charlie for regular mods. Character Slot mods take
        -- the scenic route through Dead as Disco's cosmetic system instead.
        local targetMeshPath =
            defaultPlayerMeshPath

        local expectedMeshName =
            string.lower(
                string.match(
                    targetMeshPath,
                    "%.([^%.]+)$") or "")

        if expectedMeshName == "" then
            return false,
                "The selected player mesh object path was invalid."
        end

        local loadCallSucceeded,
              meshAsset,
              assetWasFound,
              assetWasLoaded =
            pcall(function()
                return loadMountedAsset(
                    targetMeshPath)
            end)

        if not loadCallSucceeded then
            return false,
                "The replacement player mesh could not be loaded from " ..
                targetMeshPath .. ": " ..
                tostring(meshAsset)
        end

        if not assetWasFound or
           not assetWasLoaded or
           meshAsset == nil or
           not meshAsset:IsValid() then

            return false,
                "The newly mounted container did not provide a loadable player mesh at " ..
                targetMeshPath .. "."
        end

        local loadedAssetName =
            string.lower(
                meshAsset:GetFName():ToString())

        if loadedAssetName ~= expectedMeshName then
            return false,
                "The freshly loaded player mesh did not match " ..
                expectedMeshName .. "."
        end

        local previousMesh =
            currentMeshAsset

        local previousMaterials = {}

        pcall(function()
            local previousMaterialCount =
                meshComponent:GetNumMaterials()

            for materialIndex = 0, previousMaterialCount - 1 do
                local previousMaterial =
                    meshComponent:GetMaterial(materialIndex)

                if previousMaterial ~= nil and
                   previousMaterial:IsValid() then

                    table.insert(
                        previousMaterials,
                        {
                            index = materialIndex,
                            material = previousMaterial
                        })
                end
            end
        end)

        local function restorePreviousRender()
            if previousMesh == nil or
               not previousMesh:IsValid() then

                return
            end

            pcall(function()
                meshComponent:SetSkeletalMeshAsset(
                    previousMesh)
            end)

            pcall(function()
                local overrideMaterials =
                    meshComponent.OverrideMaterials

                if overrideMaterials ~= nil then
                    overrideMaterials:Empty()
                end
            end)

            for _,
                previousMaterialEntry in ipairs(previousMaterials) do

                pcall(function()
                    meshComponent:SetMaterial(
                        previousMaterialEntry.index,
                        previousMaterialEntry.material)
                end)
            end

            pcall(function()
                meshComponent:MarkRenderStateDirty()
            end)

            pcall(function()
                meshComponent:RecreateRenderState()
            end)

            restorePlayerMeshVisibility()
        end

        local clearedOverrideCount = 0

        local overridesCleared,
              overrideClearError =
            pcall(function()
                local overrideMaterials =
                    meshComponent.OverrideMaterials

                if overrideMaterials ~= nil then
                    clearedOverrideCount =
                        overrideMaterials:GetArrayNum()

                    -- Both replacement formats must lose dynamic overrides
                    -- left by the previous character before the new body mesh
                    -- supplies its own material slots.
                    overrideMaterials:Empty()
                end
            end)

        if not overridesCleared then
            return false,
                "The previous character material overrides could not be cleared: " ..
                tostring(overrideClearError)
        end

        local setSucceeded = false
        local setError = nil

        setSucceeded,
        setError =
            pcall(function()
                meshComponent:SetSkeletalMeshAsset(
                    meshAsset)
            end)

        pcall(function()
            local materialCount =
                meshComponent:GetNumMaterials()

            for materialIndex = 0, materialCount - 1 do
                local currentMaterial =
                    meshComponent:GetMaterial(materialIndex)

                if currentMaterial ~= nil and
                   currentMaterial:IsValid() then

                    pcall(function()
                        meshComponent:SetMaterial(
                            materialIndex,
                            currentMaterial)
                    end)
                end
            end
        end)

        -- I bind first, then ask Unreal to stream the complete replacement
        -- character before I rebuild its render state.
        pcall(function()
            if meshComponent.PrestreamTextures ~= nil then
                meshComponent:PrestreamTextures(
                    600.0,
                    true,
                    0)
            end
        end)

        pcall(function()
            if meshComponent.MarkRenderStateDirty ~= nil then
                meshComponent:MarkRenderStateDirty()
            end
        end)

        pcall(function()
            if meshComponent.RecreateRenderState ~= nil then
                meshComponent:RecreateRenderState()
            end
        end)

        if not setSucceeded then
            restorePreviousRender()

            return false,
                "The active Charlie pawn could not accept the replacement mesh: " ..
                tostring(setError)
        end

        local materialsReady,
              materialSummary =
            inspectCharlieMaterials(
                meshComponent)

        if not materialsReady then
            -- I refuse to call a black model a successful switch. The old mesh
            -- and its exact materials get their seats back before I retry.
            restorePreviousRender()

            return false,
                "The replacement materials are not ready: " ..
                materialSummary
        end

        local visibilityReady,
              visibilityMessage =
            restorePlayerMeshVisibility()

        if not visibilityReady then
            restorePreviousRender()

            return false,
                visibilityMessage
        end

        print(
            "[LimelightBridge] Cleared " ..
            tostring(clearedOverrideCount) ..
            " material overrides on the active Charlie pawn. Active materials: " ..
            materialSummary ..
            "\n")

        activeCustomMeshApplied = true

        return true,
            "The mounted player mesh and verified materials were applied to the active gameplay mesh on " ..
            pawnName .. "."
    end

    local function findCharacterLoaderActor()
        local actors =
            FindAllOf("ModActor_C")

        if actors == nil then
            return nil,
                "Character Loader's ModActor_C is missing. Launch through Limelight once, then restart Dead as Disco."
        end

        for _,
            targetActorId in ipairs(characterLoaderActorIds) do

            for _,
                actor in ipairs(actors) do

                if actor ~= nil and
                   actor:IsValid() then

                    local idReadSucceeded,
                          actorId =
                        pcall(function()
                            return actor.ActorID
                        end)

                    if idReadSucceeded and
                       actorId == targetActorId then

                        return actor,
                            "Character Loader is ready."
                    end
                end
            end
        end

        return nil,
            "Character Loader's actor is not ready yet. Expected Limelight ID " ..
            tostring(characterLoaderActorIds[1]) ..
            " or legacy ID " ..
            tostring(characterLoaderActorIds[2]) .. "."
    end

    local function findCosmeticSubsystem()
        local subsystems =
            FindAllOf(
                "PagodaCosmeticLocalPlayerSubsystem")

        if subsystems == nil then
            return nil
        end

        for _,
            subsystem in ipairs(subsystems) do

            if subsystem ~= nil and
               subsystem:IsValid() then

                local fullNameReadSucceeded,
                      fullName =
                    pcall(function()
                        return subsystem:GetFullName()
                    end)

                if not fullNameReadSucceeded or
                   string.find(
                       fullName,
                       "Default__",
                       1,
                       true) == nil then

                    return subsystem
                end
            end
        end

        return nil
    end

    local function registerCharacterSlotDefinition(
        definitionPath)

        local loaderActor,
              loaderMessage =
            findCharacterLoaderActor()

        if loaderActor == nil then
            return false,
                loaderMessage
        end

        local registrationState =
            registeredCharacterSlotDefinitions[
                definitionPath]

        if registrationState == nil then
            local definitionAdded,
                  addError =
                pcall(function()
                    loaderActor:AddToModDefinitions(
                        definitionPath)
                end)

            if not definitionAdded then
                return false,
                    "Character Loader rejected the PPCD: " ..
                    tostring(addError)
            end

            -- I remember the halfway point too. If AddToList trips over its
            -- shoelaces, a retry must not stuff the PPCD into the actor twice.
            registeredCharacterSlotDefinitions[
                definitionPath] =
                "definition_added"

            registrationState =
                "definition_added"
        end

        if registrationState ~= "ready" then
            local listUpdated,
                  listError =
                pcall(function()
                    loaderActor:AddToList()
                end)

            if not listUpdated then
                return false,
                    "Character Loader could not refresh its catalogue: " ..
                    tostring(listError)
            end

            registeredCharacterSlotDefinitions[
                definitionPath] =
                "ready"
        end

        return true,
            "Character Loader registered " ..
            definitionPath .. "."
    end

    local function readCharacterSlotCatalogue()
        local catalogueFile =
            io.open(characterSlotCataloguePath, "r")

        if catalogueFile == nil then
            return {}
        end

        local definitions = {}
        local seenDefinitions = {}

        for line in catalogueFile:lines() do
            local definitionPath =
                string.match(line, "^%s*(.-)%s*$")

            if definitionPath ~= nil and
               string.sub(definitionPath, 1, 48) ==
                   "/Game/Pagoda/Characters/Player/ModdedCharacters/" and
               string.find(
                   definitionPath,
                   ".PPCD_",
                   1,
                   true) ~= nil and
               not seenDefinitions[definitionPath] then

                seenDefinitions[definitionPath] = true

                table.insert(
                    definitions,
                    definitionPath)
            end
        end

        catalogueFile:close()
        return definitions
    end

    local function externalCharacterSlotRegistrationIsActive()
        local modeFile =
            io.open(characterSlotLoaderModePath, "r")

        if modeFile == nil then
            return false
        end

        local mode =
            string.match(
                modeFile:read("*a") or "",
                "^%s*(.-)%s*$")

        modeFile:close()
        return mode == "official" or
               mode == "native"
    end

    local initialiseCharacterSlotCatalogue

    initialiseCharacterSlotCatalogue = function()
        if characterSlotCatalogueInitialised then
            return
        end

        local definitions =
            readCharacterSlotCatalogue()

        if #definitions == 0 then
            characterSlotCatalogueInitialised = true
            return
        end

        if externalCharacterSlotRegistrationIsActive() then
            -- I let the selected external owner fill the Locker, then remember
            -- its homework so a live switch does not add the same slot twice.
            for _, definitionPath in ipairs(definitions) do
                registeredCharacterSlotDefinitions[
                    definitionPath] = "ready"
            end

            characterSlotCatalogueInitialised = true

            print(
                "[LimelightBridge] External Character Slot registration detected; Live Loader will not duplicate it.\n")

            return
        end

        local loaderActor,
              loaderMessage =
            findCharacterLoaderActor()

        if loaderActor == nil then
            ExecuteWithDelay(
                1000,
                initialiseCharacterSlotCatalogue)

            return
        end

        local definitionsAdded = 0

        for _, definitionPath in ipairs(definitions) do
            local registrationState =
                registeredCharacterSlotDefinitions[
                    definitionPath]

            if registrationState == nil then
                local definitionAdded,
                      addError =
                    pcall(function()
                        loaderActor:AddToModDefinitions(
                            definitionPath)
                    end)

                if definitionAdded then
                    registeredCharacterSlotDefinitions[
                        definitionPath] =
                            "definition_added"

                    definitionsAdded =
                        definitionsAdded + 1
                else
                    print(
                        "[LimelightBridge] Character Slot catalogue is still waiting for " ..
                        definitionPath .. ": " ..
                        tostring(addError) .. "\n")
                end
            elseif registrationState ==
                   "definition_added" then

                definitionsAdded =
                    definitionsAdded + 1
            end
        end

        if definitionsAdded > 0 then
            local listUpdated,
                  listError =
                pcall(function()
                    loaderActor:AddToList()
                end)

            if not listUpdated then
                print(
                    "[LimelightBridge] Character Slot catalogue refresh is still warming up: " ..
                    tostring(listError) .. "\n")

                ExecuteWithDelay(
                    1000,
                    initialiseCharacterSlotCatalogue)

                return
            end
        end

        local allDefinitionsReady = true

        for _, definitionPath in ipairs(definitions) do
            if registeredCharacterSlotDefinitions[
                    definitionPath] ==
               "definition_added" then

                registeredCharacterSlotDefinitions[
                    definitionPath] = "ready"
            elseif registeredCharacterSlotDefinitions[
                       definitionPath] ~=
                   "ready" then

                allDefinitionsReady = false
            end
        end

        if not allDefinitionsReady then
            ExecuteWithDelay(
                1000,
                initialiseCharacterSlotCatalogue)

            return
        end

        characterSlotCatalogueInitialised = true

        print(
            "[LimelightBridge] Added " ..
            tostring(#definitions) ..
            " Character Slot model(s) to the in-game Locker.\n")
    end

    local function getEquippedBodyDefinition(
        subsystem)

        local wrapperReadSucceeded,
              wrapper =
            pcall(function()
                -- I ask for BodyType explicitly. Some modded PPCDs forget to
                -- introduce their slot properly, bless their little hearts.
                return subsystem:GetEquippedCosmeticItem(
                    1)
            end)

        if not wrapperReadSucceeded or
           wrapper == nil or
           not wrapper:IsValid() then

            return nil
        end

        local definitionReadSucceeded,
              equippedDefinition =
            pcall(function()
                return wrapper.CosmeticDef
            end)

        if definitionReadSucceeded and
           equippedDefinition ~= nil and
           equippedDefinition:IsValid() then

            return equippedDefinition
        end

        return nil
    end

    local function getDefaultBodyDefinition()
        local definition,
              definitionWasFound,
              definitionWasLoaded =
            loadMountedAsset(
                defaultBodyCosmeticPath)

        if definitionWasFound and
           definitionWasLoaded and
           definition ~= nil and
           definition:IsValid() then

            return definition
        end

        return nil
    end

    local function getRollbackBodyDefinition(
        subsystem)

        local equippedDefinition =
            getEquippedBodyDefinition(
                subsystem)

        if equippedDefinition ~= nil and
           equippedDefinition:IsValid() then

            return equippedDefinition
        end

        if activeCharacterSlotDefinitionPath ~= nil then
            local activeDefinition,
                  activeDefinitionWasFound,
                  activeDefinitionWasLoaded =
                loadMountedAsset(
                    activeCharacterSlotDefinitionPath)

            if activeDefinitionWasFound and
               activeDefinitionWasLoaded and
               activeDefinition ~= nil and
               activeDefinition:IsValid() then

                return activeDefinition
            end
        end

        -- I keep vanilla Charlie's body card behind the bar. The cosmetic
        -- subsystem occasionally claims its equipped wrapper has gone home.
        return getDefaultBodyDefinition()
    end

    local function verifyCharacterSlotMesh(
        expectedMeshPath)

        local meshComponent,
              pawnName =
            findActiveCharlieMeshComponent()

        if meshComponent == nil then
            return false,
                pawnName
        end

        local expectedMeshName =
            string.lower(
                string.match(
                    expectedMeshPath,
                    "%.([^%.]+)$") or "")

        local meshReadSucceeded,
              currentMesh =
            pcall(function()
                return meshComponent:GetSkeletalMeshAsset()
            end)

        if not meshReadSucceeded or
           currentMesh == nil or
           not currentMesh:IsValid() then

            return false,
                "Character Loader has not attached a valid body mesh yet."
        end

        local currentMeshName =
            string.lower(
                currentMesh:GetFName():ToString())

        if currentMeshName ~= expectedMeshName then
            return false,
                "Character Loader is still applying " ..
                expectedMeshName .. "."
        end

        local materialsReady,
              materialSummary =
            inspectCharlieMaterials(
                meshComponent)

        if not materialsReady then
            return false,
                "The CSM mesh arrived, but its materials are still backstage: " ..
                materialSummary
        end

        local visibilityReady,
              visibilityMessage =
            restorePlayerMeshVisibility()

        if not visibilityReady then
            return false,
                visibilityMessage
        end

        return true,
            "Character Loader equipped the CSM through Dead as Disco's cosmetic system on " ..
            pawnName .. ". " ..
            materialSummary
    end

    local function rollbackCharacterSlot(
        subsystem,
        previousDefinition)

        if subsystem == nil or
           previousDefinition == nil or
           not previousDefinition:IsValid() then

            return false
        end

        local rollbackCallSucceeded,
              rollbackAccepted =
            pcall(function()
                return subsystem:TryEquipCosmetic(
                    previousDefinition)
            end)

        return rollbackCallSucceeded and
               rollbackAccepted == true
    end

    local function scheduleCharacterSlotVerification(
        requestId,
        subsystem,
        definitionPath,
        expectedMeshPath,
        characterName,
        previousDefinition,
        previousActiveDefinitionPath,
        previousActiveMeshPath,
        previousActiveName)

        local verifyAttempt

        verifyAttempt = function(attempt)
            ExecuteInGameThreadWithDelay(
                attempt == 1 and 80 or 140,
                function()
                    if worldTransitioning or
                       worldSettling then

                        rollbackCharacterSlot(
                            subsystem,
                            previousDefinition)

                        writeResponse(
                            requestId,
                            false,
                            "A level started loading while Character Loader was applying the CSM. The previous cosmetic was restored.")

                        return
                    end

                    local verificationCallSucceeded,
                          verificationSucceeded,
                          verificationMessage =
                        pcall(function()
                            return verifyCharacterSlotMesh(
                                expectedMeshPath)
                        end)

                    if verificationCallSucceeded and
                       verificationSucceeded then

                        if activeCharacterSlotDefinitionPath == nil and
                           baseCharacterDefinition == nil and
                           previousDefinition ~= nil and
                           previousDefinition:IsValid() then

                            baseCharacterDefinition =
                                previousDefinition
                        end

                        activeCharacterSlotDefinitionPath =
                            definitionPath

                        activeCharacterSlotMeshPath =
                            expectedMeshPath

                        activeCharacterSlotName =
                            characterName

                        automaticCharlieRefreshEnabled = true
                        activeCustomMeshApplied = true

                        writeResponse(
                            requestId,
                            true,
                            verificationMessage)

                        return
                    end

                    if attempt < 7 then
                        verifyAttempt(
                            attempt + 1)

                        return
                    end

                    local rollbackSucceeded =
                        rollbackCharacterSlot(
                            subsystem,
                            previousDefinition)

                    activeCharacterSlotDefinitionPath =
                        previousActiveDefinitionPath

                    activeCharacterSlotMeshPath =
                        previousActiveMeshPath

                    activeCharacterSlotName =
                        previousActiveName

                    writeResponse(
                        requestId,
                        false,
                        tostring(verificationMessage) ..
                        (rollbackSucceeded
                            and " The previous cosmetic was restored."
                            or " The previous cosmetic could not be restored automatically."))
                end)
        end

        verifyAttempt(1)
    end

    local function beginCharacterSlotActivation(
        requestId,
        definitionPath,
        expectedMeshPath,
        characterName)

        local registrationSucceeded,
              registrationMessage =
            registerCharacterSlotDefinition(
                definitionPath)

        if not registrationSucceeded then
            writeResponse(
                requestId,
                false,
                registrationMessage)

            return
        end

        local definition,
              definitionWasFound,
              definitionWasLoaded =
            loadMountedAsset(
                definitionPath)

        if not definitionWasFound or
           not definitionWasLoaded or
           definition == nil or
           not definition:IsValid() then

            writeResponse(
                requestId,
                false,
                "Character Loader registered the slot, but its PPCD object is still unavailable: " ..
                definitionPath)

            return
        end

        local definitionName =
            definition:GetFullName()

        if string.find(
                definitionName,
                "PagodaPlayerCosmeticDefinition",
                1,
                true) == nil then

            writeResponse(
                requestId,
                false,
                "The CSM entry point was not a PagodaPlayerCosmeticDefinition: " ..
                definitionName)

            return
        end

        local subsystem =
            findCosmeticSubsystem()

        if subsystem == nil then
            writeResponse(
                requestId,
                false,
                "No local cosmetic subsystem is ready yet. Limelight will retry when the player appears.")

            return
        end

        local previousDefinition =
            getRollbackBodyDefinition(
                subsystem)

        local previousActiveDefinitionPath =
            activeCharacterSlotDefinitionPath

        local previousActiveMeshPath =
            activeCharacterSlotMeshPath

        local previousActiveName =
            activeCharacterSlotName

        local equipCallSucceeded,
              equipAccepted =
            pcall(function()
                return subsystem:TryEquipCosmetic(
                    definition)
            end)

        if not equipCallSucceeded or
           equipAccepted ~= true then

            writeResponse(
                requestId,
                false,
                equipCallSucceeded
                    and "Dead as Disco declined the registered CSM cosmetic."
                    or "Dead as Disco could not equip the registered CSM cosmetic: " ..
                       tostring(equipAccepted))

            return
        end

        scheduleCharacterSlotVerification(
            requestId,
            subsystem,
            definitionPath,
            expectedMeshPath,
            characterName,
            previousDefinition,
            previousActiveDefinitionPath,
            previousActiveMeshPath,
            previousActiveName)
    end

    local function restoreBaseCharacterCosmetic()
        if activeCharacterSlotDefinitionPath == nil then
            return true,
                "No Character Slot cosmetic needed restoring."
        end

        local subsystem =
            findCosmeticSubsystem()

        if subsystem == nil then
            return false,
                "The local cosmetic subsystem is not ready to restore the previous body type."
        end

        if baseCharacterDefinition == nil or
           not baseCharacterDefinition:IsValid() then

            -- I would rather fetch vanilla Charlie's real body definition than
            -- strand everyone in Oberon's dressing room.
            baseCharacterDefinition =
                getDefaultBodyDefinition()
        end

        if baseCharacterDefinition == nil or
           not baseCharacterDefinition:IsValid() then

            return false,
                "Dead as Disco's vanilla Charlie body definition is not loadable yet. Limelight will retry when the world is ready."
        end

        if not rollbackCharacterSlot(
                   subsystem,
                   baseCharacterDefinition) then

            return false,
                "Dead as Disco could not restore the pre-CSM cosmetic."
        end

        activeCharacterSlotDefinitionPath = nil
        activeCharacterSlotMeshPath = nil
        activeCharacterSlotName = nil
        baseCharacterDefinition = nil

        return true,
            "The pre-CSM cosmetic was restored."
    end

    local function reapplyActivePlayerCharacter()
        if activeCharacterSlotDefinitionPath == nil then
            return reapplyCharlie()
        end

        local definition,
              definitionWasFound,
              definitionWasLoaded =
            loadMountedAsset(
                activeCharacterSlotDefinitionPath)

        local subsystem =
            findCosmeticSubsystem()

        if not definitionWasFound or
           not definitionWasLoaded or
           definition == nil or
           not definition:IsValid() or
           subsystem == nil then

            return false,
                "The active CSM cosmetic is waiting for the new world."
        end

        local equipCallSucceeded,
              equipAccepted =
            pcall(function()
                return subsystem:TryEquipCosmetic(
                    definition)
            end)

        if not equipCallSucceeded or
           equipAccepted ~= true then

            return false,
                "Dead as Disco did not re-equip the active CSM after loading."
        end

        return verifyCharacterSlotMesh(
            activeCharacterSlotMeshPath)
    end

    local function scanMountFunctions()
        local candidates = {}
        local candidateSet = {}
        local scannedObjectCount = 0

        -- Search reflected Unreal functions without printing every object.
        ForEachUObject(function(object)
            scannedObjectCount =
                scannedObjectCount + 1

            local nameReadSucceeded,
                  fullName =
                pcall(function()
                    return object:GetFullName()
                end)

            if nameReadSucceeded and
               fullName ~= nil then

                local lowerName =
                    string.lower(fullName)

                local isFunction =
                    string.sub(
                        lowerName,
                        1,
                        9) == "function "

                local mentionsMount =
                    string.find(
                        lowerName,
                        "mount",
                        1,
                        true) ~= nil

                local mentionsContainer =
                    string.find(
                        lowerName,
                        "pak",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "iostore",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "container",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "chunk",
                        1,
                        true) ~= nil

                local mentionsPakAction =
                    string.find(
                        lowerName,
                        "loadpak",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "openpak",
                        1,
                        true) ~= nil

                if isFunction and
                   ((mentionsMount and mentionsContainer) or
                    mentionsPakAction) and
                   candidateSet[fullName] == nil then

                    candidateSet[fullName] = true

                    table.insert(
                        candidates,
                        fullName)
                end
            end
        end)

        table.sort(candidates)

        local reportFile =
            io.open(
                mountFunctionsPath,
                "w")

        if reportFile == nil then
            return false,
                "Limelight could not create the mount-function report."
        end

        reportFile:write(
            "Objects scanned: " ..
            tostring(scannedObjectCount) ..
            "\n")

        reportFile:write(
            "Candidate functions: " ..
            tostring(#candidates) ..
            "\n\n")

        for _, candidate in
            ipairs(candidates) do

            reportFile:write(
                candidate .. "\n")
        end

        reportFile:close()

        if #candidates == 0 then
            return false,
                "No reflected mounting functions were found. The report was saved to " ..
                mountFunctionsPath
        end

        return true,
            tostring(#candidates) ..
            " possible mounting functions were found. The report was saved to " ..
            mountFunctionsPath
    end

    local function processCommand()
        local command =
            readValues(commandPath)

        if command == nil then
            return
        end

        local requestId =
            command.requestId

        if requestId == nil or
           requestId == "" then

            os.remove(commandPath)
            return
        end

        if requestId == lastRequestId then
            os.remove(commandPath)
            return
        end

        lastRequestId = requestId

        local action =
            string.lower(
                command.action or "")

        if action == "ping" then
            writeResponse(
                requestId,
                true,
                "Limelight bridge is online")
        elseif action == "scan_mount_functions" then
            ExecuteInGameThread(function()
                local callSucceeded,
                      scanSucceeded,
                      scanMessage =
                    pcall(scanMountFunctions)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Mount-function scan failed: " ..
                        tostring(scanSucceeded))
                else
                    writeResponse(
                        requestId,
                        scanSucceeded,
                        scanMessage)
                end
            end)
        elseif action == "reapply_charlie" then
            ExecuteInGameThread(function()
                if worldTransitioning or worldSettling then
                    writeResponse(
                        requestId,
                        false,
                        "A level is still loading. Limelight will retry once the new world is ready.")

                    return
                end

                local cosmeticRestored,
                      cosmeticRestoreMessage =
                    restoreBaseCharacterCosmetic()

                if not cosmeticRestored then
                    writeResponse(
                        requestId,
                        false,
                        cosmeticRestoreMessage)

                    return
                end

                local callSucceeded,
                      reapplySucceeded,
                      reapplyMessage =
                    pcall(reapplyCharlie)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Model reapply failed: " ..
                        tostring(reapplySucceeded))
                else
                    if reapplySucceeded then
                        automaticCharlieRefreshEnabled = true
                    end

                    writeResponse(
                        requestId,
                        reapplySucceeded,
                        reapplyMessage)
                end
            end)
        elseif action == "activate_character_slot" then
            ExecuteInGameThread(function()
                if worldTransitioning or worldSettling then
                    writeResponse(
                        requestId,
                        false,
                        "A level is still loading. Limelight will retry once the new world is ready.")

                    return
                end

                local definitionPath =
                    command.definitionPath

                local expectedMeshPath =
                    command.meshPath

                local characterName =
                    command.characterName

                if definitionPath == nil or
                   expectedMeshPath == nil or
                   characterName == nil or
                   string.sub(definitionPath, 1, 6) ~= "/Game/" or
                   string.sub(expectedMeshPath, 1, 6) ~= "/Game/" or
                   string.find(definitionPath, ".", 1, true) == nil or
                   string.find(expectedMeshPath, ".", 1, true) == nil then

                    writeResponse(
                        requestId,
                        false,
                        "The Character Slot PPCD or mesh path was invalid.")

                    return
                end

                local activationCallSucceeded,
                      activationError =
                    pcall(function()
                        beginCharacterSlotActivation(
                            requestId,
                            definitionPath,
                            expectedMeshPath,
                            characterName)
                    end)

                if not activationCallSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Character Slot activation failed: " ..
                        tostring(activationError))
                end
            end)
        elseif action == "remember_active_assets" then
            rememberActiveAssets(
                command.objectPaths)

            writeResponse(
                requestId,
                true,
                "Limelight remembered the complete active asset list.")
        elseif action == "commit_active_assets" then
            commitActiveAssets()

            writeResponse(
                requestId,
                true,
                "Limelight committed the verified active asset list.")
        elseif action == "rollback_active_assets" then
            local rollbackApplied =
                rollbackActiveAssets()

            writeResponse(
                requestId,
                true,
                rollbackApplied and
                    "Limelight restored the previous active asset list." or
                    "No pending active asset list needed rollback.")
        elseif action == "reload_assets" then
            ExecuteInGameThread(function()
                if worldTransitioning or worldSettling then
                    writeResponse(
                        requestId,
                        false,
                        "A level is still loading. Mounted assets were left untouched until it finishes.")

                    return
                end

                local callSucceeded,
                      reloadSucceeded,
                      reloadMessage =
                    pcall(function()
                        return reloadAssets(
                            command.objectPaths,
                            command.requireEveryAsset == "true")
                    end)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Mounted asset reload failed: " ..
                        tostring(reloadSucceeded))
                else
                    if reloadSucceeded then
                        automaticCharlieRefreshEnabled = true

                        -- The first reload is a useful fallback for older
                        -- callers. Later dependency passes must not replace
                        -- the complete active manifest with a smaller list.
                        if activeObjectPathsText == nil or
                           activeObjectPathsText == "" then

                            applyActiveAssets(
                                command.objectPaths)
                        end
                    end

                    writeResponse(
                        requestId,
                        reloadSucceeded,
                        reloadMessage)
                end
            end)
        else
            writeResponse(
                requestId,
                false,
                "Unknown bridge command: " .. action)
        end

        os.remove(commandPath)
    end

    local function scheduleAutomaticCharlieRefresh(
        delayMilliseconds,
        expectedGeneration)

        if not automaticCharlieRefreshEnabled then
            return
        end

        ExecuteInGameThreadWithDelay(
            delayMilliseconds,
            function()
                if worldTransitioning or
                   worldSettling or
                   expectedGeneration ~= transitionGeneration or
                   not automaticCharlieRefreshEnabled then

                    return
                end

                local assetReloadCallSucceeded = true
                local assetsReloaded = true
                local assetReloadMessage =
                    "No active asset paths were saved."

                if activeObjectPathsText ~= nil and
                   activeObjectPathsText ~= "" then

                    -- The old world may have released interface and localization
                    -- objects. I load the active packages again before touching
                    -- any of the newly created widgets.
                    assetReloadCallSucceeded,
                    assetsReloaded,
                    assetReloadMessage =
                        pcall(function()
                            return reloadAssets(
                                activeObjectPathsText,
                                false)
                        end)
                end

                local reapplyCallSucceeded,
                      refreshSucceeded,
                      refreshMessage =
                    pcall(reapplyActivePlayerCharacter)

                if assetReloadCallSucceeded and
                   assetsReloaded and
                   reapplyCallSucceeded and
                   refreshSucceeded then

                    print(
                        "[LimelightBridge] Automatic post-load refresh: " ..
                        tostring(assetReloadMessage) ..
                        " " ..
                        tostring(refreshMessage) ..
                        "\n")
                else
                    print(
                        "[LimelightBridge] Automatic post-load refresh is still waiting. Assets: " ..
                        tostring(assetReloadMessage) ..
                        " Character: " ..
                        tostring(refreshMessage) ..
                        "\n")
                end
            end)
    end

    RegisterLoadMapPreHook(function()
        worldTransitioning = true
        worldSettling = true
        transitionGeneration =
            transitionGeneration + 1

        print(
            "[LimelightBridge] Level transition started; model refresh paused.\n")
    end)

    RegisterLoadMapPostHook(function()
        worldTransitioning = false
        worldSettling = true

        local completedGeneration =
            transitionGeneration

        if activeObjectPathsText ~= nil and
           activeObjectPathsText ~= "" then

            -- A new map creates fresh widgets, so I re-arm every interface
            -- and localization refresh from the complete active manifest.
            rememberActiveAssets(
                activeObjectPathsText)
        elseif activeCharliePortraitPath ~= nil and
               activeCharliePortraitPath ~= "" then

            portraitRefreshPassesRemaining = 20
            lastPortraitRefreshSecond = 0
        end

        -- LoadMap finishes before every streamed actor and widget is ready. I
        -- keep every refresh locked until the same quiet period used by the
        -- native bridge has passed without another map starting.
        ExecuteInGameThreadWithDelay(
            6000,
            function()
                if worldTransitioning or
                   completedGeneration ~= transitionGeneration then

                    return
                end

                worldSettling = false

                scheduleAutomaticCharlieRefresh(
                    0,
                    completedGeneration)

                print(
                    "[LimelightBridge] New level settled; model refresh unlocked.\n")
            end)

        print(
            "[LimelightBridge] Level transition finished; model refresh scheduled.\n")
    end)

    RegisterBeginPlayPostHook(function(contextParameter)
        if worldTransitioning or
           worldSettling or
           not automaticCharlieRefreshEnabled then

            return
        end

        local context = contextParameter:get()

        if context == nil or
           not context:IsValid() then

            return
        end

        local contextName =
            string.lower(
                context:GetFullName())

        if string.find(
                contextName,
                "bp_pagodaplayercharacter_charlie",
                1,
                true) ~= nil then

            -- This catches characters created after LoadMap's normal delay,
            -- including streamed stages and late player respawns.
            scheduleAutomaticCharlieRefresh(
                350,
                transitionGeneration)
        end
    end)

    -- Produce a heartbeat immediately so the dashboard can recognise us.
    writeHeartbeat()
    lastHeartbeatSecond = os.time()

    LoopAsync(250, function()
        local currentSecond =
            os.time()

        if currentSecond ~=
           lastHeartbeatSecond then

            writeHeartbeat()
            lastHeartbeatSecond =
                currentSecond
        end

        local shouldRefreshPortrait =
            activeCharliePortraitPath ~= nil and
            activeCharliePortraitPath ~= "" and
            not worldTransitioning and
            not worldSettling and
            ((portraitRefreshPassesRemaining > 0 and
              currentSecond ~= lastPortraitRefreshSecond) or
             (portraitRefreshPassesRemaining <= 0 and
              currentSecond - lastPortraitRefreshSecond >= 3))

        if shouldRefreshPortrait then

            -- Portrait widgets can appear long after the texture loads. I
            -- keep this lightweight pass alive for newly opened screens.
            local refreshedCount =
                refreshCharliePortraitWidgets()

            if portraitRefreshPassesRemaining > 0 then
                portraitRefreshPassesRemaining =
                    portraitRefreshPassesRemaining - 1
            end

            lastPortraitRefreshSecond =
                currentSecond

            if refreshedCount > 0 then
                print(
                    "[LimelightBridge] Refreshed " ..
                    tostring(refreshedCount) ..
                    " Charlie portrait widget(s).\n")
            end
        end

        local shouldRefreshLocalizedText =
            #activeStringTablePaths > 0 and
            not worldTransitioning and
            not worldSettling and
            ((localizedTextRefreshPassesRemaining > 0 and
              currentSecond ~= lastLocalizedTextRefreshSecond) or
             (localizedTextRefreshPassesRemaining <= 0 and
              currentSecond - lastLocalizedTextRefreshSecond >= 3))

        if shouldRefreshLocalizedText then

            -- StringTables can finish loading before a screen creates its
            -- text widgets. I keep this lightweight refresh available so a
            -- later menu still receives the replacement text.
            lastLocalizedTextRefreshSecond =
                currentSecond

            if localizedTextRefreshPassesRemaining > 0 then
                localizedTextRefreshPassesRemaining =
                    localizedTextRefreshPassesRemaining - 1
            end

            refreshLocalizedTextWidgets()
        end

        processCommand()

        -- Returning false keeps the bridge loop running.
        return false
    end)

    initialiseCharacterSlotCatalogue()

    print("[LimelightBridge] Runtime bridge online\n")
    """;

        public string RuntimeDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Runtime");

        public string HeartbeatPath =>
            Path.Combine(
                RuntimeDirectory,
                "heartbeat.txt");

        public string SessionBypassPath =>
            Path.Combine(
                RuntimeDirectory,
                "live-loader-disabled.txt");

        public void SetSessionBypass(
            bool isDisabled)
        {
            if (!isDisabled)
            {
                if (File.Exists(SessionBypassPath))
                {
                    File.Delete(SessionBypassPath);
                }

                return;
            }

            Directory.CreateDirectory(
                RuntimeDirectory);

            // I give the marker an expiry so an interrupted Limelight process
            // can never leave future game launches without the bridge.
            long expiry =
                DateTimeOffset.UtcNow
                    .AddMinutes(10)
                    .ToUnixTimeSeconds();

            File.WriteAllText(
                SessionBypassPath,
                expiry.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        public void EnsureInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "UE4SS must be installed before adding the Limelight bridge.");
            }

            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The UE4SS Mods directory could not be determined.");
            }

            Directory.CreateDirectory(
                RuntimeDirectory);

            string scriptsDirectory =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts");

            Directory.CreateDirectory(
                scriptsDirectory);

            string scriptPath =
                Path.Combine(
                    scriptsDirectory,
                    "main.lua");

            WriteScriptIfChanged(
                scriptPath);

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            EnableBridgeInModsFile(
                modsTextPath);
        }

        public bool IsInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return false;
            }

            string scriptPath =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts",
                    "main.lua");

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            if (!File.Exists(scriptPath) ||
                !File.Exists(modsTextPath))
            {
                return false;
            }

            try
            {
                string installedScript =
                    File.ReadAllText(
                        scriptPath);

                return string.Equals(
                           installedScript,
                           BridgeScript,
                           StringComparison.Ordinal) &&
                       File.ReadLines(modsTextPath)
                           .Any(IsEnabledBridgeLine);
            }
            catch
            {
                return false;
            }
        }

        public bool HasBridgeFiles(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return false;
            }

            string scriptPath =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts",
                    "main.lua");

            // The bridge script only exists after the user has accepted
            // setup, so it is safe for Limelight to repair its mods.txt entry.
            return File.Exists(scriptPath);
        }

        public bool IsOnline()
        {
            try
            {
                if (!File.Exists(HeartbeatPath))
                {
                    return false;
                }

                DateTime lastHeartbeat =
                    File.GetLastWriteTimeUtc(
                        HeartbeatPath);

                TimeSpan heartbeatAge =
                    DateTime.UtcNow -
                    lastHeartbeat;

                // The bridge writes once per second. Five seconds leaves room
                // for a loading screen or a short frame-rate stall.
                return heartbeatAge >=
                           TimeSpan.FromSeconds(-2) &&
                       heartbeatAge <=
                           TimeSpan.FromSeconds(5);
            }
            catch
            {
                return false;
            }
        }

        public void ClearHeartbeat()
        {
            try
            {
                if (File.Exists(HeartbeatPath))
                {
                    File.Delete(HeartbeatPath);
                }
            }
            catch
            {
                // A stale heartbeat naturally expires after five seconds, so
                // failing to remove it is harmless.
            }
        }

        private static void WriteScriptIfChanged(
            string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                string existingScript =
                    File.ReadAllText(scriptPath);

                if (string.Equals(
                        existingScript,
                        BridgeScript,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            // Limelight owns this one script, so updating it does not affect
            // any other UE4SS mods the user has installed.
            File.WriteAllText(
                scriptPath,
                BridgeScript);
        }

        private static void EnableBridgeInModsFile(
            string modsTextPath)
        {
            List<string> lines =
                File.Exists(modsTextPath)
                    ? File.ReadAllLines(modsTextPath).ToList()
                    : new List<string>();

            int existingLineIndex =
                lines.FindIndex(IsBridgeLine);

            if (existingLineIndex >= 0)
            {
                if (IsEnabledBridgeLine(
                        lines[existingLineIndex]))
                {
                    return;
                }

                lines[existingLineIndex] =
                    $"{BridgeName} : 1";
            }
            else
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(
                    $"{BridgeName} : 1");
            }

            string? modsDirectory =
                Path.GetDirectoryName(modsTextPath);

            if (!string.IsNullOrWhiteSpace(modsDirectory))
            {
                Directory.CreateDirectory(
                    modsDirectory);
            }

            string temporaryPath =
                modsTextPath + ".limelight.tmp";

            try
            {
                File.WriteAllLines(
                    temporaryPath,
                    lines);

                if (File.Exists(modsTextPath))
                {
                    // Keep one small safety copy because mods.txt may also
                    // contain entries belonging to other tools.
                    File.Copy(
                        modsTextPath,
                        modsTextPath + ".limelight.bak",
                        overwrite: true);
                }

                File.Move(
                    temporaryPath,
                    modsTextPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool IsBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length > 0 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabledBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length == 2 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase) &&
                   parts[1].Trim().StartsWith(
                       "1",
                       StringComparison.Ordinal);
        }
    }
}
