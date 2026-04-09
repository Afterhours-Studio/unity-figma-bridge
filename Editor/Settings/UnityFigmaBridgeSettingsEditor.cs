using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    [CustomEditor(typeof(UnityFigmaBridgeSettings))]
    public sealed class UnityFigmaBridgeSettingsEditor : UnityEditor.Editor
    {
        private static Vector2 s_PageScrollPos;

        public override void OnInspectorGUI()
        {
            var targetSettingsObject = target as UnityFigmaBridgeSettings;
            var preEditUrl = targetSettingsObject.DocumentUrl;

            base.OnInspectorGUI();

            // If the URL has changed, clear page list so it gets re-fetched
            if (targetSettingsObject.DocumentUrl != preEditUrl)
                targetSettingsObject.PageDataList.Clear();

            if (targetSettingsObject.PageDataList.Count > 0)
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
    }
}
