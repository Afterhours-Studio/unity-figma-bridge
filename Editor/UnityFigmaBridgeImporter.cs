using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Nodes;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    ///  Manages Figma importing and document creation
    /// </summary>
    public static class UnityFigmaBridgeImporter
    {
        
        /// <summary>
        /// The settings asset, containing preferences for importing
        /// </summary>
        private static UnityFigmaBridgeSettings s_UnityFigmaBridgeSettings;

        /// <summary>
        /// Public accessor for settings (used by EditorWindow)
        /// </summary>
        public static UnityFigmaBridgeSettings Settings
        {
            get
            {
                if (s_UnityFigmaBridgeSettings == null)
                    s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
                return s_UnityFigmaBridgeSettings;
            }
        }
        
        /// <summary>
        /// We'll cache the access token in editor Player prefs
        /// </summary>
        public const string FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY = "FIGMA_PERSONAL_ACCESS_TOKEN";

        public const string PROGRESS_BOX_TITLE = "Importing Figma Document";

        /// <summary>
        /// Progress event fired during import: (message, fraction 0..1)
        /// </summary>
        public static event Action<string, float> OnProgressChanged;

        /// <summary>
        /// Fired when import completes: (success, errorMessage)
        /// </summary>
        public static event Action<bool, string> OnImportComplete;

        /// <summary>
        /// Whether an import is currently running
        /// </summary>
        public static bool IsImporting { get; private set; }

        /// <summary>
        /// Figma imposes a limit on the number of images in a single batch. This is batch size
        /// (This is a bit of a guess - 650 is rejected)
        /// </summary>
        private const int MAX_SERVER_RENDER_IMAGE_BATCH_SIZE = 300;

        /// <summary>
        /// Cached personal access token, retrieved from PlayerPrefs
        /// </summary>
        private static string s_PersonalAccessToken;
        
        /// <summary>
        /// Active canvas used for construction
        /// </summary>
        private static Canvas s_SceneCanvas;

        /// <summary>
        /// The flowScreen controller to mange prototype functionality
        /// </summary>


        static void Sync()
        {
            _ = StartSyncAsync();
        }

        /// <summary>
        /// Public entry point for triggering sync — callable by EditorWindow or menu item
        /// </summary>
        /// <summary>
        /// Selected frame node IDs — set by EditorWindow before calling StartSyncAsync.
        /// If empty, all frames are imported.
        /// </summary>
        public static List<string> SelectedFrameIds { get; set; } = new();

        public static async Task StartSyncAsync()
        {
            if (IsImporting) return;
            IsImporting = true;

            try
            {
                await RunImportPipelineAsync();
                OnImportComplete?.Invoke(true, null);
            }
            catch (Exception e)
            {
                OnImportComplete?.Invoke(false, e.Message);
            }
            finally
            {
                IsImporting = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task RunImportPipelineAsync()
        {
            var requirementsMet = CheckRequirements();
            if (!requirementsMet) return;

            var figmaFile = await DownloadFigmaDocument(s_UnityFigmaBridgeSettings.FileId);
            if (figmaFile == null) return;

            // Cache document for Build tab (non-critical — don't break import if this fails)
            try { FigmaDocumentCache.Save(figmaFile); }
            catch (Exception e) { Debug.LogWarning($"[FigmaBridge] Cache save failed (non-critical): {e.Message}"); }

            var pageNodeList = FigmaDataUtils.GetPageNodes(figmaFile);

            if (s_UnityFigmaBridgeSettings.OnlyImportSelectedPages)
            {
                var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();
                downloadPageNodeIdList.Sort();

                var settingsPageDataIdList = s_UnityFigmaBridgeSettings.PageDataList.Select(p => p.NodeId).ToList();
                settingsPageDataIdList.Sort();

                if (!settingsPageDataIdList.SequenceEqual(downloadPageNodeIdList))
                {
                    ReportError("The pages found in the Figma document have changed - check your settings file and Sync again when ready", "");

                    s_UnityFigmaBridgeSettings.RefreshForUpdatedPages(figmaFile);
                    Selection.activeObject = s_UnityFigmaBridgeSettings;
                    EditorUtility.SetDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.Refresh();

                    return;
                }

                var enabledPageIdList = s_UnityFigmaBridgeSettings.PageDataList.Where(p => p.Selected).Select(p => p.NodeId).ToList();

                if (enabledPageIdList.Count <= 0)
                {
                    ReportError("'Import Selected Pages' is selected, but no pages are selected for import", "");
                    SelectSettings();
                    return;
                }

                pageNodeList = pageNodeList.Where(p => enabledPageIdList.Contains(p.id)).ToList();
            }

            await ImportDocument(s_UnityFigmaBridgeSettings.FileId, figmaFile, pageNodeList, SelectedFrameIds);
        }

        /// <summary>
        /// Check to make sure all requirements are met before syncing
        /// </summary>
        /// <returns></returns>
        public static bool CheckRequirements() {
            
            // Find the settings asset if it exists
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            
            if (s_UnityFigmaBridgeSettings == null)
            {
                if (
                    EditorUtility.DisplayDialog("No Unity Figma Bridge Settings File",
                        "Create a new Unity Figma bridge settings file? ", "Create", "Cancel"))
                {
                    s_UnityFigmaBridgeSettings =
                        UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
                }
                else
                {
                    return false;
                }
            }

            if (Shader.Find("TextMeshPro/Mobile/Distance Field")==null)
            {
                EditorUtility.DisplayDialog("Text Mesh Pro" ,"You need to install TestMeshPro Essentials. Use Window->Text Mesh Pro->Import TMP Essential Resources","OK");
                return false;
            }
            
            if (s_UnityFigmaBridgeSettings.FileId.Length == 0)
            {
                EditorUtility.DisplayDialog("Missing Figma Document" ,"Figma Document Url is not valid, please enter valid URL","OK");
                return false;
            }
            
            // Get stored personal access key (stored in EditorPrefs, not included in builds)
            s_PersonalAccessToken = EditorPrefs.GetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY);

            if (string.IsNullOrEmpty(s_PersonalAccessToken))
            {
                var setToken = RequestPersonalAccessToken();
                if (!setToken) return false;
            }
            
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Figma Unity Bridge Importer","Please exit play mode before importing", "OK");
                return false;
            }
            
            return true;
            
        }


        private static bool CheckRunTimeRequirements()
        {
            if (string.IsNullOrEmpty(s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath))
            {
                if (EditorUtility.DisplayDialog("No Figma Bridge Scene set",
                        "Use current scene for generating prototype flow? ", "OK", "Cancel"))
                {
                    var currentScene = SceneManager.GetActiveScene();
                    s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath = currentScene.path;
                    EditorUtility.SetDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_UnityFigmaBridgeSettings);
                }
                else
                {
                    return false;
                }
            }

            // Auto-create the scene file if it doesn't exist yet
            var scenePath = s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath;
            if (!File.Exists(scenePath))
            {
                var sceneDir = Path.GetDirectoryName(scenePath);
                if (!string.IsNullOrEmpty(sceneDir) && !Directory.Exists(sceneDir))
                    Directory.CreateDirectory(sceneDir);
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, scenePath);
                AssetDatabase.Refresh();
                Debug.Log($"[FigmaBridge] Created scene at {scenePath}");
            }

            // If current scene doesn't match, switch
            if (SceneManager.GetActiveScene().path != scenePath)
            {
                if (EditorUtility.DisplayDialog("Figma Bridge Scene",
                        "Current Scene doesn't match Runtime asset scene - switch scenes?", "OK", "Cancel"))
                {
                    EditorSceneManager.OpenScene(scenePath);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        static void SelectSettings()
        {
            var bridgeSettings=UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            Selection.activeObject = bridgeSettings;
        }

        static void SetPersonalAccessToken()
        {
            RequestPersonalAccessToken();
        }
        
        /// <summary>
        /// Launch window to request personal access token
        /// </summary>
        /// <returns></returns>
        static bool RequestPersonalAccessToken()
        {
            s_PersonalAccessToken = EditorPrefs.GetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY);
            var newAccessToken = EditorInputDialog.Show( "Personal Access Token", "Please enter your Figma Personal Access Token (you can create in the 'Developer settings' page)",s_PersonalAccessToken);
            if (!string.IsNullOrEmpty(newAccessToken))
            {
                s_PersonalAccessToken = newAccessToken;
                Debug.Log("Personal access token updated");
                EditorPrefs.SetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY, s_PersonalAccessToken);
                return true;
            }

            return false;
        }


        private static Canvas CreateCanvas(bool createEventSystem)
        {
            // Canvas
            var canvasGameObject = new GameObject("Canvas");
            var canvas = canvasGameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGameObject.AddComponent<GraphicRaycaster>();

            // CanvasScaler — apply reference resolution from settings
            var scaler = canvasGameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(
                s_UnityFigmaBridgeSettings?.CanvasWidth ?? 1080,
                s_UnityFigmaBridgeSettings?.CanvasHeight ?? 2400);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            if (!createEventSystem) return canvas;

            var existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (existingEventSystem == null)
            {
                // Create new event system
                var eventSystemGameObject = new GameObject("EventSystem");
                existingEventSystem=eventSystemGameObject.AddComponent<EventSystem>();
            }

            var pointerInputModule = Object.FindFirstObjectByType<PointerInputModule>();
            if (pointerInputModule == null)
            {
                // TODO - Allow for new input system?
                existingEventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            return canvas;
        }
        

        private static void ReportProgress(string message, float fraction)
        {
            // EditorUtility.DisplayProgressBar is required — Unity Editor only pumps
            // async operations (UnityWebRequest) while a modal progress bar is active.
            EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, message, Mathf.Max(fraction, 0f));
            OnProgressChanged?.Invoke(message, fraction);
        }

        /// <summary>
        /// Public progress reporting — used by FigmaApiUtils during downloads.
        /// </summary>
        public static void ReportProgressPublic(string message, float fraction)
        {
            ReportProgress(message, fraction);
        }

        private static void ReportError(string message,string error)
        {
            EditorUtility.DisplayDialog("Unity Figma Bridge Error",message,"Ok");
            Debug.LogWarning($"{message}\n {error}\n");
        }

        public static async Task<FigmaFile> DownloadFigmaDocument(string fileId)
        {
            // Ensure token is loaded (may be called standalone from Build tab)
            if (string.IsNullOrEmpty(s_PersonalAccessToken))
                s_PersonalAccessToken = EditorPrefs.GetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY);

            // Download figma document
            ReportProgress("Downloading file", 0);

            // Debug: log what we're using
            var tokenPreview = string.IsNullOrEmpty(s_PersonalAccessToken)
                ? "(EMPTY)"
                : s_PersonalAccessToken.Substring(0, Math.Min(8, s_PersonalAccessToken.Length)) + "...";
            Debug.Log($"[FigmaBridge] Downloading file '{fileId}' with token {tokenPreview}");

            try
            {
                var figmaFile = await FigmaApiUtils.GetFigmaDocument(fileId, s_PersonalAccessToken, true);
                return figmaFile;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigmaBridge] Download failed: {e}");
                ReportError($"Download failed: {e.Message}", e.ToString());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return null;
        }

        private static async Task ImportDocument(string fileId, FigmaFile figmaFile, List<Node> downloadPageNodeList, List<string> selectedFrameIds)
        {

            // Build a list of page IDs to download
            var downloadPageIdList = downloadPageNodeList.Select(p => p.id).ToList();
            
            // Ensure we have all required directories, and remove existing files
            // TODO - Once we move to processing only differences, we won't remove existing files
            FigmaPaths.CreateRequiredDirectories();

            // Write .synced markers early so Build tab shows these frames even if import hangs later
            try
            {
                var importedFrames = CollectImportedFrameRecords(figmaFile, selectedFrameIds, s_UnityFigmaBridgeSettings.SelectedSection);
                foreach (var frame in importedFrames)
                {
                    var folder = FigmaPaths.GetContextFolder(frame.sectionName, frame.name);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, ".synced"), frame.id);
                }
                Debug.Log($"[FigmaBridge] Marked {importedFrames.Count} frame(s) as synced");
            }
            catch (Exception e) { Debug.LogError($"[FigmaBridge] .synced write failed: {e}"); }
            
            // Next build a list of all externally referenced components not included in the document (eg
            // from external libraries) and download
            var externalComponentList = FigmaDataUtils.FindMissingComponentDefinitions(figmaFile);
            
            // TODO - Implement external components
            // This is currently not working as only returns a depth of 1 of returned nodes. Need to get original files too
            /*
            FigmaFileNodes activeExternalComponentsData=null;
            if (externalComponentList.Count > 0)
            {
                ReportProgress("Getting external component data", 0);
                try
                {
                    var figmaTask = FigmaApiUtils.GetFigmaFileNodes(fileId, s_PersonalAccessToken,externalComponentList);
                    await figmaTask;
                    activeExternalComponentsData = figmaTask.Result;
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    ReportError("Error downloading external component Data",e.ToString());
                    return;
                }
            }
            */

            // For any missing component definitions, we are going to find the first instance and switch it to be
            // The source component. This has to be done early to ensure download of server images
            //FigmaFileUtils.ReplaceMissingComponents(figmaFile,externalComponentList);
            
            // Some of the nodes, we'll want to identify to use Figma server side rendering (eg vector shapes, SVGs)
            // First up create a list of nodes we'll substitute with rendered images
            var sectionFilter = s_UnityFigmaBridgeSettings.SelectedSection;
            var exportOnly = s_UnityFigmaBridgeSettings.OnlyImportExportNodes;
            var serverRenderNodes = FigmaDataUtils.FindAllServerRenderNodesInFile(
                figmaFile, externalComponentList, downloadPageIdList, sectionFilter, exportOnly,
                s_UnityFigmaBridgeSettings.SyncDepth, selectedFrameIds,
                s_UnityFigmaBridgeSettings.SkipTextImages);
            
            // Request a render of these nodes on the server if required
            var serverRenderData=new List<FigmaServerRenderData>();
            if (serverRenderNodes.Count > 0)
            {
                // Dedup Export nodes by safe name: only request one node per unique export name,
                // and skip entirely if a file with that name already exists anywhere in Sections.
                var seenExportNames = new HashSet<string>();
                var sectionsDir = FigmaPaths.FigmaSectionsFolder;
                var allNodeIds = new List<string>();
                foreach (var n in serverRenderNodes)
                {
                    if (n.RenderType == ServerRenderType.Export)
                    {
                        var safeName = FigmaPaths.MakeValidFileName(n.SourceNode.name.Trim());
                        if (!seenExportNames.Add(safeName)) continue;
                        // Already on disk somewhere in Sections → skip download entirely
                        if (Directory.Exists(sectionsDir) &&
                            Directory.GetFiles(sectionsDir, safeName + ".png", SearchOption.AllDirectories).Length > 0)
                            continue;
                    }
                    allNodeIds.Add(n.SourceNode.id);
                }
                // As the API has an upper limit of images that can be rendered in a single request, we'll need to batch
                var batchCount = Mathf.CeilToInt((float)allNodeIds.Count / MAX_SERVER_RENDER_IMAGE_BATCH_SIZE);
                for (var i = 0; i < batchCount; i++)
                {
                    var startIndex = i * MAX_SERVER_RENDER_IMAGE_BATCH_SIZE;
                    var nodeBatch = allNodeIds.GetRange(startIndex,
                        Mathf.Min(MAX_SERVER_RENDER_IMAGE_BATCH_SIZE, allNodeIds.Count - startIndex));
                    var serverNodeCsvList = string.Join(",", nodeBatch);
                    ReportProgress($"Downloading server-rendered image data {i+1}/{batchCount}", (float)i/(float)batchCount);
                    try
                    {
                        var figmaTask = FigmaApiUtils.GetFigmaServerRenderData(fileId, s_PersonalAccessToken,
                            serverNodeCsvList, s_UnityFigmaBridgeSettings.ServerRenderImageScale);
                        await figmaTask;
                        serverRenderData.Add(figmaTask.Result);
                    }
                    catch (Exception e)
                    {
                        EditorUtility.ClearProgressBar();
                        ReportError("Error downloading Figma Server Render Image Data", e.ToString());
                        return;
                    }
                }
            }

            // Make sure that existing downloaded assets are in the correct format
            FigmaApiUtils.CheckExistingAssetProperties();
            
            // Track fills that are actually used. This is needed as FIGMA has a way of listing any bitmap used rather than active
            var foundImageFills = FigmaDataUtils.GetAllImageFillIdsFromFile(
                figmaFile, downloadPageIdList, sectionFilter, exportOnly, selectedFrameIds);

            // Get image fill data for the document (list of urls to download any bitmap data used)
            FigmaImageFillData activeFigmaImageFillData;
            ReportProgress("Downloading image fill data", 0);
            try
            {
                var figmaTask = FigmaApiUtils.GetDocumentImageFillData(fileId, s_PersonalAccessToken);
                await figmaTask;
                activeFigmaImageFillData = figmaTask.Result;
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                ReportError("Error downloading Figma Image Fill Data",e.ToString());
                return;
            }
            
            // Generate a list of all items that need to be downloaded
            var downloadList =
                FigmaApiUtils.GenerateDownloadQueue(activeFigmaImageFillData,foundImageFills, serverRenderData, serverRenderNodes);

            // Download all required files
            await FigmaApiUtils.DownloadFiles(downloadList, s_UnityFigmaBridgeSettings);
            

            // Generate font mapping data
            var figmaFontMapTask = FontManager.GenerateFontMapForDocument(figmaFile,
                s_UnityFigmaBridgeSettings.EnableGoogleFontsDownloads,
                selectedFrameIds, sectionFilter);
            await figmaFontMapTask;

            // Import done — assets downloaded, fonts mapped. Build tab handles scene construction.
            ReportProgress("Import complete", 1f);
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Clean up any leftover assets post-generation.
        /// Canvas and panels are kept in the scene for the user to work with.
        /// </summary>
        private static void CleanUpPostGeneration()
        {
        }

        // ─── Build Single Frame ─────────────────────────────

        /// <summary>
        /// Fired when a single-frame build completes: (success, errorMessage)
        /// </summary>
        public static event Action<bool, string> OnBuildComplete;

        /// <summary>
        /// Whether a build is currently running
        /// </summary>
        public static bool IsBuilding { get; private set; }

        /// <summary>
        /// Build a single frame from cached document data into the active scene.
        /// Assets (images, fonts) must already exist on disk from a previous Import.
        /// </summary>
        public static async Task BuildFrameAsync(string frameNodeId)
        {
            if (IsBuilding || IsImporting) return;
            IsBuilding = true;

            try
            {
                await RunBuildFramePipeline(frameNodeId);
                OnBuildComplete?.Invoke(true, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigmaBridge] Build failed: {e}");
                OnBuildComplete?.Invoke(false, e.Message);
            }
            finally
            {
                IsBuilding = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task RunBuildFramePipeline(string frameNodeId)
        {
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            if (s_UnityFigmaBridgeSettings == null)
                throw new InvalidOperationException("No settings file found.");

            ReportProgress("Loading cached document...", 0f);

            var figmaFile = FigmaDocumentCache.Load();
            if (figmaFile == null)
                throw new InvalidOperationException("No cached document. Run Import first.");

            var (frameNode, parentNode) = FindFrameAndParent(figmaFile, frameNodeId);
            if (frameNode == null)
                throw new InvalidOperationException($"Frame node '{frameNodeId}' not found in cached document.");

            ReportProgress("Mapping fonts...", 0.2f);
            var fontMap = await FontManager.GenerateFontMapForDocument(
                figmaFile, s_UnityFigmaBridgeSettings.EnableGoogleFontsDownloads,
                new System.Collections.Generic.List<string> { frameNodeId },
                s_UnityFigmaBridgeSettings.SelectedSection);

            // Minimal process data — only what clean build needs
            var allPageIds = FigmaDataUtils.GetPageNodes(figmaFile).Select(p => p.id).ToList();
            var externalComponents = FigmaDataUtils.FindMissingComponentDefinitions(figmaFile);
            var serverRenderNodes = FigmaDataUtils.FindAllServerRenderNodesInFile(
                figmaFile, externalComponents, allPageIds,
                s_UnityFigmaBridgeSettings.SelectedSection,
                s_UnityFigmaBridgeSettings.OnlyImportExportNodes,
                s_UnityFigmaBridgeSettings.SyncDepth,
                skipTextImages: s_UnityFigmaBridgeSettings.SkipTextImages);

            var processData = new FigmaImportProcessData
            {
                Settings = s_UnityFigmaBridgeSettings,
                SourceFile = figmaFile,
                ServerRenderNodes = serverRenderNodes,
                FontMap = fontMap,
            };

            // Find or create canvas
            s_SceneCanvas = Object.FindFirstObjectByType<Canvas>();
            if (s_SceneCanvas == null)
                s_SceneCanvas = CreateCanvas(true);

            ReportProgress($"Building frame '{frameNode.name}'...", 0.4f);

            FigmaAssetGenerator.BuildSingleFrame(
                s_SceneCanvas, frameNode, parentNode, processData);

            ReportProgress("Build complete", 1f);
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        private static List<Utils.FrameRecord> CollectImportedFrameRecords(
            FigmaFile file, List<string> selectedFrameIds, string sectionFilter)
        {
            var records = new List<Utils.FrameRecord>();
            foreach (var page in file.document.children)
            {
                if (page.children == null) continue;
                foreach (var child in page.children)
                {
                    if (child.type == NodeType.SECTION)
                    {
                        if (!string.IsNullOrEmpty(sectionFilter) && child.name != sectionFilter) continue;
                        if (child.children == null) continue;
                        foreach (var frame in child.children)
                        {
                            if (frame.type != NodeType.FRAME) continue;
                            if (selectedFrameIds != null && selectedFrameIds.Count > 0 && !selectedFrameIds.Contains(frame.id)) continue;
                            records.Add(new Utils.FrameRecord(frame.id, frame.name, child.name, page.name));
                        }
                    }
                    else if (child.type == NodeType.FRAME)
                    {
                        if (!string.IsNullOrEmpty(sectionFilter)) continue;
                        if (selectedFrameIds != null && selectedFrameIds.Count > 0 && !selectedFrameIds.Contains(child.id)) continue;
                        records.Add(new Utils.FrameRecord(child.id, child.name, "", page.name));
                    }
                }
            }
            return records;
        }

        /// <summary>
        /// Walks the cached document tree to find a frame node by ID and its parent.
        /// </summary>
        private static (Node frame, Node parent) FindFrameAndParent(FigmaFile file, string nodeId)
        {
            foreach (var page in file.document.children)
            {
                if (page.children == null) continue;
                foreach (var child in page.children)
                {
                    if (child.id == nodeId)
                        return (child, page);

                    if (child.type == NodeType.SECTION && child.children != null)
                    {
                        foreach (var frame in child.children)
                        {
                            if (frame.id == nodeId)
                                return (frame, child);
                        }
                    }
                }
            }
            return (null, null);
        }
    }
}
