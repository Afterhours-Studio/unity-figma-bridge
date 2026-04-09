using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    /// <summary>
    /// Shared drawing helper for UnityFigmaBridgeSettings - used by both the
    /// custom inspector (UnityFigmaBridgeSettingsEditor) and the unified EditorWindow.
    /// </summary>
    internal static class SettingsInspectorDrawer
    {
        private static readonly string[] SettingsTabs = { "Import", "Build" };
        private static int s_SettingsTab;

        private static GUIStyle s_RedStyle;
        private static GUIStyle s_GreenStyle;

        private static void EnsureStyles()
        {
            if (s_RedStyle != null) return;
            s_RedStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = UnityEngine.Color.red } };
            s_GreenStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = UnityEngine.Color.green } };
        }

        /// <summary>
        /// Draw settings split into Import / Build sub-tabs.
        /// </summary>
        public static void DrawSettings(UnityFigmaBridgeSettings settings, SerializedObject so)
        {
            EnsureStyles();
            s_SettingsTab = GUILayout.Toolbar(s_SettingsTab, SettingsTabs);
            GUILayout.Space(6);
            so.Update();

            if (s_SettingsTab == 0)
                DrawImportSettings(so);
            else
                DrawBuildSettings(so);

            so.ApplyModifiedProperties();

            if (s_SettingsTab == 0)
            {
                GUILayout.Space(8);
                DrawUrlValidation(settings);
            }
        }

        private static void DrawImportSettings(SerializedObject so)
        {
            DrawProp(so, "DocumentUrl",               "Figma URL",    "Figma URL - supports Document, Page, and Frame URLs with node-id");
            GUILayout.Space(4);
            DrawProp(so, "AssetOutputPath",           "Output Path",  "Folder to store imported Figma assets (prefabs, images, fonts)");
            DrawProp(so, "ServerRenderImageScale",    "Render Scale", "Scale multiplier for server-rendered images");
            DrawProp(so, "EnableGoogleFontsDownloads","Google Fonts", "Download missing fonts from Google Fonts automatically");
            DrawProp(so, "OnlyImportExportNodes",     "Export Only",  "Only import nodes marked for Export in Figma (ignores all other nodes)");
            DrawProp(so, "SyncDepth",                 "Sync Depth",   "Layer depth: 0 = full depth, 1 = top-level only, 2+ = descend N levels");
        }

        private static void DrawBuildSettings(SerializedObject so)
        {
            DrawProp(so, "RunTimeAssetsScenePath", "Scene Path",       "Scene path for runtime assets. Created automatically if it does not exist.");
            DrawCanvasSize(so);
            DrawProp(so, "EnableAutoLayout",  "Auto Layout",           "Convert Figma Auto Layout to Unity Layout Groups (Horizontal/Vertical)");
            DrawProp(so, "SkipTextImages",    "Skip Text Images",      "Never server-render text nodes — use TMP/Text components instead of downloading images");
            DrawProp(so, "TextMode",          "Text Mode",             "Auto = TMP if available, else legacy Text. Force TextMeshPro or LegacyText to override.");
            DrawProp(so, "SmartNaming",       "Smart Naming",          "Format node names to snake_case or PascalCase. [Tags] are stripped automatically.");
        }

        private static void DrawCanvasSize(SerializedObject so)
        {
            var wProp = so.FindProperty("CanvasWidth");
            var hProp = so.FindProperty("CanvasHeight");
            if (wProp == null || hProp == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Canvas Size", "Reference resolution for the CanvasScaler (width × height in pixels)"), GUILayout.Width(EditorGUIUtility.labelWidth));
                EditorGUILayout.PropertyField(wProp, GUIContent.none, GUILayout.MinWidth(60));
                EditorGUILayout.LabelField("×", GUILayout.Width(14));
                EditorGUILayout.PropertyField(hProp, GUIContent.none, GUILayout.MinWidth(60));
            }
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
