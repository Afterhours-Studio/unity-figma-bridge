using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor
{
    public sealed class FigmaBridgeEditorWindow : EditorWindow
    {
        // ─── Tabs ────────────────────────────────────────
        private static readonly string[] Tabs = { "Import", "Settings", "Log" };
        private int _tabIndex;
        private Vector2 _scrollPos;

        // ─── Theme ───────────────────────────────────────
        private static readonly UnityEngine.Color Accent = new(0.30f, 0.55f, 0.92f);
        private static readonly UnityEngine.Color AccentDim = new(0.22f, 0.42f, 0.72f);
        private static readonly UnityEngine.Color AccentGlow = new(0.40f, 0.65f, 1f, 0.15f);
        private static bool Pro => EditorGUIUtility.isProSkin;
        private static UnityEngine.Color C(float v) => new(v, v, v);
        private static UnityEngine.Color C(float v, float a) => new(v, v, v, a);
        private static UnityEngine.Color HeaderBg => Pro ? C(0.13f) : C(0.80f);
        private static UnityEngine.Color TabBarBg => Pro ? C(0.16f) : C(0.84f);
        private static UnityEngine.Color ActiveTabBg => Pro ? C(0.21f) : C(0.94f);
        private static UnityEngine.Color ContentBg => Pro ? C(0.19f) : C(0.91f);
        private static UnityEngine.Color CardBg => Pro ? C(0.24f) : C(0.97f);
        private static UnityEngine.Color CardBorder => Pro ? C(0.30f) : C(0.82f);
        private static UnityEngine.Color SepColor => Pro ? C(0.28f) : C(0.76f);
        private static UnityEngine.Color MutedText => Pro ? C(0.50f) : C(0.42f);
        private static UnityEngine.Color SubText => Pro ? C(0.60f) : C(0.35f);
        private static UnityEngine.Color ErrorText => new(0.92f, 0.32f, 0.32f);
        private static UnityEngine.Color SuccessText => new(0.28f, 0.78f, 0.45f);
        private static UnityEngine.Color WarningText => new(0.95f, 0.75f, 0.15f);
        private static UnityEngine.Color BadgeBg => Pro ? new(0.30f, 0.55f, 0.92f, 0.18f) : new(0.30f, 0.55f, 0.92f, 0.12f);

        // ─── Import state ────────────────────────────────
        private string _tokenInput = "";
        private bool _tokenVisible;
        private bool _isImporting;
        private string _progressMessage = "";
        private float _progressFraction;

        // Section/depth cache
        private string[] _cachedSectionNames;
        private int _selectedSectionIndex;
        private int _syncDepth;

        // ─── Preview state ───────────────────────────────
        private List<FramePreviewEntry> _previewFrames;
        private UnityEngine.Networking.UnityWebRequest _previewRequest;
        private bool _previewReady;
        private Vector2 _previewScrollPos;

        // ─── Settings state ──────────────────────────────
        private SerializedObject _serializedSettings;
        private UnityFigmaBridgeSettings _settings;
        private Vector2 _pageScrollPos;

        // ─── Log state ───────────────────────────────────
        private readonly List<LogEntry> _logEntries = new();
        private Vector2 _logScrollPos;

        // ─── Lifecycle ───────────────────────────────────

        [MenuItem("Figma Bridge/Open Window")]
        public static void Open()
        {
            var w = GetWindow<FigmaBridgeEditorWindow>("Figma Bridge");
            w.minSize = new Vector2(480, 440);
            w.Show();
        }

        private void OnEnable()
        {
            _tokenInput = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            _isImporting = UnityFigmaBridgeImporter.IsImporting;
            EnsureSettingsLoaded();
            if (_settings != null)
            {
                _syncDepth = _settings.SyncDepth;
                // Auto-fetch sections on window open if token and URL are set
                if (!string.IsNullOrEmpty(_tokenInput) && !string.IsNullOrEmpty(_settings.FileId))
                    RefreshSections();
            }
            UnityFigmaBridgeImporter.OnProgressChanged += HandleProgress;
            UnityFigmaBridgeImporter.OnImportComplete += HandleComplete;
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckPendingSectionRequest;
            _sectionRequest?.Dispose();
            _sectionRequest = null;
            _previewRequest?.Dispose();
            _previewRequest = null;
            UnityFigmaBridgeImporter.OnProgressChanged -= HandleProgress;
            UnityFigmaBridgeImporter.OnImportComplete -= HandleComplete;
        }

        // ─── Main Layout ─────────────────────────────────

        private void OnGUI()
        {
            _isImporting = UnityFigmaBridgeImporter.IsImporting;
            DrawHeader();
            DrawTabBar();

            // Check pending requests each frame
            CheckPendingSectionRequest();
            CheckPendingPreviewRequest();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(4);

            switch (_tabIndex)
            {
                case 0: DrawImportTab(); break;
                case 1: DrawSettingsTab(); break;
                case 2: DrawLogTab(); break;
            }

            GUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        // ─── Header ──────────────────────────────────────

        private void DrawHeader()
        {
            // Main header bar
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, HeaderBg);
                // Subtle bottom accent line
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Accent);
            }

            // Status indicator
            var statusColor = _isImporting ? WarningText
                : !string.IsNullOrEmpty(_tokenInput) ? SuccessText : MutedText;
            if (Event.current.type == EventType.Repaint)
            {
                var dotCenter = new Vector2(rect.x + 16, rect.y + rect.height / 2);
                // Simple circle via overlapping rects
                EditorGUI.DrawRect(new Rect(dotCenter.x - 3, dotCenter.y - 4, 6, 8), statusColor);
                EditorGUI.DrawRect(new Rect(dotCenter.x - 4, dotCenter.y - 3, 8, 6), statusColor);
            }

            // Title
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Pro ? UnityEngine.Color.white : C(0.12f) },
            };
            GUI.Label(rect, "Figma Bridge", titleStyle);

            // Version badge (right side)
            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 12, 0, 0),
                normal = { textColor = MutedText },
            };
            GUI.Label(rect, "v1.0", badgeStyle);
        }

        // ─── Tab Bar ─────────────────────────────────────

        private void DrawTabBar()
        {
            var barRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, TabBarBg);

            var tabW = barRect.width / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tabRect = new Rect(barRect.x + i * tabW, barRect.y, tabW, barRect.height);
                bool active = _tabIndex == i;

                if (Event.current.type == EventType.Repaint)
                {
                    if (active)
                    {
                        EditorGUI.DrawRect(tabRect, ActiveTabBg);
                        // Accent underline with subtle glow
                        EditorGUI.DrawRect(new Rect(tabRect.x + 8, tabRect.yMax - 3, tabRect.width - 16, 3), Accent);
                        EditorGUI.DrawRect(new Rect(tabRect.x + 4, tabRect.yMax - 1, tabRect.width - 8, 1), AccentGlow);
                    }
                }

                var style = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = active ? (Pro ? UnityEngine.Color.white : C(0.1f)) : MutedText },
                };

                if (GUI.Button(tabRect, Tabs[i], style))
                    _tabIndex = i;

                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }
        }

        // ─── Import Tab ──────────────────────────────────

        private void DrawImportTab()
        {
            // ── Authentication card ──
            BeginCard("Authentication");

            var hasToken = !string.IsNullOrEmpty(_tokenInput);
            var storedToken = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            var tokenChanged = _tokenInput != storedToken;

            // Status row — with Save button inline when token changed
            EditorGUILayout.BeginHorizontal();
            DrawKeyValueInline("Status", hasToken ? "Token configured" : "No token set",
                hasToken ? SuccessText : ErrorText);
            if (tokenChanged)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = SuccessText;
                if (GUILayout.Button("Save", GUILayout.Width(52), GUILayout.Height(18)))
                {
                    EditorPrefs.SetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, _tokenInput);
                    Debug.Log("Personal access token updated");
                    AppendLog("Token updated");
                    RefreshSections();
                }
                GUI.backgroundColor = prevBg;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (_tokenVisible)
                _tokenInput = EditorGUILayout.TextField(_tokenInput);
            else
                _tokenInput = EditorGUILayout.PasswordField(_tokenInput);
            if (GUILayout.Button(_tokenVisible ? "Hide" : "Show", GUILayout.Width(48), GUILayout.Height(18)))
                _tokenVisible = !_tokenVisible;
            EditorGUILayout.EndHorizontal();

            EndCard();

            // ── Document card ──
            BeginCard("Document");

            EnsureSettingsLoaded();
            if (_settings != null)
            {
                var url = _settings.DocumentUrl ?? "";
                var info = FigmaApiUtils.ParseFigmaUrl(url);

                DrawKeyValue("URL", string.IsNullOrEmpty(url) ? "(not set)" : TruncateUrl(url));

                if (info.IsValid)
                {
                    DrawKeyValue("File ID", info.FileId, Accent);
                    if (info.HasNodeId)
                        DrawKeyValue("Node ID", info.NodeId, Accent);
                }
                else if (!string.IsNullOrEmpty(url))
                {
                    GUILayout.Space(2);
                    DrawBadge("Invalid URL", ErrorText);
                }
            }
            else
            {
                var hint = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = MutedText }, wordWrap = true, fontSize = 11
                };
                GUILayout.Label("No settings file found. Go to Settings tab to create one.", hint);
            }

            EndCard();

            // ── Quick Sync Options card ──
            BeginCard("Sync Options");

            // Section dropdown
            var sectionDesc = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = SubText }, wordWrap = true
            };
            GUILayout.Label("Filter by section - only import frames within the selected section.", sectionDesc);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            var sectionLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = MutedText } };
            EditorGUILayout.LabelField("Section", sectionLabel, GUILayout.Width(70));

            if (_cachedSectionNames == null || _cachedSectionNames.Length == 0)
                _cachedSectionNames = new[] { "All Sections" };

            _selectedSectionIndex = EditorGUILayout.Popup(_selectedSectionIndex, _cachedSectionNames);
            if (_settings != null)
            {
                _settings.SelectedSection = _selectedSectionIndex == 0 ? "" : _cachedSectionNames[_selectedSectionIndex];
                EditorUtility.SetDirty(_settings);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(60), GUILayout.Height(18)))
                RefreshSections();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Layer depth
            GUILayout.Label("Layer depth - how deep to traverse inside each frame.", sectionDesc);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Depth", sectionLabel, GUILayout.Width(70));

            var depthLabels = new[] { "Full", "1", "2", "3", "4", "5" };
            var depthValues = new[] { 0, 1, 2, 3, 4, 5 };
            var depthIdx = Array.IndexOf(depthValues, Mathf.Clamp(_syncDepth, 0, 5));
            if (depthIdx < 0) depthIdx = 0;

            depthIdx = EditorGUILayout.Popup(depthIdx, depthLabels);
            _syncDepth = depthValues[depthIdx];
            if (_settings != null)
            {
                _settings.SyncDepth = _syncDepth;
                EditorUtility.SetDirty(_settings);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            var depthHint = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, wordWrap = true, fontSize = 10
            };
            GUILayout.Label(
                _syncDepth == 0
                    ? "Full: import all nested layers."
                    : $"Depth {_syncDepth}: import top {_syncDepth} level(s). Deeper objects render as flat images.",
                depthHint);

            EndCard();

            GUILayout.Space(4);

            // ── Preview / Sync flow ──
            if (!_previewReady)
            {
                // Step 1: Preview button
                Indent(() =>
                {
                    var fetching = _previewRequest != null && !_previewRequest.isDone;
                    using (new EditorGUI.DisabledGroupScope(fetching || _isImporting))
                    {
                        DrawAccentButton(fetching ? "Loading Preview..." : "Preview Document", () => FetchPreview());
                    }

                    if (fetching)
                    {
                        GUILayout.Space(6);
                        DrawProgressBar(-1, "Fetching document structure...");
                    }
                });
            }
            else
            {
                // Step 2: Frame selection list
                DrawFrameSelectionCard();

                GUILayout.Space(4);

                // Step 3: Sync selected
                Indent(() =>
                {
                    var selectedCount = _previewFrames?.Count(f => f.Selected) ?? 0;

                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledGroupScope(_isImporting || selectedCount == 0))
                    {
                        var syncLabel = _isImporting ? "Syncing..." : $"Sync {selectedCount} Frame(s)";
                        var prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = Accent;
                        if (GUILayout.Button(syncLabel, GUILayout.Height(36)))
                            BeginImport();
                        GUI.backgroundColor = prevBg;
                    }
                    if (GUILayout.Button("Back", GUILayout.Width(60), GUILayout.Height(36)))
                    {
                        _previewReady = false;
                        _previewFrames = null;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_isImporting)
                    {
                        GUILayout.Space(8);
                        DrawProgressBar(_progressFraction, _progressMessage);
                    }
                });
            }

            GUILayout.FlexibleSpace();

            // Footer actions
            Indent(() =>
            {
                DrawSeparator();
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (DrawSmallButton("Select Settings"))
                    if (_settings != null) Selection.activeObject = _settings;
                GUILayout.Space(4);
                if (DrawSmallButton("Project Settings"))
                    SettingsService.OpenProjectSettings("Project/Unity Figma Bridge");
                EditorGUILayout.EndHorizontal();
            });
        }

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

            BeginCard("Import Settings");
            SettingsInspectorDrawer.DrawSettings(_settings, _serializedSettings);
            EndCard();

            if (_settings.OnlyImportSelectedPages && _settings.PageDataList.Count > 0)
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
                var hint = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = MutedText }, wordWrap = true
                };
                GUILayout.Label("No sections loaded. Click Refresh to fetch from Figma.", hint);
                GUILayout.Space(4);

                using (new EditorGUI.DisabledGroupScope(
                    string.IsNullOrEmpty(_tokenInput) || _settings == null || string.IsNullOrEmpty(_settings.FileId)))
                {
                    if (GUILayout.Button("Refresh Sections", GUILayout.Height(24)))
                        RefreshSections();
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

                    var nameStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 11,
                        fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                        padding = new RectOffset(10, 4, 0, 0),
                        normal = { textColor = isSelected ? Accent : (Pro ? C(0.78f) : C(0.2f)) },
                    };
                    GUI.Label(rRect, sectionName, nameStyle);

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
            var icon = new GUIStyle(EditorStyles.label)
            {
                fontSize = 32, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = MutedText },
            };
            GUILayout.Label("?", icon); // placeholder icon
            GUILayout.Space(8);

            var center = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 13,
                normal = { textColor = Pro ? C(0.7f) : C(0.3f) },
            };
            GUILayout.Label("No settings asset found", center);
            GUILayout.Space(12);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = Accent;
            if (GUILayout.Button("Create Settings Asset", GUILayout.Height(30), GUILayout.Width(200)))
            {
                _settings = UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
                _serializedSettings = new SerializedObject(_settings);
                AppendLog("Settings asset created");
                EditorGUIUtility.PingObject(_settings);
            }
            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            var hint = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            GUILayout.Label("Or drag a settings asset into the Config field above.", hint);
        }

        // ─── Log Tab ─────────────────────────────────────

        private void DrawLogTab()
        {
            // Header with count badge + clear
            Indent(() =>
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Import Log", EditorStyles.boldLabel);
                if (_logEntries.Count > 0)
                    DrawBadge($"{_logEntries.Count}", Accent);
                GUILayout.FlexibleSpace();
                if (_logEntries.Count > 0 && DrawSmallButton("Clear"))
                    _logEntries.Clear();
                EditorGUILayout.EndHorizontal();
            });

            DrawSeparator();
            GUILayout.Space(4);

            if (_logEntries.Count == 0)
            {
                GUILayout.Space(40);
                var empty = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter, wordWrap = true,
                    fontSize = 11, normal = { textColor = MutedText },
                };
                GUILayout.Label("No log entries yet.\nSync a document to see activity here.", empty);
                return;
            }

            // Log rows
            const float rowH = 22;
            var evenBg = Pro ? C(0.22f) : C(0.94f);
            var oddBg = Pro ? C(0.20f) : C(0.92f);

            Indent(() =>
            {
                for (int i = 0; i < _logEntries.Count; i++)
                {
                    var entry = _logEntries[i];
                    var rRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));

                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(rRect, i % 2 == 0 ? evenBg : oddBg);
                        // Left color indicator
                        var indicatorColor = entry.IsError ? ErrorText : SuccessText;
                        EditorGUI.DrawRect(new Rect(rRect.x, rRect.y + 4, 3, rowH - 8), indicatorColor);
                    }

                    // Timestamp
                    var tsStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = MutedText },
                        padding = new RectOffset(8, 4, 0, 0),
                    };
                    GUI.Label(new Rect(rRect.x, rRect.y, 62, rowH), entry.Timestamp.ToString("HH:mm:ss"), tsStyle);

                    // Message
                    var msgColor = entry.IsError ? ErrorText : (Pro ? C(0.80f) : C(0.18f));
                    var msgStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = msgColor },
                        clipping = TextClipping.Clip,
                        padding = new RectOffset(2, 4, 0, 0),
                    };
                    GUI.Label(new Rect(rRect.x + 64, rRect.y, rRect.width - 64, rowH), entry.Message, msgStyle);
                }
            });
        }

        // ─── Frame Preview ────────────────────────────────

        private void FetchPreview()
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.FileId)) return;
            var token = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            if (string.IsNullOrEmpty(token)) return;

            AppendLog("Fetching document structure...");

            // depth=3: document → pages → sections → frames
            var url = $"https://api.figma.com/v1/files/{_settings.FileId}?depth=3";
            _previewRequest = UnityEngine.Networking.UnityWebRequest.Get(url);
            _previewRequest.SetRequestHeader("X-Figma-Token", token);
            _previewRequest.SendWebRequest();
            Repaint();
        }

        private void CheckPendingPreviewRequest()
        {
            if (_previewRequest == null) return;
            if (!_previewRequest.isDone)
            {
                Repaint();
                return;
            }

            if (_previewRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AppendLog($"Preview failed ({_previewRequest.responseCode}): {_previewRequest.error}", true);
                _previewRequest.Dispose();
                _previewRequest = null;
                Repaint();
                return;
            }

            try
            {
                var json = _previewRequest.downloadHandler.text;
                var figmaFile = Newtonsoft.Json.JsonConvert.DeserializeObject<FigmaFile>(json,
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Ignore,
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    });

                BuildPreviewList(figmaFile);
                _previewReady = true;

                var total = _previewFrames.Count;
                var existing = _previewFrames.Count(f => f.ExistsOnDisk);
                AppendLog($"Found {total} frame(s), {existing} already imported");
            }
            catch (Exception e)
            {
                AppendLog($"Preview parse error: {e.Message}", true);
            }
            finally
            {
                _previewRequest.Dispose();
                _previewRequest = null;
            }
            Repaint();
        }

        private void BuildPreviewList(FigmaFile figmaFile)
        {
            _previewFrames = new List<FramePreviewEntry>();
            if (figmaFile?.document?.children == null) return;

            var sectionFilter = _settings != null ? _settings.SelectedSection : "";
            var outputRoot = UnityFigmaBridge.Editor.Utils.FigmaPaths.FigmaScreenPrefabFolder;

            foreach (var page in figmaFile.document.children)
            {
                if (page.children == null) continue;

                foreach (var child in page.children)
                {
                    if (child.type == NodeType.SECTION)
                    {
                        // Skip sections that don't match filter
                        if (!string.IsNullOrEmpty(sectionFilter) && child.name != sectionFilter)
                            continue;

                        // Frames inside section
                        if (child.children != null)
                        {
                            foreach (var frame in child.children)
                            {
                                if (frame.type == NodeType.FRAME)
                                    AddFrameEntry(frame, page.name, child.name, outputRoot);
                            }
                        }
                    }
                    else if (child.type == NodeType.FRAME)
                    {
                        // Top-level frame on page (no section)
                        if (string.IsNullOrEmpty(sectionFilter))
                            AddFrameEntry(child, page.name, null, outputRoot);
                    }
                }
            }
        }

        private void AddFrameEntry(Node frame, string pageName, string sectionName, string outputRoot)
        {
            var safeName = UnityFigmaBridge.Editor.Utils.FigmaPaths.MakeValidFileName(frame.name.Trim());
            var prefabPath = $"{outputRoot}/{safeName}.prefab";
            var exists = System.IO.File.Exists(prefabPath);

            _previewFrames.Add(new FramePreviewEntry
            {
                NodeId = frame.id,
                Name = frame.name,
                PageName = pageName,
                SectionName = sectionName,
                PrefabPath = prefabPath,
                ExistsOnDisk = exists,
                Selected = !exists, // Auto-uncheck if already exists
            });
        }

        private void DrawFrameSelectionCard()
        {
            BeginCard("Select Frames to Import");

            // Select all / none
            EditorGUILayout.BeginHorizontal();
            var totalCount = _previewFrames?.Count ?? 0;
            var selectedCount = _previewFrames?.Count(f => f.Selected) ?? 0;
            DrawBadge($"{selectedCount}/{totalCount} selected", Accent);
            GUILayout.FlexibleSpace();
            if (DrawSmallButton("All"))
                _previewFrames?.ForEach(f => f.Selected = true);
            if (DrawSmallButton("None"))
                _previewFrames?.ForEach(f => f.Selected = false);
            if (DrawSmallButton("New Only"))
                _previewFrames?.ForEach(f => f.Selected = !f.ExistsOnDisk);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (_previewFrames == null || _previewFrames.Count == 0)
            {
                var empty = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = MutedText } };
                GUILayout.Label("No frames found.", empty);
                EndCard();
                return;
            }

            // Frame list
            const float rowH = 26;
            var evenBg = Pro ? C(0.22f) : C(0.94f);
            var oddBg = Pro ? C(0.20f) : C(0.92f);
            var warningBg = Pro
                ? new UnityEngine.Color(0.95f, 0.75f, 0.15f, 0.12f)
                : new UnityEngine.Color(0.95f, 0.75f, 0.15f, 0.08f);

            _previewScrollPos = EditorGUILayout.BeginScrollView(_previewScrollPos,
                GUILayout.MaxHeight(300));

            string lastSection = null;
            for (int i = 0; i < _previewFrames.Count; i++)
            {
                var entry = _previewFrames[i];

                // Section header
                var sectionLabel = entry.SectionName ?? "(No Section)";
                if (sectionLabel != lastSection)
                {
                    lastSection = sectionLabel;
                    GUILayout.Space(i > 0 ? 6 : 0);
                    var hdrStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = MutedText },
                        padding = new RectOffset(4, 0, 0, 0),
                    };
                    GUILayout.Label($"  {entry.PageName} / {sectionLabel}", hdrStyle);
                }

                // Row
                var rRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    var bg = (entry.Selected && entry.ExistsOnDisk) ? warningBg
                        : (i % 2 == 0 ? evenBg : oddBg);
                    EditorGUI.DrawRect(rRect, bg);

                    // Left color bar
                    var barColor = entry.ExistsOnDisk
                        ? (entry.Selected ? WarningText : MutedText)
                        : (entry.Selected ? SuccessText : MutedText);
                    EditorGUI.DrawRect(new Rect(rRect.x, rRect.y + 3, 3, rowH - 6), barColor);
                }

                // Checkbox
                var checkRect = new Rect(rRect.x + 8, rRect.y + 4, 18, 18);
                entry.Selected = EditorGUI.Toggle(checkRect, entry.Selected);

                // Frame name
                var nameStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    padding = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = entry.Selected ? (Pro ? C(0.88f) : C(0.12f)) : MutedText },
                };
                var nameRect = new Rect(rRect.x + 30, rRect.y, rRect.width - 120, rowH);
                GUI.Label(nameRect, entry.Name, nameStyle);

                // Status badge (right)
                if (entry.ExistsOnDisk)
                {
                    var badgeRect = new Rect(rRect.xMax - 90, rRect.y + 5, 80, 16);
                    var badgeColor = entry.Selected ? WarningText : MutedText;
                    if (Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(badgeRect, new UnityEngine.Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.15f));
                    var bs = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 9,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = badgeColor },
                    };
                    GUI.Label(badgeRect, entry.Selected ? "OVERWRITE" : "EXISTS", bs);
                }
            }

            EditorGUILayout.EndScrollView();
            EndCard();
        }

        // ─── Section Refresh ─────────────────────────────

        private UnityEngine.Networking.UnityWebRequest _sectionRequest;

        private void RefreshSections()
        {
            if (_sectionRequest != null && !_sectionRequest.isDone)
            {
                AppendLog("Already fetching, please wait...");
                Repaint();
                return;
            }

            if (_settings == null)
            {
                AppendLog("No settings file.", true);
                Repaint();
                return;
            }

            var fileId = _settings.FileId;
            if (string.IsNullOrEmpty(fileId))
            {
                AppendLog($"Invalid URL: {_settings.DocumentUrl ?? "(empty)"}", true);
                Repaint();
                return;
            }

            var token = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            if (string.IsNullOrEmpty(token))
            {
                AppendLog("No token set.", true);
                Repaint();
                return;
            }

            AppendLog($"Fetching sections (File: {fileId})...");

            // Use depth=2 for lightweight fetch — only need pages and their direct children
            var url = $"https://api.figma.com/v1/files/{fileId}?depth=2";
            _sectionRequest = UnityEngine.Networking.UnityWebRequest.Get(url);
            _sectionRequest.SetRequestHeader("X-Figma-Token", token);
            _sectionRequest.SendWebRequest();

            EditorApplication.update += CheckPendingSectionRequest;
            Repaint();
        }

        private void CheckPendingSectionRequest()
        {
            if (_sectionRequest == null)
            {
                EditorApplication.update -= CheckPendingSectionRequest;
                return;
            }

            if (!_sectionRequest.isDone)
            {
                Repaint(); // Keep repainting so OnGUI checks again
                return;
            }

            EditorApplication.update -= CheckPendingSectionRequest;

            if (_sectionRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AppendLog($"Fetch failed ({_sectionRequest.responseCode}): {_sectionRequest.error}", true);
                _sectionRequest.Dispose();
                _sectionRequest = null;
                Repaint();
                return;
            }

            try
            {
                var json = _sectionRequest.downloadHandler.text;
                var figmaFile = Newtonsoft.Json.JsonConvert.DeserializeObject<FigmaFile>(json,
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Ignore,
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    });

                if (figmaFile?.document?.children == null)
                {
                    AppendLog("Document has no pages.", true);
                    return;
                }

                var sections = new List<string> { "All Sections" };
                foreach (var page in figmaFile.document.children)
                {
                    if (page.children == null) continue;
                    foreach (var child in page.children)
                    {
                        if (child.type == NodeType.SECTION)
                            sections.Add(child.name);
                    }
                }

                _cachedSectionNames = sections.ToArray();

                if (!string.IsNullOrEmpty(_settings?.SelectedSection))
                {
                    var idx = Array.IndexOf(_cachedSectionNames, _settings.SelectedSection);
                    _selectedSectionIndex = idx >= 0 ? idx : 0;
                }
                else
                {
                    _selectedSectionIndex = 0;
                }

                AppendLog($"Found {sections.Count - 1} section(s)");
            }
            catch (Exception e)
            {
                AppendLog($"Parse error: {e.Message}", true);
            }
            finally
            {
                _sectionRequest.Dispose();
                _sectionRequest = null;
            }

            Repaint();
        }

        // ─── Event Handlers ──────────────────────────────

        private void HandleProgress(string message, float fraction)
        {
            _progressMessage = message;
            _progressFraction = fraction;
            AppendLog(message);
            Repaint();
        }

        private void HandleComplete(bool success, string error)
        {
            _isImporting = false;
            AppendLog(success ? "Import completed successfully" : $"Import failed: {error}", !success);
            Repaint();
        }

        // ─── Helpers ─────────────────────────────────────

        private void EnsureSettingsLoaded()
        {
            if (_settings != null) return;
            _settings = UnityFigmaBridgeImporter.Settings;
            if (_settings != null)
            {
                _serializedSettings = new SerializedObject(_settings);
                _syncDepth = _settings.SyncDepth;
            }
        }

        private async void BeginImport()
        {
            _logEntries.Clear();
            AppendLog("Starting import...");
            await UnityFigmaBridgeImporter.StartSyncAsync();
        }

        private void AppendLog(string message, bool isError = false)
        {
            _logEntries.Add(new LogEntry(message, isError));
            _logScrollPos = new Vector2(0, float.MaxValue);
        }

        private static string TruncateUrl(string url, int maxLen = 55)
        {
            if (url.Length <= maxLen) return url;
            return url.Substring(0, maxLen - 3) + "...";
        }

        // ─── UI Primitives ───────────────────────────────

        private static void BeginCard(string title)
        {
            Indent(() => { }); // spacer
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            // Card background start - we draw it as a colored rect behind content
            var cardStart = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
            // We'll store position for EndCard to draw the full rect

            // Title row
            GUILayout.BeginHorizontal();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Pro ? C(0.85f) : C(0.18f) },
            };
            GUILayout.Label(title, titleStyle);
            GUILayout.EndHorizontal();

            // Separator
            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, SepColor);

            GUILayout.Space(6);
        }

        private static void EndCard()
        {
            GUILayout.Space(6);
            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private static void DrawKeyValue(string label, string value, UnityEngine.Color? valueColor = null)
        {
            EditorGUILayout.BeginHorizontal();
            DrawKeyValueInline(label, value, valueColor);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws label + value without wrapping in a horizontal group — use inside an existing BeginHorizontal.
        /// </summary>
        private static void DrawKeyValueInline(string label, string value, UnityEngine.Color? valueColor = null)
        {
            const float labelW = 70f;
            const float rowH = 18f;
            var lStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = MutedText },
                fontSize = 11,
                padding = new RectOffset(0, 0, 1, 1),
            };
            EditorGUILayout.LabelField(label, lStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
            var vStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 11,
                padding = new RectOffset(0, 0, 1, 1),
                normal = { textColor = valueColor ?? (Pro ? C(0.80f) : C(0.18f)) },
            };
            EditorGUILayout.LabelField(value, vStyle, GUILayout.Height(rowH));
        }

        private static void DrawBadge(string text, UnityEngine.Color color)
        {
            var content = new GUIContent(text);
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color },
                padding = new RectOffset(4, 4, 1, 1),
            };
            var size = style.CalcSize(content);
            var rect = GUILayoutUtility.GetRect(size.x + 8, 16);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new UnityEngine.Color(color.r, color.g, color.b, 0.18f));
            GUI.Label(rect, text, style);
        }

        private void DrawProgressBar(float fraction, string label)
        {
            var barRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                // Background
                EditorGUI.DrawRect(barRect, Pro ? C(0.18f) : C(0.82f));

                if (fraction < 0 || (fraction <= 0.001f && _isImporting))
                {
                    // Indeterminate: animated sliding bar
                    var t = (float)((EditorApplication.timeSinceStartup * 0.8) % 1.0);
                    var slideW = barRect.width * 0.3f;
                    var slideX = barRect.x + (barRect.width + slideW) * t - slideW;
                    var clippedX = Mathf.Max(slideX, barRect.x);
                    var clippedR = Mathf.Min(slideX + slideW, barRect.xMax);
                    if (clippedR > clippedX)
                        EditorGUI.DrawRect(new Rect(clippedX, barRect.y, clippedR - clippedX, barRect.height), Accent);
                }
                else
                {
                    // Determinate fill
                    var fillRect = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(fraction), barRect.height);
                    EditorGUI.DrawRect(fillRect, Accent);
                    if (fraction > 0.01f && fraction < 0.99f)
                    {
                        var glowRect = new Rect(fillRect.xMax - 2, fillRect.y, 4, fillRect.height);
                        EditorGUI.DrawRect(glowRect, new UnityEngine.Color(1f, 1f, 1f, 0.2f));
                    }
                }
            }

            var barLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UnityEngine.Color.white },
            };
            GUI.Label(barRect, label, barLabel);

            // Keep repainting for animation
            if (_isImporting)
                Repaint();
        }

        private static void DrawSeparator()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, SepColor);
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
        }

        private static void DrawAccentButton(string text, Action onClick)
        {
            var btnRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            var hover = btnRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
            {
                var color = hover ? new UnityEngine.Color(0.35f, 0.62f, 0.98f) : Accent;
                EditorGUI.DrawRect(btnRect, color);
            }

            var label = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UnityEngine.Color.white },
            };
            GUI.Label(btnRect, text, label);

            if (Event.current.type == EventType.MouseDown && btnRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                onClick?.Invoke();
            }
            EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
        }

        private static bool DrawSmallButton(string text)
        {
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 20, fontSize = 10,
                padding = new RectOffset(8, 8, 2, 2),
            };
            return GUILayout.Button(text, style);
        }

        private static void Indent(Action content)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();
            content();
            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
        }
    }

    internal class FramePreviewEntry
    {
        public string NodeId;
        public string Name;
        public string PageName;
        public string SectionName;
        public string PrefabPath;
        public bool ExistsOnDisk;
        public bool Selected;
    }

    internal readonly struct LogEntry
    {
        public readonly DateTime Timestamp;
        public readonly string Message;
        public readonly bool IsError;

        public LogEntry(string message, bool isError)
        {
            Timestamp = DateTime.Now;
            Message = message;
            IsError = isError;
        }
    }
}
