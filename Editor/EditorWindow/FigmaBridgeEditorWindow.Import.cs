using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    public sealed partial class FigmaBridgeEditorWindow
    {
        // ─── Import Tab ──────────────────────────────────

        private void DrawImportTab()
        {
            // ── Authentication card ──
            BeginCard("Authentication");

            var hasToken = !string.IsNullOrEmpty(_tokenInput);
            var storedToken = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            var tokenChanged = _tokenInput != storedToken;

            // Status row - with Save button inline when token changed
            EditorGUILayout.BeginHorizontal();
            DrawKeyValueInline("Status", hasToken ? "Token configured" : "No token set",
                hasToken ? SuccessText : ErrorText);
            if (tokenChanged)
            {
                DrawColorButton("Cancel", BtnRed, BtnRedHover, () => _tokenInput = storedToken,
                    GUILayout.Width(54), GUILayout.Height(20));
                GUILayout.Space(3);
                DrawColorButton("Save", BtnGreen, BtnGreenHover, () =>
                {
                    EditorPrefs.SetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, _tokenInput);
                    Debug.Log("Personal access token updated");
                    AppendLog("Token updated");
                    RefreshSections();
                }, GUILayout.Width(54), GUILayout.Height(20));
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (_tokenVisible)
                _tokenInput = EditorGUILayout.TextField(_tokenInput);
            else
                _tokenInput = EditorGUILayout.PasswordField(_tokenInput, s_MonoTextField);
            var showStyle = s_MiniBtn;
            if (GUILayout.Button(_tokenVisible ? "Hide" : "Show", showStyle, GUILayout.Width(44)))
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
                var hint = s_MiniHintWrap;
                hint.fontSize = 11;
                GUILayout.Label("No settings file found. Go to Settings tab to create one.", hint);
            }

            EndCard();

            // ── Quick Sync Options card ──
            BeginCard("Sync Options");

            // Section dropdown
            var sectionDesc = s_SubDesc;
            GUILayout.Label("Filter by section - only import frames within the selected section.", sectionDesc);
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            var sectionLabel = s_SectionLabelMuted;
            EditorGUILayout.LabelField("Section", sectionLabel, GUILayout.Width(70));

            if (_cachedSectionNames == null || _cachedSectionNames.Length == 0)
                _cachedSectionNames = new[] { "All Sections" };

            _selectedSectionIndex = EditorGUILayout.Popup(_selectedSectionIndex, _cachedSectionNames);
            if (_settings != null)
            {
                _settings.SelectedSection = _selectedSectionIndex == 0 ? "" : _cachedSectionNames[_selectedSectionIndex];
                EditorUtility.SetDirty(_settings);
            }

            var refreshStyle = s_MiniBtn;
            if (GUILayout.Button("Refresh", refreshStyle, GUILayout.Width(56)))
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
            var depthHint = s_MiniHint;
            GUILayout.Label(
                _syncDepth == 0
                    ? "Full: import all nested layers."
                    : $"Depth {_syncDepth}: import top {_syncDepth} level(s). Deeper objects render as flat images.",
                depthHint);

            EndCard();

            GUILayout.Space(4);

            // ── Frame selection + Sync flow ──
            DrawFrameSelectionCard();

            GUILayout.Space(4);

            Indent(() =>
            {
                var fetching = _previewRequest != null && !_previewRequest.isDone;
                var selectedCount = _previewFrames?.Count(f => f.Selected) ?? 0;

                if (_previewFrames == null && !fetching)
                {
                    using (new EditorGUI.DisabledGroupScope(_isImporting))
                        DrawAccentButton("Preview Document", () => FetchPreview());
                }
                else if (fetching)
                {
                    // Progress bar shown in "Select Frames to Import" card title - no button needed here
                }
                else
                {

                    GUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(4);
                    using (new EditorGUI.DisabledGroupScope(_isImporting || selectedCount == 0))
                    {
                        var syncLabel = _isImporting ? "Syncing..." : $"Sync {selectedCount} Frame(s)";
                        DrawColorButton(syncLabel, Accent, AccentHover, () => BeginImport());
                        GUILayout.Space(4);
                        DrawColorButton("Force Sync", BtnOrange, BtnOrangeHover, () =>
                        {
                            UnityFigmaBridgeImporter.ForceSync = true;
                            BeginImport();
                        }, GUILayout.Width(110), GUILayout.Height(36));
                    }
                    GUILayout.Space(4);
                    using (new EditorGUI.DisabledGroupScope(_isImporting))
                    {
                        DrawColorButton("Reload", BtnGray, BtnGrayHover, () =>
                        {
                            _previewFrames = null;
                            FetchPreview();
                        }, GUILayout.Width(70), GUILayout.Height(36));
                    }
                    GUILayout.Space(4);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(4);

                }
            });

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
            var outputRoot = FigmaPaths.FigmaSectionsFolder;

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
            var safeName = FigmaPaths.MakeValidFileName(frame.name.Trim());
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
            var previewFetching = _previewRequest != null && !_previewRequest.isDone;
            var showMiniProgress = previewFetching || _isImporting;
            BeginCard("Select Frames to Import", showMiniProgress);
            if (showMiniProgress) Repaint();

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
                GUILayout.Label("No frames found.", s_MiniHintWrap);
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
                var sectionLabel2 = entry.SectionName ?? "(No Section)";
                if (sectionLabel2 != lastSection)
                {
                    lastSection = sectionLabel2;
                    GUILayout.Space(i > 0 ? 6 : 0);
                    GUILayout.Label($"  {entry.PageName} / {sectionLabel2}", s_FrameHdr);
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
                var nameColor = entry.Selected ? (Pro ? C(0.88f) : C(0.12f)) : MutedText;
                s_FrameName.normal.textColor = nameColor;
                var nameRect = new Rect(rRect.x + 30, rRect.y, rRect.width - 120, rowH);
                GUI.Label(nameRect, entry.Name, s_FrameName);
                s_FrameName.normal.textColor = Pro ? C(0.82f) : C(0.16f); // restore default

                // Status badge (right)
                if (entry.ExistsOnDisk)
                {
                    var badgeRect = new Rect(rRect.xMax - 90, rRect.y + 5, 80, 16);
                    var badgeColor = entry.Selected ? WarningText : MutedText;
                    if (Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(badgeRect, new UnityEngine.Color(badgeColor.r, badgeColor.g, badgeColor.b, 0.15f));
                    var badgeStyle = new GUIStyle(s_FrameBadge) { normal = { textColor = badgeColor } };
                    GUI.Label(badgeRect, entry.Selected ? "OVERWRITE" : "EXISTS", badgeStyle);
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

            // Use depth=2 for lightweight fetch - only need pages and their direct children
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
            if (success) { _buildFrames = null; LoadBuildFrames(); }
            Repaint();
        }
    }
}
