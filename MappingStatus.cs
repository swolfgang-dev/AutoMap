using System.Text;
using WheresWolfgang.SaveScopedConfig;

namespace AutoMap
{
    public sealed partial class Plugin
    {
        private string compatibilityStatus = "Starting";

        internal string GetMappingStatus()
        {
            if (!SaveScopedConfig.HasActiveSave || GameManager.instance == null) return "Load a save to inspect mapping.";
            var gm = GameManager.instance;
            var text = new StringBuilder();
            text.AppendLine("Scene mapping: " + ModSettingsUi.ExplainEnablement(false,
                globalAutoUpdateSceneMaps.Value, saveAutoUpdateSceneMaps.Value));
            text.AppendLine("Rough-region reveal: " + ModSettingsUi.ExplainEnablement(false,
                globalAutoUnlockAreaMaps.Value, saveAutoUnlockAreaMaps.Value));
            text.AppendLine("Quill requirement: " + ModSettingsUi.ExplainEnablement(false, globalQuillRequired.Value, saveQuillRequired.Value));
            text.AppendLine("Region-map requirement: " + ModSettingsUi.ExplainEnablement(false, globalAreaMapRequired.Value, saveAreaMapRequired.Value));
            if (!HasRequiredQuill()) text.AppendLine("Waiting for a usable quill.");
            if (globalAutoUpdateSceneMaps.Value || saveAutoUpdateSceneMaps.Value)
            {
                if (gm.gameMap == null || string.IsNullOrEmpty(gm.sceneName)) text.AppendLine("Map is not initialized for this scene.");
                else
                {
                    bool bounds;
                    bool hasMap = gm.gameMap.HasMapForScene(gm.sceneName, out bounds);
                    if (IsAreaMapRequired() && !hasMap) text.AppendLine("Waiting for this region's map.");
                    if (!bounds || !IsSceneMappable(gm.gameMap, gm.sceneName)) text.AppendLine("This scene has no mappable scene detail.");
                    else if (HasRequiredQuill() && (!IsAreaMapRequired() || hasMap)) text.AppendLine("Scene mapping is active.");
                }
            }
            text.Append("Global and save settings combine. Normal map purchases and quill progression are preserved.");
            return text.ToString();
        }
    }
}
