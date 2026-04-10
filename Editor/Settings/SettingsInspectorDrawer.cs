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
        private static readonly UnityEngine.Color TabActive = new(0.24f, 0.44f, 0.74f);
        private static readonly UnityEngine.Color TabHover = new(0.28f, 0.28f, 0.28f);
        private static readonly UnityEngine.Color TabBg = new(0.16f, 0.16f, 0.16f);

        public static void DrawSettings(UnityFigmaBridgeSettings settings, SerializedObject so)
        {
            EnsureStyles();
            DrawSettingsTabBar();
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
            DrawProp(so, "AutoSlice9",       "Auto 9-Slice",          "Auto-detect rounded rectangles and set Image.Type.Sliced with sprite borders from cornerRadius");
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

        private static float s_HighlightX = -1;
        private static double s_AnimStart;
        private static int s_AnimFrom;
        private const float AnimDuration = 0.25f;

        private static void DrawSettingsTabBar()
        {
            bool pro = EditorGUIUtility.isProSkin;
            var pad = 4f;
            var gap = 2f;
            var barRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, pro ? TabBg : new UnityEngine.Color(0.84f, 0.84f, 0.84f));

            var totalGap = gap * (SettingsTabs.Length - 1) + pad * 2;
            var tabW = (barRect.width - totalGap) / SettingsTabs.Length;

            // Compute target X for active tab highlight
            var targetX = barRect.x + pad + s_SettingsTab * (tabW + gap);

            // Animate highlight position
            var animating = false;
            if (s_HighlightX < 0) s_HighlightX = targetX; // first frame
            if (Mathf.Abs(s_HighlightX - targetX) > 0.5f)
            {
                var t = (float)((EditorApplication.timeSinceStartup - s_AnimStart) / AnimDuration);
                t = Mathf.Clamp01(t);
                t = 1f - (1f - t) * (1f - t); // ease-out quad
                var fromX = barRect.x + pad + s_AnimFrom * (tabW + gap);
                s_HighlightX = Mathf.Lerp(fromX, targetX, t);
                animating = t < 1f;
            }
            else
            {
                s_HighlightX = targetX;
            }

            // Draw animated highlight
            if (Event.current.type == EventType.Repaint)
            {
                var hlRect = new Rect(s_HighlightX, barRect.y + 3, tabW, barRect.height - 6);
                EditorGUI.DrawRect(hlRect, TabActive);
            }

            // Draw tab labels + handle clicks
            for (int i = 0; i < SettingsTabs.Length; i++)
            {
                var tabRect = new Rect(barRect.x + pad + i * (tabW + gap), barRect.y + 3, tabW, barRect.height - 6);
                bool active = s_SettingsTab == i;
                bool hover = !active && tabRect.Contains(Event.current.mousePosition);

                if (Event.current.type == EventType.Repaint && hover)
                    EditorGUI.DrawRect(tabRect, pro ? TabHover : new UnityEngine.Color(0.88f, 0.88f, 0.88f));

                var style = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = active ? UnityEngine.Color.white : (pro ? new UnityEngine.Color(0.5f, 0.5f, 0.5f) : new UnityEngine.Color(0.42f, 0.42f, 0.42f)) },
                };

                if (GUI.Button(tabRect, SettingsTabs[i], style))
                {
                    s_AnimFrom = s_SettingsTab;
                    s_AnimStart = EditorApplication.timeSinceStartup;
                    s_SettingsTab = i;
                }

                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }

            // Keep repainting during animation
            if (animating)
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (w.titleContent.text.Contains("Figma")) { w.Repaint(); break; }
        }
    }
}
