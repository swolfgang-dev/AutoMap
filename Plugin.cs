using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GlobalEnums;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WheresWolfgang.SaveScopedConfig;

namespace AutoMap
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(SaveScopedConfigPlugin.PluginGuid, "0.4.0")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "whereswolfgang.automap";
        public const string PluginName = "AutoMap";
        public const string PluginVersion = "1.1.0";

        private static readonly Harmony Harmony = new Harmony(PluginGuid);
        private static readonly MethodInfo GameMapIsSceneMappableMethod = AccessTools.Method(typeof(GameMap), "IsSceneMappable");
        private static readonly FieldInfo GameMapSceneSpriteRendererField = AccessTools.Field(typeof(GameMapScene), "spriteRenderer");
        private static readonly FieldInfo GameMapSceneInitialStateField = AccessTools.Field(typeof(GameMapScene), "initialState");
        private static readonly FieldInfo GameMapSceneInitialSpriteField = AccessTools.Field(typeof(GameMapScene), "initialSprite");
        private static readonly FieldInfo GameMapSceneFullSpriteField = AccessTools.Field(typeof(GameMapScene), "fullSprite");
        private static readonly FieldInfo GameMapSceneInitialColorField = AccessTools.Field(typeof(GameMapScene), "initialColor");
        private static readonly FieldInfo GameMapCurrentSceneField = AccessTools.Field(typeof(GameMap), "currentScene");
        private static readonly FieldInfo GameMapMapZoneInfoField = AccessTools.Field(typeof(GameMap), "mapZoneInfo");
        private static readonly FieldInfo GameMapZoneInfoParentsField = AccessTools.Field(AccessTools.TypeByName("GameMap+ZoneInfo"), "Parents");
        private static readonly FieldInfo GameMapParentInfoPlayerDataBoolField = AccessTools.Field(AccessTools.TypeByName("GameMap+ParentInfo"), "PlayerDataBool");
        private static readonly FieldInfo GameMapParentInfoParentField = AccessTools.Field(AccessTools.TypeByName("GameMap+ParentInfo"), "Parent");
        private static readonly FieldInfo GameMapParentInfoMapsField = AccessTools.Field(AccessTools.TypeByName("GameMap+ParentInfo"), "Maps");
        private static readonly FieldInfo GameMapMapCacheSceneNameField = AccessTools.Field(AccessTools.TypeByName("GameMap+ZoneInfo+MapCache"), "sceneName");
        private static readonly FieldInfo GameMapMapCacheGameMapSceneField = AccessTools.Field(AccessTools.TypeByName("GameMap+ZoneInfo+MapCache"), "gameMapScene");
        private static readonly FieldInfo InventoryItemWideMapZoneSpriteField = AccessTools.Field(typeof(InventoryItemWideMapZone), "sprite");
        private static readonly FieldInfo InventoryItemWideMapZoneInitialColorField = AccessTools.Field(typeof(InventoryItemWideMapZone), "initialColor");
        private static readonly FieldInfo InventoryItemWideMapZoneInitialLabelColorField = AccessTools.Field(typeof(InventoryItemWideMapZone), "initialLabelColor");
        private static readonly MethodInfo InventoryItemWideMapZoneEvaluateUnlockedMethod = AccessTools.Method(typeof(InventoryItemWideMapZone), "EvaluateUnlocked");
        private static readonly MethodInfo InventoryItemWideMapZoneUpdateColorMethod = AccessTools.Method(typeof(InventoryItemWideMapZone), "UpdateColor");
        private static readonly MethodInfo InventoryItemSelectableDirectionalEvaluateAutoNavMethod = AccessTools.Method(typeof(InventoryItemSelectableDirectional), "EvaluateAutoNav");
        private static RegionMapState activeQuickMapRegionMapState;
        private static RegionMapState activeInventoryMapRegionMapState;
        private static bool isInventoryMapActive;
        private static bool isApplyingWideMapZoneVisual;
        private ConfigEntry<bool> globalQuillRequired;
        private ConfigEntry<bool> globalAreaMapRequired;
        private ConfigEntry<bool> globalAutoUnlockAreaMaps;
        private ConfigEntry<bool> globalAutoUpdateSceneMaps;
        private SaveScopedConfigEntry<bool> saveQuillRequired;
        private SaveScopedConfigEntry<bool> saveAreaMapRequired;
        private SaveScopedConfigEntry<bool> saveAutoUnlockAreaMaps;
        private SaveScopedConfigEntry<bool> saveAutoUpdateSceneMaps;
        private SaveScopedConfigFile saveConfig;

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            saveConfig = SaveScopedConfig.For(this);
            RemoveObsoleteConfigEntries();
            BindConfig();

            ModSettingsUi.BindPanel(Config, "Mapping Status", () =>
            {
                ModSettingsUi.Label(compatibilityStatus);
                ModSettingsUi.Label(GetMappingStatus());
            }, "Explains mapping settings and missing prerequisites for the active scene.");
            ModCompatibility.TryPatch(Harmony, typeof(Plugin).Assembly, Logger, out compatibilityStatus,
                GameMapIsSceneMappableMethod, GameMapCurrentSceneField, GameMapSceneSpriteRendererField);
            Logger.LogInfo(PluginName + " loaded.");
        }

        private void OnDestroy()
        {
            RestoreActiveQuickMapRegionMap();
            RestoreActiveInventoryMapRegionMap();

            if (saveConfig != null)
            {
                saveConfig.Dispose();
                saveConfig = null;
            }

            Harmony.UnpatchSelf();
            Instance = null;
            Log = null;
        }

        private void BindConfig()
        {
            globalQuillRequired = Config.Bind(
                "Global: General",
                "Quill Required",
                false,
                GlobalDescription("Require the quill for AutoMap features on every save.", 400));
            globalAutoUpdateSceneMaps = Config.Bind(
                "Global: General",
                "Auto Map Scene",
                false,
                GlobalDescription("Automatically map the current scene for every save.", 300));
            globalAreaMapRequired = Config.Bind(
                "Global: General",
                "Region Map Required for Auto Map Scene",
                false,
                GlobalDescription("Require the current region's map before Auto Map Scene can work for every save.", 200));
            globalAutoUnlockAreaMaps = Config.Bind(
                "Global: General",
                "Auto Reveal Rough Region Map",
                false,
                GlobalDescription("Automatically reveal the rough map for the active region for every save.", 100));

            saveQuillRequired = saveConfig.Bind(
                "General",
                "Quill Required",
                true,
                SaveDescription("Require the quill for AutoMap features on this save.", 400));
            saveAutoUpdateSceneMaps = saveConfig.Bind(
                "General",
                "Auto Map Scene",
                true,
                SaveDescription("Automatically map the current scene for this save.", 300));
            saveAreaMapRequired = saveConfig.Bind(
                "General",
                "Region Map Required for Auto Map Scene",
                true,
                SaveDescription("Require the current region's map before Auto Map Scene can work for this save.", 200));
            saveAutoUnlockAreaMaps = saveConfig.Bind(
                "General",
                "Auto Reveal Rough Region Map",
                false,
                SaveDescription("Automatically reveal the rough map for the active region for this save.", 100));
        }

        private static ConfigDescription GlobalDescription(string description, int order)
        {
            return new ConfigDescription(description, null, new ConfigurationManagerAttributes { Order = order });
        }

        private static SaveScopedConfigDescription SaveDescription(string description, int order)
        {
            return new SaveScopedConfigDescription(description)
            {
                Order = () => order,
            };
        }

        private void RemoveObsoleteConfigEntries()
        {
            Config.Remove(new ConfigDefinition("Global: General", "Show Visited Scenes Without Area Map"));
            Config.Remove(new ConfigDefinition("Global: General", "Area Map Required"));
            Config.Remove(new ConfigDefinition("Global: General", "Auto-unlock Area Maps"));
            Config.Remove(new ConfigDefinition("Global: General", "Auto-update Scene Maps"));
            for (int saveSlot = 1; saveSlot <= 4; saveSlot++)
            {
                Config.Remove(new ConfigDefinition("Save " + saveSlot + ": General", "Show Visited Scenes Without Area Map"));
                Config.Remove(new ConfigDefinition("Save " + saveSlot + ": General", "Area Map Required"));
                Config.Remove(new ConfigDefinition("Save " + saveSlot + ": General", "Auto-unlock Area Maps"));
                Config.Remove(new ConfigDefinition("Save " + saveSlot + ": General", "Auto-update Scene Maps"));
            }
            Config.Save();
        }

        internal bool IsQuillRequired()
        {
            return (globalQuillRequired != null && globalQuillRequired.Value) ||
                (saveQuillRequired != null && saveQuillRequired.Value);
        }

        internal bool IsAreaMapRequired()
        {
            return (globalAreaMapRequired != null && globalAreaMapRequired.Value) ||
                (saveAreaMapRequired != null && saveAreaMapRequired.Value);
        }

        internal bool HasRequiredQuill()
        {
            if (!IsQuillRequired())
            {
                return true;
            }

            PlayerData playerData = PlayerData.instance;
            return playerData != null && playerData.hasQuill && playerData.QuillState > 0;
        }

        internal bool ShouldAutoUnlockAreaMaps()
        {
            return HasRequiredQuill() &&
                ((globalAutoUnlockAreaMaps != null && globalAutoUnlockAreaMaps.Value) ||
                (saveAutoUnlockAreaMaps != null && saveAutoUnlockAreaMaps.Value));
        }

        internal bool ShouldAutoUpdateSceneMaps()
        {
            return HasRequiredQuill() &&
                ((globalAutoUpdateSceneMaps != null && globalAutoUpdateSceneMaps.Value) ||
                (saveAutoUpdateSceneMaps != null && saveAutoUpdateSceneMaps.Value));
        }

        internal bool ShouldBypassAreaMapRequirementForAutoMap()
        {
            return !IsAreaMapRequired() && ShouldAutoUpdateSceneMaps();
        }

        private bool ShouldUseVirtualQuill()
        {
            return !IsQuillRequired() &&
                (((globalAutoUnlockAreaMaps != null && globalAutoUnlockAreaMaps.Value) ||
                (saveAutoUnlockAreaMaps != null && saveAutoUnlockAreaMaps.Value)) ||
                ((globalAutoUpdateSceneMaps != null && globalAutoUpdateSceneMaps.Value) ||
                (saveAutoUpdateSceneMaps != null && saveAutoUpdateSceneMaps.Value)));
        }

        private static void UpdateSceneMapIfEnabled()
        {
            if (Instance == null || !Instance.ShouldAutoUpdateSceneMaps())
            {
                return;
            }

            GameManager gameManager = GameManager.instance;
            if (gameManager != null)
            {
                QuillStateState state = ApplyVirtualQuillIfNeeded();
                try
                {
                    PrimeCurrentSceneForMapUpdate(gameManager);
                    gameManager.UpdateGameMap();
                }
                finally
                {
                    RestoreVirtualQuill(state);
                }
            }
        }

        private static void PrimeCurrentSceneForMapUpdate(GameManager gameManager)
        {
            if (Instance == null ||
                gameManager == null ||
                !Instance.ShouldAutoUpdateSceneMaps())
            {
                return;
            }

            PlayerData playerData = PlayerData.instance;
            GameMap gameMap = gameManager.gameMap;
            if (playerData == null || gameMap == null)
            {
                return;
            }

            gameMap.UpdateCurrentScene();
            GameMapScene currentScene = GameMapCurrentSceneField == null ? null : GameMapCurrentSceneField.GetValue(gameMap) as GameMapScene;
            string sceneName = currentScene != null ? currentScene.name : gameManager.sceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            bool hasBounds;
            if (!gameMap.HasMapForScene(sceneName, out hasBounds) || !IsSceneMappable(gameMap, sceneName))
            {
                return;
            }

            playerData.scenesVisited.Add(sceneName);
            playerData.scenesMapped.Add(sceneName);

            if (currentScene != null)
            {
                currentScene.SetMapped();
            }
        }

        private static bool IsSceneMappable(GameMap gameMap, string sceneName)
        {
            if (gameMap == null || GameMapIsSceneMappableMethod == null)
            {
                return true;
            }

            return (bool)GameMapIsSceneMappableMethod.Invoke(gameMap, new object[] { sceneName });
        }

        private static bool IsParentForCurrentMapZone(object parentInfo)
        {
            object[] parents = GetCurrentMapZoneParents();
            if (parentInfo == null || parents == null)
            {
                return false;
            }

            for (int i = 0; i < parents.Length; i++)
            {
                if (ReferenceEquals(parents[i], parentInfo))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ParentHasVisitedScene(object parentInfo)
        {
            if (parentInfo == null)
            {
                return false;
            }

            PlayerData playerData = PlayerData.instance;
            if (playerData == null || playerData.scenesVisited == null || playerData.scenesMapped == null)
            {
                return false;
            }

            if (GameMapParentInfoMapsField != null && GameMapMapCacheSceneNameField != null)
            {
                System.Collections.IEnumerable maps = GameMapParentInfoMapsField.GetValue(parentInfo) as System.Collections.IEnumerable;
                if (maps != null)
                {
                    foreach (object map in maps)
                    {
                        string sceneName = map == null ? null : GameMapMapCacheSceneNameField.GetValue(map) as string;
                        if (!string.IsNullOrEmpty(sceneName) &&
                            (playerData.scenesVisited.Contains(sceneName) || playerData.scenesMapped.Contains(sceneName)))
                        {
                            return true;
                        }
                    }
                }
            }

            if (GameMapParentInfoParentField == null)
            {
                return false;
            }

            GameObject parent = GameMapParentInfoParentField.GetValue(parentInfo) as GameObject;
            if (parent == null)
            {
                return false;
            }

            Transform transform = parent.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (child != null &&
                    child.GetComponent<GameMapScene>() != null &&
                    (playerData.scenesVisited.Contains(child.name) || playerData.scenesMapped.Contains(child.name)))
                {
                    return true;
                }
            }

            return false;
        }

        private static object[] GetCurrentMapZoneParents()
        {
            GameManager gameManager = GameManager.instance;
            GameMap gameMap = gameManager == null ? null : gameManager.gameMap;
            if (gameMap == null || GameMapMapZoneInfoField == null || GameMapZoneInfoParentsField == null)
            {
                return null;
            }

            object[] zoneInfos = GameMapMapZoneInfoField.GetValue(gameMap) as object[];
            int zoneIndex = GetCurrentSceneMapZoneIndex(gameManager, gameMap, zoneInfos);
            if (zoneInfos == null || zoneIndex < 0 || zoneIndex >= zoneInfos.Length)
            {
                return null;
            }

            return GetMapZoneParents(gameMap, zoneIndex);
        }

        private static object[] GetMapZoneParents(GameMap gameMap, int zoneIndex)
        {
            if (gameMap == null || GameMapMapZoneInfoField == null || GameMapZoneInfoParentsField == null)
            {
                return null;
            }

            object[] zoneInfos = GameMapMapZoneInfoField.GetValue(gameMap) as object[];
            if (zoneInfos == null || zoneIndex < 0 || zoneIndex >= zoneInfos.Length)
            {
                return null;
            }

            return GameMapZoneInfoParentsField.GetValue(zoneInfos[zoneIndex]) as object[];
        }

        private static int GetCurrentSceneMapZoneIndex(GameManager gameManager, GameMap gameMap, object[] zoneInfos)
        {
            if (gameManager != null && zoneInfos != null)
            {
                int gameManagerZone = (int)gameManager.GetCurrentMapZoneEnum();
                if (gameManagerZone > 0 && gameManagerZone < zoneInfos.Length)
                {
                    return gameManagerZone;
                }
            }

            return gameMap == null ? -1 : (int)gameMap.GetCurrentMapZone();
        }

        private static VirtualState ApplyVirtualStateForQuickMap()
        {
            RestoreActiveQuickMapRegionMap();
            return new VirtualState
            {
                QuillState = ApplyVirtualQuillIfNeeded(),
                RegionMapState = SetActiveQuickMapRegionMap(),
            };
        }

        private static void RestoreVirtualQuillOnly(VirtualState state)
        {
            RestoreVirtualQuill(state.QuillState);
        }

        private static RegionMapState SetActiveQuickMapRegionMap()
        {
            activeQuickMapRegionMapState = ApplyVirtualRegionMapIfNeeded();
            return activeQuickMapRegionMapState;
        }

        private static void RestoreActiveQuickMapRegionMap()
        {
            RestoreVirtualRegionMap(activeQuickMapRegionMapState);
            activeQuickMapRegionMapState = default;
        }

        private static void ActivateInventoryMapState()
        {
            isInventoryMapActive = true;
            RestoreActiveInventoryMapRegionMap();
            QuillStateState quillState = ApplyVirtualQuillIfNeeded();
            activeInventoryMapRegionMapState = ApplyVirtualRegionMapIfNeeded();
            UpdateSceneMapIfEnabled();
            RefreshInventoryMapDisplay();
            RestoreVirtualQuill(quillState);
        }

        private static void RestoreActiveInventoryMapRegionMap()
        {
            RestoreVirtualRegionMap(activeInventoryMapRegionMapState);
            activeInventoryMapRegionMapState = default;
            isInventoryMapActive = false;
        }

        private static bool IsMapPane(InventoryPaneList paneList, InventoryPane pane)
        {
            if (paneList == null || pane == null)
            {
                return false;
            }

            return paneList.GetPaneIndex(pane) == paneList.GetPaneIndex("Map");
        }

        private static void RefreshInventoryMapDisplay()
        {
            GameManager gameManager = GameManager.instance;
            GameMap gameMap = gameManager == null ? null : gameManager.gameMap;
            if (gameMap == null)
            {
                return;
            }

            gameMap.SetupMap(false);
            RefreshWideMapZones();
        }

        private static void RefreshWideMapZones()
        {
            InventoryItemWideMapZone[] zones = Resources.FindObjectsOfTypeAll<InventoryItemWideMapZone>();
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null)
                {
                    InventoryItemWideMapZoneEvaluateUnlockedMethod?.Invoke(zones[i], null);
                }
            }
        }

        private static QuillStateState ApplyVirtualQuillIfNeeded()
        {
            PlayerData playerData = PlayerData.instance;
            if (Instance == null || playerData == null || !Instance.ShouldUseVirtualQuill())
            {
                return default;
            }

            QuillStateState state = new QuillStateState
            {
                Applied = true,
                HadQuill = playerData.hasQuill,
                QuillState = playerData.QuillState,
            };
            playerData.hasQuill = true;
            if (playerData.QuillState <= 0)
            {
                playerData.QuillState = 1;
            }

            return state;
        }

        private static void RestoreVirtualQuill(QuillStateState state)
        {
            if (!state.Applied)
            {
                return;
            }

            PlayerData playerData = PlayerData.instance;
            if (playerData != null)
            {
                playerData.hasQuill = state.HadQuill;
                playerData.QuillState = state.QuillState;
            }
        }

        private static RegionMapState ApplyVirtualRegionMapIfNeeded()
        {
            if (Instance == null || !Instance.ShouldAutoUnlockAreaMaps())
            {
                return default;
            }

            PlayerData playerData = PlayerData.instance;
            object[] parents = GetCurrentMapZoneParents();
            if (playerData == null || parents == null || GameMapParentInfoPlayerDataBoolField == null)
            {
                return default;
            }

            var values = new List<RegionMapBoolState>();
            for (int i = 0; i < parents.Length; i++)
            {
                string boolName = GameMapParentInfoPlayerDataBoolField.GetValue(parents[i]) as string;
                if (string.IsNullOrEmpty(boolName) || ContainsRegionMapBool(values, boolName))
                {
                    continue;
                }

                bool currentValue;
                if (!TryGetPlayerDataBool(playerData, boolName, out currentValue))
                {
                    continue;
                }

                values.Add(new RegionMapBoolState { Name = boolName, Value = currentValue });
                TrySetPlayerDataBool(playerData, boolName, true);
            }

            return new RegionMapState { Applied = values.Count > 0, Values = values };
        }

        private static void RestoreVirtualRegionMap(RegionMapState state)
        {
            if (!state.Applied || state.Values == null)
            {
                return;
            }

            PlayerData playerData = PlayerData.instance;
            if (playerData == null)
            {
                return;
            }

            for (int i = 0; i < state.Values.Count; i++)
            {
                TrySetPlayerDataBool(playerData, state.Values[i].Name, state.Values[i].Value);
            }
        }

        private static bool ContainsRegionMapBool(List<RegionMapBoolState> values, string name)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPlayerDataBool(PlayerData playerData, string name, out bool value)
        {
            FieldInfo field = AccessTools.Field(typeof(PlayerData), name);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(playerData);
                return true;
            }

            PropertyInfo property = AccessTools.Property(typeof(PlayerData), name);
            if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            {
                value = (bool)property.GetValue(playerData, null);
                return true;
            }

            value = false;
            return false;
        }

        private static bool TrySetPlayerDataBool(PlayerData playerData, string name, bool value)
        {
            FieldInfo field = AccessTools.Field(typeof(PlayerData), name);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(playerData, value);
                return true;
            }

            PropertyInfo property = AccessTools.Property(typeof(PlayerData), name);
            if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
            {
                property.SetValue(playerData, value, null);
                return true;
            }

            return false;
        }

        private static bool TryRestoreRoughMapVisual(GameMapScene scene)
        {
            if (scene == null ||
                scene.IsMapped ||
                GameMapSceneInitialStateField == null ||
                GameMapSceneInitialSpriteField == null ||
                GameMapSceneFullSpriteField == null)
            {
                return false;
            }

            SpriteRenderer spriteRenderer = GameMapSceneSpriteRendererField == null
                ? scene.GetComponent<SpriteRenderer>()
                : GameMapSceneSpriteRendererField.GetValue(scene) as SpriteRenderer;
            if (spriteRenderer == null || spriteRenderer.sprite != null)
            {
                return false;
            }

            int initialState = (int)GameMapSceneInitialStateField.GetValue(scene);
            if (initialState != 1)
            {
                return false;
            }

            Sprite initialSprite = GameMapSceneInitialSpriteField.GetValue(scene) as Sprite;
            Sprite fullSprite = GameMapSceneFullSpriteField.GetValue(scene) as Sprite;
            if (initialSprite == null || fullSprite == null)
            {
                return false;
            }

            spriteRenderer.sprite = initialSprite;
            return true;
        }

        private static bool HasVisitedSceneInZone(GameMap gameMap, MapZone mapZone)
        {
            if (Instance == null || !Instance.ShouldBypassAreaMapRequirementForAutoMap())
            {
                return false;
            }

            object[] parents = GetMapZoneParents(gameMap, (int)mapZone);
            if (parents == null)
            {
                return false;
            }

            for (int i = 0; i < parents.Length; i++)
            {
                if (ParentHasVisitedScene(parents[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCurrentSceneMapZone(GameMap gameMap, MapZone mapZone)
        {
            GameManager gameManager = GameManager.instance;
            object[] zoneInfos = gameMap == null || GameMapMapZoneInfoField == null
                ? null
                : GameMapMapZoneInfoField.GetValue(gameMap) as object[];
            return GetCurrentSceneMapZoneIndex(gameManager, gameMap, zoneInfos) == (int)mapZone;
        }

        private static bool ShouldUnlockWideMapZone(InventoryItemWideMapZone item)
        {
            if (item == null || Instance == null)
            {
                return false;
            }

            GameManager gameManager = GameManager.instance;
            GameMap gameMap = gameManager == null ? null : gameManager.gameMap;
            if (gameMap == null)
            {
                return false;
            }

            foreach (MapZone mapZone in item.EnumerateMapZones())
            {
                bool currentReveal = Instance.ShouldAutoUnlockAreaMaps() && IsCurrentSceneMapZone(gameMap, mapZone);
                bool visitedReveal = HasVisitedSceneInZone(gameMap, mapZone);
                if (currentReveal || visitedReveal)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetWideMapZoneColor(InventoryItemWideMapZone item, out Color color)
        {
            color = Color.white;
            if (item == null)
            {
                return false;
            }

            GameManager gameManager = GameManager.instance;
            GameMap gameMap = gameManager == null ? null : gameManager.gameMap;
            PlayerData playerData = PlayerData.instance;
            if (gameMap == null || playerData == null)
            {
                return false;
            }

            foreach (MapZone mapZone in item.EnumerateMapZones())
            {
                object[] parents = GetMapZoneParents(gameMap, (int)mapZone);
                if (parents == null)
                {
                    continue;
                }

                for (int i = 0; i < parents.Length; i++)
                {
                    if (TryGetParentVisitedSceneColor(parents[i], playerData, out color))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetParentVisitedSceneColor(object parentInfo, PlayerData playerData, out Color color)
        {
            color = Color.white;
            if (parentInfo == null || playerData == null || GameMapParentInfoMapsField == null)
            {
                return false;
            }

            System.Collections.IEnumerable maps = GameMapParentInfoMapsField.GetValue(parentInfo) as System.Collections.IEnumerable;
            if (maps == null)
            {
                return false;
            }

            foreach (object map in maps)
            {
                string sceneName = map == null || GameMapMapCacheSceneNameField == null
                    ? null
                    : GameMapMapCacheSceneNameField.GetValue(map) as string;
                if (string.IsNullOrEmpty(sceneName) ||
                    (!playerData.scenesVisited.Contains(sceneName) && !playerData.scenesMapped.Contains(sceneName)))
                {
                    continue;
                }

                GameMapScene scene = GameMapMapCacheGameMapSceneField == null
                    ? null
                    : GameMapMapCacheGameMapSceneField.GetValue(map) as GameMapScene;
                if (TryGetGameMapSceneColor(scene, out color))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetGameMapSceneColor(GameMapScene scene, out Color color)
        {
            color = Color.white;
            if (scene == null)
            {
                return false;
            }

            if (GameMapSceneInitialColorField != null)
            {
                Color initialColor = (Color)GameMapSceneInitialColorField.GetValue(scene);
                if (initialColor.a > 0f)
                {
                    color = initialColor;
                    color.a = 1f;
                    return true;
                }
            }

            SpriteRenderer spriteRenderer = GameMapSceneSpriteRendererField == null
                ? scene.GetComponent<SpriteRenderer>()
                : GameMapSceneSpriteRendererField.GetValue(scene) as SpriteRenderer;
            if (spriteRenderer == null)
            {
                return false;
            }

            color = spriteRenderer.color;
            color.a = 1f;
            return true;
        }

        private static void ShowWideMapZoneTile(InventoryItemWideMapZone item)
        {
            if (item == null || isApplyingWideMapZoneVisual || !ShouldUnlockWideMapZone(item))
            {
                return;
            }

            if (!item.gameObject.activeSelf)
            {
                item.gameObject.SetActive(true);
            }

            Color zoneColor;
            if (TryGetWideMapZoneColor(item, out zoneColor))
            {
                if (InventoryItemWideMapZoneInitialColorField != null)
                {
                    InventoryItemWideMapZoneInitialColorField.SetValue(item, zoneColor);
                }

                if (InventoryItemWideMapZoneInitialLabelColorField != null)
                {
                    InventoryItemWideMapZoneInitialLabelColorField.SetValue(item, zoneColor);
                }
            }

            isApplyingWideMapZoneVisual = true;
            try
            {
                InventoryItemWideMapZoneUpdateColorMethod?.Invoke(item, null);
            }
            finally
            {
                isApplyingWideMapZoneVisual = false;
            }

            SpriteRenderer spriteRenderer = InventoryItemWideMapZoneSpriteField == null
                ? item.GetComponent<SpriteRenderer>()
                : InventoryItemWideMapZoneSpriteField.GetValue(item) as SpriteRenderer;
            if (spriteRenderer == null)
            {
                return;
            }

            spriteRenderer.enabled = true;
            Color color = spriteRenderer.color;
            color.a = 1f;
            spriteRenderer.color = color;
            InventoryItemSelectableDirectionalEvaluateAutoNavMethod?.Invoke(item, null);
        }

        [HarmonyPatch(typeof(GameMap), "TryOpenQuickMap")]
        private static class GameMapTryOpenQuickMapPatch
        {
            private static void Prefix(out VirtualState __state)
            {
                __state = ApplyVirtualStateForQuickMap();
                UpdateSceneMapIfEnabled();
            }

            private static void Postfix(VirtualState __state)
            {
                RestoreVirtualQuillOnly(__state);
            }

            private static void Finalizer(System.Exception __exception, VirtualState __state)
            {
                if (__exception != null)
                {
                    RestoreVirtualQuillOnly(__state);
                    RestoreActiveQuickMapRegionMap();
                }
            }
        }

        [HarmonyPatch(typeof(GameMap), "CloseQuickMap")]
        private static class GameMapCloseQuickMapPatch
        {
            private static void Postfix()
            {
                RestoreActiveQuickMapRegionMap();
            }
        }

        [HarmonyPatch(typeof(InventoryPaneInput), "GetInventoryInputPressed")]
        private static class InventoryPaneInputGetInventoryInputPressedPatch
        {
            private static void Postfix(InventoryPaneList.PaneTypes __result)
            {
                if (__result == InventoryPaneList.PaneTypes.Map)
                {
                    ActivateInventoryMapState();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryPaneList), "BeginPane")]
        private static class InventoryPaneListBeginPanePatch
        {
            private static void Prefix(InventoryPaneList __instance, InventoryPane pane)
            {
                if (IsMapPane(__instance, pane))
                {
                    ActivateInventoryMapState();
                    return;
                }

                RestoreActiveInventoryMapRegionMap();
            }

            private static void Postfix(InventoryPaneList __instance, InventoryPane pane)
            {
                if (IsMapPane(__instance, pane))
                {
                    ActivateInventoryMapState();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryPaneList), "OnClosingInventory")]
        private static class InventoryPaneListOnClosingInventoryPatch
        {
            private static void Prefix()
            {
                RestoreActiveInventoryMapRegionMap();
            }
        }

        [HarmonyPatch(typeof(InventoryMapManager), "OnPaneStart")]
        private static class InventoryMapManagerOnPaneStartPatch
        {
            private static void Postfix()
            {
                ActivateInventoryMapState();
            }
        }

        [HarmonyPatch(typeof(InventoryMapManager), "ZoomIn")]
        private static class InventoryMapManagerZoomInPatch
        {
            private static void Prefix()
            {
                RefreshInventoryMapDisplay();
            }
        }

        [HarmonyPatch(typeof(InventoryItemWideMapZone), "get_IsUnlocked")]
        private static class InventoryItemWideMapZoneIsUnlockedPatch
        {
            private static void Postfix(InventoryItemWideMapZone __instance, ref bool __result)
            {
                bool shouldUnlock = ShouldUnlockWideMapZone(__instance);
                if (__result)
                {
                    return;
                }

                if (shouldUnlock)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(InventoryItemWideMapZone), "UpdateColor")]
        private static class InventoryItemWideMapZoneUpdateColorPatch
        {
            private static void Postfix(InventoryItemWideMapZone __instance)
            {
                ShowWideMapZoneTile(__instance);
            }
        }

        [HarmonyPatch(typeof(PlayerData), "get_HasAnyMap")]
        private static class PlayerDataHasAnyMapPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result &&
                    Instance != null &&
                    (Instance.ShouldAutoUnlockAreaMaps() ||
                    Instance.ShouldBypassAreaMapRequirementForAutoMap()))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerData), "get_CanUpdateMap")]
        private static class PlayerDataCanUpdateMapPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result &&
                    Instance != null &&
                    (Instance.ShouldAutoUpdateSceneMaps() ||
                    Instance.ShouldAutoUnlockAreaMaps()))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(GameMap), "HasAnyMapForZone")]
        private static class GameMapHasAnyMapForZonePatch
        {
            private static void Postfix(GameMap __instance, MapZone mapZone, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                if (Instance == null)
                {
                    return;
                }

                bool currentReveal = Instance.ShouldAutoUnlockAreaMaps() && IsCurrentSceneMapZone(__instance, mapZone);
                bool visitedReveal = HasVisitedSceneInZone(__instance, mapZone);
                if (currentReveal || visitedReveal)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch]
        private static class GameMapParentInfoIsUnlockedPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.PropertyGetter(AccessTools.TypeByName("GameMap+ParentInfo"), "IsUnlocked");
            }

            private static void Postfix(object __instance, ref bool __result)
            {
                if (__result || Instance == null)
                {
                    return;
                }

                bool currentReveal = Instance.ShouldAutoUnlockAreaMaps() && IsParentForCurrentMapZone(__instance);
                bool visitedReveal = Instance.ShouldBypassAreaMapRequirementForAutoMap() && ParentHasVisitedScene(__instance);
                if (currentReveal || visitedReveal)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(GameMapScene), "SetNotMapped")]
        private static class GameMapSceneSetNotMappedPatch
        {
            private static void Postfix(GameMapScene __instance)
            {
                if (Instance == null ||
                    __instance == null)
                {
                    return;
                }

                if (Instance.ShouldAutoUnlockAreaMaps())
                {
                    TryRestoreRoughMapVisual(__instance);
                    return;
                }

                if (!Instance.ShouldAutoUpdateSceneMaps())
                {
                    return;
                }

                if (isInventoryMapActive)
                {
                    return;
                }

                SpriteRenderer spriteRenderer = GameMapSceneSpriteRendererField == null
                    ? __instance.GetComponent<SpriteRenderer>()
                    : GameMapSceneSpriteRendererField.GetValue(__instance) as SpriteRenderer;
                if (spriteRenderer != null)
                {
                    spriteRenderer.sprite = null;
                }
            }
        }

        private struct VirtualState
        {
            public QuillStateState QuillState;
            public RegionMapState RegionMapState;
        }

        private struct QuillStateState
        {
            public bool Applied;
            public bool HadQuill;
            public int QuillState;
        }

        private struct RegionMapState
        {
            public bool Applied;
            public List<RegionMapBoolState> Values;
        }

        private struct RegionMapBoolState
        {
            public string Name;
            public bool Value;
        }
    }
}
