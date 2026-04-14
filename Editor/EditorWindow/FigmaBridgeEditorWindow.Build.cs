using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    public sealed partial class FigmaBridgeEditorWindow
    {
        // ─── Build Tab ───────────────────────────────────

        private void DrawBuildTab()
        {
            _isBuilding = UnityFigmaBridgeImporter.IsBuilding;

            // Cache status card
            BeginCard("Document Cache");

            var cacheExists = FigmaDocumentCache.Exists;
            if (cacheExists)
            {
                var lastMod = FigmaDocumentCache.LastModified;
                var timeStr = lastMod.HasValue ? lastMod.Value.ToString("yyyy-MM-dd HH:mm:ss") : "unknown";
                DrawKeyValue("Status", "Cached", SuccessText);
                DrawKeyValue("Updated", timeStr);
            }
            else
            {
                DrawKeyValue("Status", "No cache - run Import first", ErrorText);
            }

            GUILayout.Space(4);
            using (new EditorGUI.DisabledGroupScope(_isImporting))
            {
                var refreshing = _cacheRefreshRequest != null && !_cacheRefreshRequest.isDone;
                var refreshLabel = refreshing ? "Refreshing..." : "Refresh Cache";
                using (new EditorGUI.DisabledGroupScope(refreshing))
                    DrawColorButton(refreshLabel, BtnGray, BtnGrayHover,
                        () => RefreshCacheFromFigma(), GUILayout.Height(28));
            }

            EndCard();

            if (!cacheExists)
            {
                GUILayout.Space(20);
                GUILayout.Label("Import a document first to populate the cache.\nThen switch to Build tab to build individual frames.", s_EmptyHint);
                return;
            }

            // Auto-load frames if not loaded yet
            if (_buildFrames == null)
                LoadBuildFrames();

            if (_buildFrames == null || _buildFrames.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("No frames found.\nCheck section filter in Settings, or click Reload.", s_EmptyHint);
                return;
            }

            // Frame list grouped by section
            BeginCard("Frames");

            var countText = $"{_buildFrames.Count} frame(s)";
            DrawBadge(countText, Accent);
            GUILayout.Space(6);

            _buildScrollPos = EditorGUILayout.BeginScrollView(_buildScrollPos, GUILayout.MaxHeight(400));

            const float rowH = 28;
            var evenBg = Pro ? C(0.22f) : C(0.94f);
            var oddBg = Pro ? C(0.20f) : C(0.92f);

            string lastSection = null;
            for (int i = 0; i < _buildFrames.Count; i++)
            {
                var entry = _buildFrames[i];

                // Section header
                var sectionLabel = entry.SectionName ?? "(No Section)";
                if (sectionLabel != lastSection)
                {
                    lastSection = sectionLabel;
                    if (i > 0) GUILayout.Space(6);

                    var sectionRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(sectionRect, Pro ? C(0.17f) : C(0.86f));
                        EditorGUI.DrawRect(new Rect(sectionRect.x, sectionRect.y, 3, sectionRect.height), Accent);
                    }
                    GUI.Label(sectionRect, sectionLabel, s_SectionHeader);
                }

                // Frame row
                var rRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rRect, i % 2 == 0 ? evenBg : oddBg);

                // Frame name
                var nameRect = new Rect(rRect.x, rRect.y, rRect.width - 70, rowH);
                GUI.Label(nameRect, entry.Name, s_RowName);

                // Sync time label
                if (!string.IsNullOrEmpty(entry.LastSyncedAt))
                {
                    var syncRect = new Rect(rRect.xMax - 200, rRect.y + 6, 60, 16);
                    GUI.Label(syncRect, entry.LastSyncedAt, s_RowSync);
                }

                // Existing badge
                if (entry.ExistsOnDisk)
                {
                    var badgeRect = new Rect(rRect.xMax - 130, rRect.y + 6, 50, 16);
                    if (Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(badgeRect, new UnityEngine.Color(MutedText.r, MutedText.g, MutedText.b, 0.15f));
                    GUI.Label(badgeRect, "EXISTS", s_RowBadge);
                }

                // Build button (right-aligned, DrawColorButton style)
                var btnRect = new Rect(rRect.xMax - 62, rRect.y + 5, 56, rowH - 10);
                var btnDisabled = _isBuilding || _isImporting;
                var btnHover = !btnDisabled && btnRect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    var btnColor = btnDisabled ? C(0.35f) : btnHover ? AccentHover : Accent;
                    EditorGUI.DrawRect(btnRect, btnColor);
                }
                var btnLabelColor = btnDisabled ? C(0.55f) : UnityEngine.Color.white;
                var btnLabelStyle = new GUIStyle(s_RowBtnLabel) { normal = { textColor = btnLabelColor } };
                GUI.Label(btnRect, "Build", btnLabelStyle);
                if (!btnDisabled && Event.current.type == EventType.MouseDown && btnRect.Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    BuildFrame(entry);
                }
                if (!btnDisabled) EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
            }

            EditorGUILayout.EndScrollView();

            if (_isBuilding)
            {
                GUILayout.Space(8);
                DrawProgressBar(_progressFraction, _progressMessage);
            }

            EndCard();
        }

        private void LoadBuildFrames()
        {
            _buildFrames = new List<BuildFrameEntry>();
            // Use in-memory cache - only re-parse JSON when file on disk changed
            var diskTime = FigmaDocumentCache.LastModified ?? DateTime.MinValue;
            if (_cachedFigmaFile == null || diskTime > _cachedFigmaFileTime)
            {
                _cachedFigmaFile = FigmaDocumentCache.Load();
                _cachedFigmaFileTime = diskTime;
            }
            var figmaFile = _cachedFigmaFile;
            if (figmaFile?.document?.children == null) return;

            var sectionFilter = _settings != null ? _settings.SelectedSection : "";

            foreach (var page in figmaFile.document.children)
            {
                if (page.children == null) continue;

                foreach (var child in page.children)
                {
                    if (child.type == NodeType.SECTION)
                    {
                        if (!string.IsNullOrEmpty(sectionFilter) && child.name != sectionFilter)
                            continue;

                        if (child.children != null)
                        {
                            foreach (var frame in child.children)
                            {
                                if (frame.type == NodeType.FRAME)
                                    AddBuildFrameEntry(frame, page.name, child.name);
                            }
                        }
                    }
                    else if (child.type == NodeType.FRAME)
                    {
                        if (string.IsNullOrEmpty(sectionFilter))
                            AddBuildFrameEntry(child, page.name, null);
                    }
                }
            }

            AppendLog($"Build tab: loaded {_buildFrames.Count} frame(s) from cache");
        }

        private void AddBuildFrameEntry(Node frame, string pageName, string sectionName)
        {
            var safeName = FigmaPaths.MakeValidFileName(frame.name.Trim());
            var folderPath = FigmaPaths.GetContextFolder(sectionName, safeName);

            // Only show frames that have been synced (marked by importer)
            if (!SyncedFrameManifest.Exists(folderPath)) return;

            var manifest = SyncedFrameManifest.Load(folderPath);
            var syncedAt = "";
            if (manifest?.syncedAt != null)
            {
                if (System.DateTime.TryParse(manifest.syncedAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    syncedAt = dt.ToLocalTime().ToString("MM/dd HH:mm");
            }

            var prefabPath = $"{folderPath}/{safeName}.prefab";
            _buildFrames.Add(new BuildFrameEntry
            {
                NodeId = frame.id,
                Name = frame.name,
                PageName = pageName,
                SectionName = sectionName ?? "(No Section)",
                PrefabPath = prefabPath,
                ExistsOnDisk = System.IO.File.Exists(prefabPath),
                LastSyncedAt = syncedAt,
            });
        }

        private async void BuildFrame(BuildFrameEntry entry)
        {
            AppendLog($"Building frame: {entry.Name}...");
            await UnityFigmaBridgeImporter.BuildFrameAsync(entry.NodeId);
            // Refresh exists-on-disk status
            LoadBuildFrames();
        }

        private UnityEngine.Networking.UnityWebRequest _cacheRefreshRequest;

        private void RefreshCacheFromFigma()
        {
            if (_cacheRefreshRequest != null && !_cacheRefreshRequest.isDone) return;

            EnsureSettingsLoaded();
            if (_settings == null || string.IsNullOrEmpty(_settings.FileId)) return;

            var token = EditorPrefs.GetString(UnityFigmaBridgeImporter.FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, "");
            if (string.IsNullOrEmpty(token)) { AppendLog("No token set.", true); return; }

            AppendLog("Refreshing cache from Figma...");
            var url = $"https://api.figma.com/v1/files/{_settings.FileId}?geometry=paths";
            _cacheRefreshRequest = UnityEngine.Networking.UnityWebRequest.Get(url);
            _cacheRefreshRequest.SetRequestHeader("X-Figma-Token", token);
            _cacheRefreshRequest.SendWebRequest();
            EditorApplication.update += CheckCacheRefreshRequest;
            Repaint();
        }

        private void CheckCacheRefreshRequest()
        {
            if (_cacheRefreshRequest == null || !_cacheRefreshRequest.isDone) return;
            EditorApplication.update -= CheckCacheRefreshRequest;

            if (_cacheRefreshRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AppendLog($"Refresh failed ({_cacheRefreshRequest.responseCode}): {_cacheRefreshRequest.error}", true);
                _cacheRefreshRequest.Dispose();
                _cacheRefreshRequest = null;
                Repaint();
                return;
            }

            try
            {
                var json = _cacheRefreshRequest.downloadHandler.text;
                var figmaFile = Newtonsoft.Json.JsonConvert.DeserializeObject<FigmaFile>(json,
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Ignore,
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter { AllowIntegerValues = true } },
                        Error = (sender, args) => { args.ErrorContext.Handled = true; },
                    });

                FigmaDocumentCache.Save(figmaFile);
                _cachedFigmaFile = null; // invalidate in-memory cache
                LoadBuildFrames();
                AppendLog("Cache refreshed successfully");
            }
            catch (Exception e)
            {
                AppendLog($"Parse error: {e.Message}", true);
            }
            finally
            {
                _cacheRefreshRequest.Dispose();
                _cacheRefreshRequest = null;
            }
            Repaint();
        }

        private void HandleBuildComplete(bool success, string error)
        {
            _isBuilding = false;
            AppendLog(success ? "Build completed successfully" : $"Build failed: {error}", !success);
            Repaint();
        }
    }
}
