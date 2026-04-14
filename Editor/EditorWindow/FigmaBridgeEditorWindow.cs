using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    public sealed partial class FigmaBridgeEditorWindow : EditorWindow
    {
        // ─── Tabs ────────────────────────────────────────
        private static readonly string[] Tabs = { "Import", "Build", "Settings", "Log" };
        private int _tabIndex;
        private Vector2 _scrollPos;

        // ─── Theme ───────────────────────────────────────
        private static readonly UnityEngine.Color Accent = new(0.24f, 0.44f, 0.74f);
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

        // ─── Button colors ───────────────────────────────
        private static readonly UnityEngine.Color AccentHover = new(0.28f, 0.50f, 0.80f);
        private static readonly UnityEngine.Color BtnGray = new(0.35f, 0.35f, 0.35f);
        private static readonly UnityEngine.Color BtnGrayHover = new(0.45f, 0.45f, 0.45f);
        private static readonly UnityEngine.Color BtnRed = new(0.45f, 0.18f, 0.18f);
        private static readonly UnityEngine.Color BtnRedHover = new(0.55f, 0.25f, 0.25f);
        private static readonly UnityEngine.Color BtnGreen = new(0.18f, 0.40f, 0.18f);
        private static readonly UnityEngine.Color BtnGreenHover = new(0.25f, 0.50f, 0.25f);
        private static readonly UnityEngine.Color BtnOrange = new(0.65f, 0.42f, 0.15f);
        private static readonly UnityEngine.Color BtnOrangeHover = new(0.75f, 0.50f, 0.22f);

        // ─── Cached Styles ──────────────────────────────
        // Rebuilt once when Pro skin changes - eliminates ~37 per-frame GUIStyle allocations.
        private static bool s_StylesBuiltForPro;
        private static bool s_StylesInitialized;

        private static GUIStyle s_HeaderTitle;
        private static GUIStyle s_VersionBadge;
        private static GUIStyle s_CardTitle;
        private static GUIStyle s_KvLabel;
        private static GUIStyle s_KvValue;
        private static GUIStyle s_BadgeLabel;
        private static GUIStyle s_MiniBtn;
        private static GUIStyle s_MiniHint;
        private static GUIStyle s_MiniHintWrap;
        private static GUIStyle s_SubDesc;
        private static GUIStyle s_SectionLabelMuted;
        private static GUIStyle s_RowName;
        private static GUIStyle s_RowSync;
        private static GUIStyle s_RowBadge;
        private static GUIStyle s_RowBtnLabel;
        private static GUIStyle s_SectionHeader;
        private static GUIStyle s_EmptyHint;
        private static GUIStyle s_EmptyIcon;
        private static GUIStyle s_EmptyCenter;
        private static GUIStyle s_EmptyGrey;
        private static GUIStyle s_LogTimestamp;
        private static GUIStyle s_LogMessage;
        private static GUIStyle s_FrameName;
        private static GUIStyle s_FrameHdr;
        private static GUIStyle s_FrameBadge;
        private static GUIStyle s_SectionName;
        private static GUIStyle s_ProgressLabel;
        private static GUIStyle s_BtnLabel;
        private static GUIStyle s_MonoTextField;
        private static GUIStyle s_VersionToggle;
        private static GUIStyle s_SettingsCfgLabel;

        private static void EnsureStyles()
        {
            if (s_StylesInitialized && s_StylesBuiltForPro == Pro) return;
            s_StylesInitialized = true;
            s_StylesBuiltForPro = Pro;

            s_HeaderTitle = new GUIStyle
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Pro ? UnityEngine.Color.white : C(0.12f) },
                hover = { textColor = Pro ? UnityEngine.Color.white : C(0.12f) },
            };
            s_VersionBadge = new GUIStyle
            {
                fontSize = 9, alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 12, 0, 0),
                normal = { textColor = MutedText }, hover = { textColor = MutedText },
            };
            s_CardTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Pro ? C(0.85f) : C(0.18f) },
            };
            s_KvLabel = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = MutedText }, fontSize = 11,
                padding = new RectOffset(0, 0, 1, 1),
            };
            s_KvValue = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true, fontSize = 11,
                padding = new RectOffset(0, 0, 1, 1),
                normal = { textColor = Pro ? C(0.80f) : C(0.18f) },
            };
            s_BadgeLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 1, 1),
            };
            s_MiniBtn = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 18, fontSize = 10,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = Pro ? C(0.85f) : C(0.18f) },
            };
            s_MiniHint = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, wordWrap = true, fontSize = 10,
            };
            s_MiniHintWrap = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, wordWrap = true,
            };
            s_SubDesc = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = SubText }, wordWrap = true,
            };
            s_SectionLabelMuted = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = MutedText },
            };
            s_RowName = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, padding = new RectOffset(14, 0, 0, 0),
                normal = { textColor = Pro ? C(0.82f) : C(0.16f) },
            };
            s_RowSync = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, alignment = TextAnchor.MiddleRight,
                normal = { textColor = MutedText },
            };
            s_RowBadge = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = MutedText },
            };
            s_RowBtnLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleCenter,
            };
            s_SectionHeader = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold, fontSize = 11,
                normal = { textColor = Accent },
                padding = new RectOffset(10, 0, 0, 0),
            };
            s_EmptyHint = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter, wordWrap = true,
                fontSize = 11, normal = { textColor = MutedText },
            };
            s_EmptyIcon = new GUIStyle(EditorStyles.label)
            {
                fontSize = 32, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = MutedText },
            };
            s_EmptyCenter = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 13,
                normal = { textColor = Pro ? C(0.7f) : C(0.3f) },
            };
            s_EmptyGrey = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            s_LogTimestamp = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText },
                padding = new RectOffset(8, 4, 0, 0),
            };
            s_LogMessage = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
                padding = new RectOffset(2, 4, 0, 0),
            };
            s_FrameName = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, padding = new RectOffset(0, 0, 0, 0),
            };
            s_FrameHdr = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = MutedText },
                padding = new RectOffset(4, 0, 0, 0),
            };
            s_FrameBadge = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter,
            };
            s_SectionName = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                padding = new RectOffset(10, 4, 0, 0),
            };
            s_ProgressLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UnityEngine.Color.white },
            };
            s_BtnLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UnityEngine.Color.white },
            };
            s_MonoTextField = new GUIStyle(EditorStyles.textField);
            var monoFont = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font
                ?? Font.CreateDynamicFontFromOSFont("Consolas", 12);
            s_MonoTextField.font = monoFont;
            s_VersionToggle = new GUIStyle
            {
                fontSize = 9, alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 12, 0, 0),
                normal = { textColor = MutedText },
                hover  = { textColor = Pro ? UnityEngine.Color.white : C(0.1f) },
            };
            s_SettingsCfgLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText },
                alignment = TextAnchor.MiddleLeft,
            };
        }

        // ─── Import state ────────────────────────────────
        private string _tokenInput = "";
        private bool _tokenVisible;
        private bool _isImporting;
        private string _progressMessage = "";
        private float _progressFraction;

        // ─── Section/depth cache ─────────────────────────
        private string[] _cachedSectionNames;
        private int _selectedSectionIndex;
        private int _syncDepth;

        // ─── Preview state ───────────────────────────────
        private List<FramePreviewEntry> _previewFrames;
        private UnityEngine.Networking.UnityWebRequest _previewRequest;
        private Vector2 _previewScrollPos;

        // ─── Settings state ──────────────────────────────
        private SerializedObject _serializedSettings;
        private UnityFigmaBridgeSettings _settings;
        private Vector2 _pageScrollPos;
        private bool _showSettings;

        // ─── Build tab state ─────────────────────────────
        private List<BuildFrameEntry> _buildFrames;
        private Vector2 _buildScrollPos;
        private FigmaFile _cachedFigmaFile;
        private DateTime _cachedFigmaFileTime;
        private bool _isBuilding;

        // ─── Log state ───────────────────────────────────
        private readonly List<LogEntry> _logEntries = new();
        private Vector2 _logScrollPos;

        // ─── Lifecycle ───────────────────────────────────

        [MenuItem("Tools/Figma Bridge/Dashboard", priority = 100)]
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
            UnityFigmaBridgeImporter.OnBuildComplete += HandleBuildComplete;
        }

        private void OnDisable()
        {
            EditorApplication.update -= CheckPendingSectionRequest;
            EditorApplication.update -= CheckCacheRefreshRequest;
            _sectionRequest?.Dispose();
            _sectionRequest = null;
            _cacheRefreshRequest?.Dispose();
            _cacheRefreshRequest = null;
            _previewRequest?.Dispose();
            _previewRequest = null;
            UnityFigmaBridgeImporter.OnProgressChanged -= HandleProgress;
            UnityFigmaBridgeImporter.OnImportComplete -= HandleComplete;
            UnityFigmaBridgeImporter.OnBuildComplete -= HandleBuildComplete;
        }

        // ─── Main Layout ─────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
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
                case 1: DrawBuildTab(); break;
                case 2: DrawSettingsTab(); break;
                case 3: DrawLogTab(); break;
            }

            GUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        // ─── Header ──────────────────────────────────────

        private static string _cachedVersion;

        private static string PackageVersion
        {
            get
            {
                if (_cachedVersion != null) return _cachedVersion;
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(FigmaBridgeEditorWindow).Assembly);
                _cachedVersion = packageInfo != null ? $"v{packageInfo.version}" : "v?";
                return _cachedVersion;
            }
        }

        private void DrawHeader()
        {
            // ── Title bar (38px) ─────────────────────────
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, HeaderBg);

            // Status indicator dot
            var statusColor = _isImporting ? WarningText
                : !string.IsNullOrEmpty(_tokenInput) ? SuccessText : MutedText;
            if (Event.current.type == EventType.Repaint)
            {
                var dc = new Vector2(rect.x + 16, rect.y + rect.height / 2);
                EditorGUI.DrawRect(new Rect(dc.x - 3, dc.y - 4, 6, 8), statusColor);
                EditorGUI.DrawRect(new Rect(dc.x - 4, dc.y - 3, 8, 6), statusColor);
            }

            // Title
            GUI.Label(rect, "Figma Bridge", s_HeaderTitle);

            // Version + toggle arrow — right side, clickable
            var arrow = _showSettings ? "\u25BC" : "\u25B6";
            GUI.Label(rect, $"{PackageVersion}  {arrow}", s_VersionToggle);
            var toggleZone = new Rect(rect.xMax - 90, rect.y, 90, rect.height);
            EditorGUIUtility.AddCursorRect(toggleZone, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && toggleZone.Contains(Event.current.mousePosition))
            {
                _showSettings = !_showSettings;
                Event.current.Use();
                Repaint();
            }

            // ── Settings sub-bar (20px) — hidden by default ──
            Rect bottomRect = rect;
            if (_showSettings)
            {
                EnsureSettingsLoaded();
                var cfgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(cfgRect, HeaderBg);

                GUI.Label(new Rect(cfgRect.x + 12, cfgRect.y + 2, 52, 16), "Settings", s_SettingsCfgLabel);

                EditorGUI.BeginChangeCheck();
                _settings = (UnityFigmaBridgeSettings)EditorGUI.ObjectField(
                    new Rect(cfgRect.x + 66, cfgRect.y + 2, cfgRect.width - 78, 16),
                    _settings, typeof(UnityFigmaBridgeSettings), false);
                if (EditorGUI.EndChangeCheck())
                    _serializedSettings = null;

                bottomRect = cfgRect;
            }

            // Accent line — always at bottom of header block
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, bottomRect.yMax - 1, rect.width, 1), Accent);
        }

        // ─── Tab Bar ─────────────────────────────────────

        // Animated accent line state
        private float _tabLineX;
        private float _tabLineFromX;
        private bool _tabLineInitialized;
        private double _tabAnimStart;
        private const float TabAnimDuration = 0.2f;

        private void DrawTabBar()
        {
            var barRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, TabBarBg);

            var tabW = barRect.width / Tabs.Length;
            var targetX = barRect.x + _tabIndex * tabW;

            // First-frame: snap line to current tab with no animation
            if (!_tabLineInitialized)
            {
                _tabLineX = targetX;
                _tabLineFromX = targetX;
                _tabLineInitialized = true;
            }

            // ── Animated accent line ──
            var animating = false;
            if (Mathf.Abs(_tabLineX - targetX) > 0.5f)
            {
                var t = (float)((EditorApplication.timeSinceStartup - _tabAnimStart) / TabAnimDuration);
                t = Mathf.Clamp01(t);
                t = 1f - (1f - t) * (1f - t); // ease-out quad
                _tabLineX = Mathf.Lerp(_tabLineFromX, targetX, t);
                animating = t < 1f;
            }
            else
            {
                _tabLineX = targetX;
            }

            // ── Tab labels + backgrounds ──
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tabRect = new Rect(barRect.x + i * tabW, barRect.y, tabW, barRect.height);
                bool active = _tabIndex == i;

                // Background switches instantly
                if (Event.current.type == EventType.Repaint && active)
                    EditorGUI.DrawRect(tabRect, ActiveTabBg);

                var style = s_FrameName;
                style.fontSize = 11;
                style.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                style.alignment = TextAnchor.MiddleCenter;
                style.normal.textColor = active ? (Pro ? UnityEngine.Color.white : C(0.1f)) : MutedText;

                if (GUI.Button(tabRect, Tabs[i], style))
                {
                    // Capture current animated position as the start of the next animation
                    _tabLineFromX = _tabLineX;
                    _tabAnimStart = EditorApplication.timeSinceStartup;
                    _tabIndex = i;
                }

                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }

            // Draw the sliding accent line (after backgrounds so it's on top)
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(_tabLineX + 8, barRect.yMax - 3, tabW - 16, 3), Accent);
                EditorGUI.DrawRect(new Rect(_tabLineX + 4, barRect.yMax - 1, tabW - 8, 1), AccentGlow);
            }

            // Keep repainting during animation
            if (animating) Repaint();
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

            // Pass selected frame IDs to importer
            UnityFigmaBridgeImporter.SelectedFrameIds = _previewFrames?
                .Where(f => f.Selected)
                .Select(f => f.NodeId)
                .ToList() ?? new List<string>();

            var count = UnityFigmaBridgeImporter.SelectedFrameIds.Count;
            AppendLog($"Starting import ({count} frame(s) selected)...");
            await UnityFigmaBridgeImporter.StartSyncAsync();
        }

        private void AppendLog(string message, bool isError = false)
        {
            _logEntries.Insert(0, new LogEntry(message, isError));
            _logScrollPos = Vector2.zero;
        }

        private static string TruncateUrl(string url, int maxLen = 55)
        {
            if (url.Length <= maxLen) return url;
            return url.Substring(0, maxLen - 3) + "...";
        }

        // ─── UI Primitives ───────────────────────────────

        private static void BeginCard(string title) => BeginCard(title, false);

        private static void BeginCard(string title, bool showMiniProgress)
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
            GUILayout.Label(title, s_CardTitle);
            GUILayout.EndHorizontal();

            // Inline mini progress bar - drawn over the right side of the title row
            if (showMiniProgress)
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                var barH = 12f;
                var barW = lastRect.width * 0.5f;
                var miniBarRect = new Rect(lastRect.xMax - barW, lastRect.yMax - barH - 1, barW, barH);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(miniBarRect, Pro ? C(0.18f) : C(0.82f));
                    var t = (float)((EditorApplication.timeSinceStartup * 0.8) % 1.0);
                    var slideW = miniBarRect.width * 0.3f;
                    var slideX = miniBarRect.x + (miniBarRect.width + slideW) * t - slideW;
                    var clippedX = Mathf.Max(slideX, miniBarRect.x);
                    var clippedR = Mathf.Min(slideX + slideW, miniBarRect.xMax);
                    if (clippedR > clippedX)
                        EditorGUI.DrawRect(new Rect(clippedX, miniBarRect.y, clippedR - clippedX, miniBarRect.height), Accent);
                }
            }

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
        /// Draws label + value without wrapping in a horizontal group - use inside an existing BeginHorizontal.
        /// </summary>
        private static void DrawKeyValueInline(string label, string value, UnityEngine.Color? valueColor = null)
        {
            const float labelW = 70f;
            const float rowH = 18f;
            EditorGUILayout.LabelField(label, s_KvLabel, GUILayout.Width(labelW), GUILayout.Height(rowH));
            if (valueColor.HasValue)
                s_KvValue.normal.textColor = valueColor.Value;
            else
                s_KvValue.normal.textColor = Pro ? C(0.80f) : C(0.18f);
            EditorGUILayout.LabelField(value, s_KvValue, GUILayout.Height(rowH));
        }

        private static void DrawBadge(string text, UnityEngine.Color color)
        {
            var content = new GUIContent(text);
            s_BadgeLabel.normal.textColor = color;
            var size = s_BadgeLabel.CalcSize(content);
            var rect = GUILayoutUtility.GetRect(size.x + 8, 16);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new UnityEngine.Color(color.r, color.g, color.b, 0.18f));
            GUI.Label(rect, text, s_BadgeLabel);
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

            GUI.Label(barRect, label, s_ProgressLabel);

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
            => DrawColorButton(text, Accent, AccentHover, onClick);

        private static void DrawGrayButton(string text, Action onClick, params GUILayoutOption[] options)
            => DrawColorButton(text, BtnGray, BtnGrayHover, onClick, options);

        private static void DrawColorButton(string text, UnityEngine.Color color, UnityEngine.Color hoverColor, Action onClick, params GUILayoutOption[] options)
        {
            var btnRect = options.Length > 0
                ? GUILayoutUtility.GetRect(0, 36, options)
                : GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            var hover = btnRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(btnRect, hover ? hoverColor : color);

            GUI.Label(btnRect, text, s_BtnLabel);

            if (Event.current.type == EventType.MouseDown && btnRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                onClick?.Invoke();
            }
            EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
        }

        private static bool DrawSmallButton(string text)
        {
            return GUILayout.Button(text, s_MiniBtn);
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

    internal class BuildFrameEntry
    {
        public string NodeId;
        public string Name;
        public string PageName;
        public string SectionName;
        public string PrefabPath;
        public bool ExistsOnDisk;
        public string LastSyncedAt;
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
