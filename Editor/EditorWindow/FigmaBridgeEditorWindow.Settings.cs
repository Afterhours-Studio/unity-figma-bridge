using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    public sealed partial class FigmaBridgeEditorWindow
    {
        // ─── Settings Tab ────────────────────────────────

        private void DrawSettingsTab()
        {
            EnsureSettingsLoaded();

            if (_settings == null)
            {
                DrawEmptyState();
                return;
            }

            if (_serializedSettings == null || _serializedSettings.targetObject == null)
                _serializedSettings = new SerializedObject(_settings);

            BeginCard("Settings");
            SettingsInspectorDrawer.DrawSettings(_settings, _serializedSettings);
            EndCard();

            if (_settings.PageDataList.Count > 0)
            {
                BeginCard("Page Selection");
                var changed = SettingsInspectorDrawer.DrawPageList(
                    "Select Pages to import", _settings.PageDataList, ref _pageScrollPos);
                if (changed)
                {
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssetIfDirty(_settings);
                }
                EndCard();
            }

            // Sections card
            DrawSectionsCard();
        }

        private void DrawSectionsCard()
        {
            BeginCard("Section Selection");

            if (_cachedSectionNames == null || _cachedSectionNames.Length <= 1)
            {
                GUILayout.Label("No sections loaded. Click Refresh to fetch from Figma.", s_MiniHintWrap);
                GUILayout.Space(4);

                using (new EditorGUI.DisabledGroupScope(
                    string.IsNullOrEmpty(_tokenInput) || _settings == null || string.IsNullOrEmpty(_settings.FileId)))
                {
                    DrawColorButton("Refresh Sections", BtnGray, BtnGrayHover,
                        () => RefreshSections(), GUILayout.Height(28));
                }
            }
            else
            {
                // Header row
                EditorGUILayout.BeginHorizontal();
                var countText = $"{_cachedSectionNames.Length - 1} section(s) found";
                DrawBadge(countText, Accent);
                GUILayout.FlexibleSpace();
                if (DrawSmallButton("Refresh"))
                    RefreshSections();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                // Section list
                const float rowH = 24;
                var evenBg = Pro ? C(0.22f) : C(0.94f);
                var oddBg = Pro ? C(0.20f) : C(0.92f);
                var selectedSection = _settings != null ? _settings.SelectedSection : "";

                for (int i = 1; i < _cachedSectionNames.Length; i++)
                {
                    var sectionName = _cachedSectionNames[i];
                    var isSelected = sectionName == selectedSection;
                    var rRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));

                    if (Event.current.type == EventType.Repaint)
                    {
                        var bg = isSelected
                            ? new UnityEngine.Color(Accent.r, Accent.g, Accent.b, 0.2f)
                            : (i % 2 == 0 ? evenBg : oddBg);
                        EditorGUI.DrawRect(rRect, bg);

                        // Left accent bar for selected
                        if (isSelected)
                            EditorGUI.DrawRect(new Rect(rRect.x, rRect.y + 2, 3, rowH - 4), Accent);
                    }

                    var sectionStyle = new GUIStyle(s_SectionName)
                    {
                        fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                        normal = { textColor = isSelected ? Accent : (Pro ? C(0.78f) : C(0.2f)) },
                    };
                    GUI.Label(rRect, sectionName, sectionStyle);

                    // Click to select/deselect
                    if (Event.current.type == EventType.MouseDown && rRect.Contains(Event.current.mousePosition))
                    {
                        if (isSelected)
                        {
                            // Deselect
                            _selectedSectionIndex = 0;
                            if (_settings != null) _settings.SelectedSection = "";
                        }
                        else
                        {
                            _selectedSectionIndex = i;
                            if (_settings != null) _settings.SelectedSection = sectionName;
                        }
                        if (_settings != null) EditorUtility.SetDirty(_settings);
                        Event.current.Use();
                        Repaint();
                    }

                    EditorGUIUtility.AddCursorRect(rRect, MouseCursor.Link);
                }

                GUILayout.Space(4);
                var selLabel = string.IsNullOrEmpty(selectedSection) ? "All sections" : selectedSection;
                DrawKeyValue("Active", selLabel, string.IsNullOrEmpty(selectedSection) ? MutedText : Accent);
            }

            EndCard();
        }

        private void DrawEmptyState()
        {
            GUILayout.Space(60);
            GUILayout.Label("?", s_EmptyIcon, GUILayout.Height(40)); // placeholder icon
            GUILayout.Space(8);

            GUILayout.Label("No settings asset found", s_EmptyCenter);
            GUILayout.Space(12);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            DrawColorButton("Create Settings Asset", BtnGray, BtnGrayHover, () =>
            {
                _settings = UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
                _serializedSettings = new SerializedObject(_settings);
                AppendLog("Settings asset created");
                EditorGUIUtility.PingObject(_settings);
            }, GUILayout.Height(30), GUILayout.Width(200));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Or drag a settings asset into the Config field above.", s_EmptyGrey);
        }
    }
}
