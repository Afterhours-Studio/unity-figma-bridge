using UnityEditor;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Settings
{
    [CustomEditor(typeof(UnityFigmaBridgeSettings))]
    public sealed class UnityFigmaBridgeSettingsEditor : UnityEditor.Editor
    {
        private static Vector2 s_PageScrollPos;

        public override void OnInspectorGUI()
        {
            var targetSettingsObject = target as UnityFigmaBridgeSettings;
            var onlyImportPages = targetSettingsObject.OnlyImportSelectedPages;
            var preEditUrl = targetSettingsObject.DocumentUrl;

            base.OnInspectorGUI();

            // If the URL has changed, reset page selection
            if (targetSettingsObject.DocumentUrl != preEditUrl)
            {
                if (targetSettingsObject.OnlyImportSelectedPages)
                {
                    targetSettingsObject.OnlyImportSelectedPages = false;
                    targetSettingsObject.PageDataList.Clear();
                }
            }
            else if (targetSettingsObject.OnlyImportSelectedPages != onlyImportPages)
            {
                if (targetSettingsObject.OnlyImportSelectedPages)
                    RefreshPageList(targetSettingsObject);
                else
                    targetSettingsObject.PageDataList.Clear();
            }

            if (targetSettingsObject.OnlyImportSelectedPages)
            {
                GUILayout.Space(20);
                var changed = SettingsInspectorDrawer.DrawPageList(
                    "Select Pages to import", targetSettingsObject.PageDataList, ref s_PageScrollPos);
                if (changed)
                {
                    EditorUtility.SetDirty(targetSettingsObject);
                    AssetDatabase.SaveAssetIfDirty(targetSettingsObject);
                }
            }
        }

        private static async void RefreshPageList(UnityFigmaBridgeSettings settings)
        {
            var requirementsMet = UnityFigmaBridgeImporter.CheckRequirements();
            if (!requirementsMet) return;

            var figmaFile = await UnityFigmaBridgeImporter.DownloadFigmaDocument(settings.FileId);
            if (figmaFile == null) return;

            settings.RefreshForUpdatedPages(figmaFile);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
    }
}