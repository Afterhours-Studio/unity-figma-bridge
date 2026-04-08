using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Settings
{
    /// <summary>
    /// Shared drawing helper for UnityFigmaBridgeSettings - used by both the
    /// custom inspector (UnityFigmaBridgeSettingsEditor) and the unified EditorWindow.
    /// </summary>
    internal static class SettingsInspectorDrawer
    {
        private static GUIStyle s_RedStyle;
        private static GUIStyle s_GreenStyle;

        private static void EnsureStyles()
        {
            if (s_RedStyle != null) return;
            s_RedStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = UnityEngine.Color.red } };
            s_GreenStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = UnityEngine.Color.green } };
        }

        /// <summary>
        /// Draw all settings properties with short labels and tooltips.
        /// </summary>
        public static void DrawSettings(UnityFigmaBridgeSettings settings, SerializedObject serializedObject)
        {
            EnsureStyles();
            serializedObject.Update();

            DrawProp(serializedObject, "DocumentUrl", "Figma URL", "Figma URL - supports Document, Page, and Frame URLs with node-id");
            DrawProp(serializedObject, "BuildPrototypeFlow", "Prototype Flow", "Generate logic and linking of screens based on Figma's Prototype settings");

            GUILayout.Space(6);
            DrawProp(serializedObject, "RunTimeAssetsScenePath", "Scene Path", "Scene used for prototype assets, including canvas");
            DrawProp(serializedObject, "AssetOutputPath", "Output Path", "Folder to store imported Figma assets (prefabs, images, fonts)");
            DrawProp(serializedObject, "ServerRenderImageScale", "Render Scale", "Scale multiplier for server-rendered images");
            DrawProp(serializedObject, "EnableAutoLayout", "Auto Layout", "Convert Figma Auto Layout to Unity Layout Groups (Horizontal/Vertical)");

            GUILayout.Space(6);
            DrawProp(serializedObject, "EnableGoogleFontsDownloads", "Google Fonts", "Download missing fonts from Google Fonts automatically");
            DrawProp(serializedObject, "OnlyImportExportNodes", "Export Only", "Only import nodes marked for Export in Figma (ignores all other nodes)");
            DrawProp(serializedObject, "OnlyImportSelectedPages", "Select Pages", "Only import selected pages instead of all");
            DrawProp(serializedObject, "SyncDepth", "Sync Depth", "Layer depth: 0 = full depth, 1 = top-level only, 2+ = descend N levels");

            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(8);
            DrawUrlValidation(settings);
        }

        private static void DrawProp(SerializedObject so, string propName, string label, string tooltip)
        {
            var prop = so.FindProperty(propName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip));
        }

        /// <summary>
        /// Show whether the configured Figma document URL is valid.
        /// </summary>
        public static void DrawUrlValidation(UnityFigmaBridgeSettings settings)
        {
            EnsureStyles();

            var (isValid, fileId) = FigmaApiUtils.GetFigmaDocumentIdFromUrl(settings.DocumentUrl);
            if (!isValid)
            {
                GUILayout.Label("Invalid Figma Document URL", s_RedStyle);
                return;
            }
            GUILayout.Label($"Valid Figma Document URL - FileID: {fileId}", s_GreenStyle);
        }

        /// <summary>
        /// Draw a selectable page list with Select All / Deselect All buttons.
        /// Returns true if any selection changed.
        /// </summary>
        public static bool DrawPageList(string listTitle, IReadOnlyList<FigmaPageData> dataList, ref Vector2 scrollPos)
        {
            var applyChanges = false;

            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(listTitle, EditorStyles.boldLabel);
                GUILayout.Space(5);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in dataList) data.Selected = true;
                    }
                    if (GUILayout.Button("Deselect all", GUILayout.Width(80)))
                    {
                        applyChanges = true;
                        foreach (var data in dataList) data.Selected = false;
                    }
                }

                GUILayout.Space(5);

                using (var scrollViewScope = new EditorGUILayout.ScrollViewScope(scrollPos))
                {
                    foreach (var data in dataList)
                    {
                        var prev = data.Selected;
                        data.Selected = EditorGUILayout.ToggleLeft(data.Name, data.Selected);
                        if (prev != data.Selected) applyChanges = true;
                    }
                    scrollPos = scrollViewScope.scrollPosition;
                }
            }

            return applyChanges;
        }
    }
}
