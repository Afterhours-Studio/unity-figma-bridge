using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    /// Parsed result from a Figma URL — contains file ID and optional node ID.
    /// </summary>
    public readonly struct FigmaUrlInfo
    {
        public readonly bool IsValid;
        public readonly string FileId;
        public readonly string NodeId;

        public FigmaUrlInfo(bool isValid, string fileId, string nodeId)
        {
            IsValid = isValid;
            FileId = fileId;
            NodeId = nodeId;
        }

        public bool HasNodeId => !string.IsNullOrEmpty(NodeId);
    }

    /// <summary>
    /// Reason for server rendering
    /// </summary>
    public enum ServerRenderType
    {
        Substitution, // We want to replace a complex node with an image
        Export // We want to export this image
    }
        
    /// <summary>
    /// Encapsulates server render node data
    /// </summary>
    public class ServerRenderNodeData
    {
        public ServerRenderType RenderType = ServerRenderType.Substitution;
        public Node SourceNode;
        public string SectionName = "";
        public string FrameName = "";
    }
    
    public static class FigmaApiUtils
    {
#if UNITY_FIGMA_DEBUG
        private static string WRITE_FILE_PATH = "FigmaOutput.json";
#endif
        private const int MAX_RETRY_COUNT = 3;
        private const int RETRY_DELAY_MS = 1000;

        /// <summary>
        /// Sends a web request with retry logic for transient failures
        /// </summary>
        private static async Task<UnityWebRequest> SendRequestWithRetry(string url, string accessToken, int maxRetries = MAX_RETRY_COUNT)
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                var webRequest = UnityWebRequest.Get(url);
                webRequest.SetRequestHeader("X-Figma-Token", accessToken);
                await webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                    return webRequest;

                if (webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    // Don't retry client errors (4xx)
                    var responseCode = webRequest.responseCode;
                    if (responseCode >= 400 && responseCode < 500)
                        throw new Exception($"Figma API error ({responseCode}): {webRequest.error} url - {url}");
                }

                if (attempt < maxRetries)
                {
                    Debug.LogWarning($"Request failed (attempt {attempt + 1}/{maxRetries + 1}), retrying: {webRequest.error}");
                    await Task.Delay(RETRY_DELAY_MS * (attempt + 1));
                }
                else
                {
                    throw new Exception($"Request failed after {maxRetries + 1} attempts: {webRequest.error} url - {url}");
                }
            }

            throw new Exception($"Unexpected retry loop exit for url: {url}");
        }
        
        /// <summary>
        /// Encapsulate download data
        /// </summary>
        public class FigmaDownloadQueueItem
        {
            public enum FigmaFileType
            {
                ImageFill,
                ServerRenderedImage
            }

            public FigmaFileType FileType;
            public string Url;
            public string FilePath;
        }
        
        


        /// <summary>
        /// Get Figma File Id from document Url
        /// </summary>
        /// <param name="url"Document Url</param>
        /// <returns>File Id</returns>
        public static (bool, string) GetFigmaDocumentIdFromUrl(string url)
        {
            var parsed = ParseFigmaUrl(url);
            return (parsed.IsValid, parsed.FileId);
        }

        /// <summary>
        /// Full URL parser — extracts file ID and optional node-id from page/frame URLs.
        /// Supports both legacy (/file/) and modern (/design/) URL formats.
        /// </summary>
        public static FigmaUrlInfo ParseFigmaUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return new FigmaUrlInfo(false, "", "");

            const string legacyPrefix = "https://www.figma.com/file/";
            const string modernPrefix = "https://www.figma.com/design/";

            string prefix;
            if (url.StartsWith(legacyPrefix, StringComparison.Ordinal))
                prefix = legacyPrefix;
            else if (url.StartsWith(modernPrefix, StringComparison.Ordinal))
                prefix = modernPrefix;
            else
                return new FigmaUrlInfo(false, "", "");

            var remainder = url.Substring(prefix.Length);
            var slashIndex = remainder.IndexOf('/');
            if (slashIndex == -1)
                return new FigmaUrlInfo(false, "", "");

            var fileId = remainder.Substring(0, slashIndex);

            // Parse node-id from query string: ?node-id=1-2 or &node-id=1-2
            var nodeId = "";
            var queryIndex = url.IndexOf('?');
            if (queryIndex >= 0)
            {
                var query = url.Substring(queryIndex + 1);
                foreach (var param in query.Split('&'))
                {
                    if (!param.StartsWith("node-id=", StringComparison.OrdinalIgnoreCase)) continue;
                    // Figma URLs use dash separators (1-2), API uses colon (1:2)
                    nodeId = param.Substring("node-id=".Length).Replace('-', ':');
                    break;
                }
            }

            return new FigmaUrlInfo(true, fileId, nodeId);
        }

        /// <summary>
        /// Download a Figma doc from server and deserialize
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="writeFile">Optionally write this file to disk</param>
        /// <returns>The deserialized Figma file</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaFile> GetFigmaDocument(string fileId, string accessToken, bool writeFile)
        {
            var url =
                $"https://api.figma.com/v1/files/{fileId}?geometry=paths"; // We need geometry=paths to get rotation and full transform

            FigmaFile figmaFile = null;
            // Download the Figma Document with retry logic
            var webRequest = await SendRequestWithRetry(url, accessToken);

            try
            {
                // Create a settings object to ignore missing members and null fields that sometimes come from Figma
                JsonSerializerSettings settings = new JsonSerializerSettings()
                {
                    DefaultValueHandling = DefaultValueHandling.Include,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore,
                    Converters = { new Newtonsoft.Json.Converters.StringEnumConverter { AllowIntegerValues = true } },
                    Error = (sender, args) =>
                    {
                        // Skip unknown enum values instead of crashing
                        Debug.LogWarning($"[FigmaBridge] JSON parse warning: {args.ErrorContext.Error.Message}");
                        args.ErrorContext.Handled = true;
                    },
                };
                
                // Deserialize the document
                figmaFile = JsonConvert.DeserializeObject<FigmaFile>(webRequest.downloadHandler.text, settings);

                Debug.Log($"Figma file downloaded, name {figmaFile.name}");
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding Figma document JSON {e.ToString()}");
            }

#if UNITY_FIGMA_DEBUG
            if (writeFile) File.WriteAllText(Path.Combine("Assets", WRITE_FILE_PATH), webRequest.downloadHandler.text);
#endif
            return figmaFile;
        }

        /// <summary>
        /// Requests a server-side rendering of nodes from a document, returning list of urls to download
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="serverNodeCsvList">Csv List of nodes to render</param>
        /// <param name="serverRenderImageScale">Scale to render images at</param>
        /// <returns>List of urls to access the rendered images</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaServerRenderData> GetFigmaServerRenderData(string fileId, string accessToken,
            string serverNodeCsvList, int serverRenderImageScale)
        {
            FigmaServerRenderData figmaServerRenderData = null;
            // Execute server-side rendering with retry logic
            var serverRenderUrl =
                $"https://api.figma.com/v1/images/{fileId}?ids={serverNodeCsvList}&scale={serverRenderImageScale}&use_absolute_bounds=true";
            var webRequest = await SendRequestWithRetry(serverRenderUrl, accessToken);

            try
            {
                figmaServerRenderData =
                    JsonConvert.DeserializeObject<FigmaServerRenderData>(webRequest.downloadHandler.text);
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding server render JSON {e.ToString()}");
            }

            return figmaServerRenderData;
        }

        /// <summary>
        /// Downloads image fill data for a Figma document
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <returns>List of image fills for the document</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaImageFillData> GetDocumentImageFillData(string fileId, string accessToken)
        {
            FigmaImageFillData imageFillData;
            // Download a list all the image fills container in the Figma document
            var imageFillUrl = $"https://api.figma.com/v1/files/{fileId}/images";

            var webRequest = await SendRequestWithRetry(imageFillUrl, accessToken);
            try
            {
                imageFillData = JsonConvert.DeserializeObject<FigmaImageFillData>(webRequest.downloadHandler.text);
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding image fill JSON {e.ToString()}");
            }

            return imageFillData;
        }


        /// <summary>
        /// Retrieves specific nodes from specific files
        /// </summary>
        /// <param name="fileId">Figma File Id</param>
        /// <param name="accessToken">Figma Access Token</param>
        /// <param name="nodeIds">List of Node Ids to process</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<FigmaFileNodes> GetFigmaFileNodes(string fileId, string accessToken,List<string> nodeIds)
        {
            FigmaFileNodes fileNodes;
            var externalComponentsJoined = string.Join(",",nodeIds);
            var componentsUrl = $"https://api.figma.com/v1/files/{fileId}/nodes/?ids={externalComponentsJoined}";
            
            // Download the FIGMA Document with retry logic
            var webRequest = await SendRequestWithRetry(componentsUrl, accessToken);
            try
            {
                fileNodes = JsonConvert.DeserializeObject<FigmaFileNodes>(webRequest.downloadHandler.text);
#if UNITY_FIGMA_DEBUG
                File.WriteAllText("ComponentNodes.json", webRequest.downloadHandler.text);
#endif
            }
            catch (Exception e)
            {
                throw new Exception($"Problem decoding Figma components JSON {e.ToString()}");
            }

            return fileNodes;
        }


        /// <summary>
        /// Generates a standardised list of files to download 
        /// </summary>
        /// <param name="imageFillData"></param>
        /// <param name="foundImageFills"></param>
        /// <param name="serverRenderData"></param>
        /// <param name="serverRenderNodes"></param>
        /// <returns></returns>
        public static List<FigmaDownloadQueueItem> GenerateDownloadQueue(FigmaImageFillData imageFillData,
            Dictionary<string, FigmaDataUtils.ImageFillContext> foundImageFills,
            List<FigmaServerRenderData> serverRenderData, List<ServerRenderNodeData> serverRenderNodes)
        {
            var downloadList = new List<FigmaDownloadQueueItem>();

            // Image fills — set context per image for folder structure
            foreach (var keyPair in imageFillData.meta.images)
            {
                if (!foundImageFills.TryGetValue(keyPair.Key, out var ctx)) continue;

                // Set context so GetPathForImageFill uses the right folder
                FigmaPaths.SetContext(ctx.SectionName, ctx.FrameName);
                var filePath = FigmaPaths.GetPathForImageFill(keyPair.Key);

                if (!File.Exists(filePath))
                {
                    downloadList.Add(new FigmaDownloadQueueItem
                    {
                        Url = keyPair.Value,
                        FilePath = filePath,
                        FileType = FigmaDownloadQueueItem.FigmaFileType.ImageFill
                    });
                }
            }
            FigmaPaths.ClearContext();

            // Server render images — set context per node for folder structure
            foreach (var serverRenderDataEntry in serverRenderData)
            {
                foreach (var keyPair in serverRenderDataEntry.images)
                {
                    if (string.IsNullOrEmpty(keyPair.Value))
                    {
                        Debug.Log($"Can't download image for Server Node {keyPair.Key}");
                    }
                    else
                    {
                        // Find the matching node to get section/frame context
                        var matchingNode = serverRenderNodes.FirstOrDefault(n => n.SourceNode.id == keyPair.Key);
                        if (matchingNode != null)
                            FigmaPaths.SetContext(matchingNode.SectionName, matchingNode.FrameName);

                        var renderPath = FigmaPaths.GetPathForServerRenderedImage(keyPair.Key, serverRenderNodes);
                        FigmaPaths.ClearContext();

                        if (File.Exists(renderPath)) continue;
                        downloadList.Add(new FigmaDownloadQueueItem
                        {
                            Url = keyPair.Value,
                            FilePath = renderPath,
                            FileType = FigmaDownloadQueueItem.FigmaFileType.ServerRenderedImage
                        });
                    }
                }
            }

            return downloadList;
        }
        

        /// <summary>
        /// Max concurrent downloads
        /// </summary>
        private const int MAX_CONCURRENT_DOWNLOADS = 8;

        /// <summary>
        /// Download required files with parallel downloads and batched asset import.
        /// </summary>
        public static async Task DownloadFiles(List<FigmaDownloadQueueItem> downloadItems, UnityFigmaBridgeSettings settings)
        {
            if (downloadItems.Count == 0) return;

            var totalCount = downloadItems.Count;
            var completedCount = 0;
            var downloadedPaths = new List<string>();

            // Ensure all directories exist upfront
            var dirs = downloadItems.Select(d => Path.GetDirectoryName(d.FilePath)).Distinct();
            foreach (var dir in dirs)
            {
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }

            // Phase 1: Download all files in parallel batches (no asset import yet)
            for (var batchStart = 0; batchStart < totalCount; batchStart += MAX_CONCURRENT_DOWNLOADS)
            {
                var batchSize = Mathf.Min(MAX_CONCURRENT_DOWNLOADS, totalCount - batchStart);
                var batch = downloadItems.GetRange(batchStart, batchSize);
                var requests = new List<(UnityWebRequest request, FigmaDownloadQueueItem item)>();

                // Fire all requests in this batch with timeout
                foreach (var item in batch)
                {
                    var req = UnityWebRequest.Get(item.Url);
                    req.timeout = 30; // 30 second timeout per request
                    var _ = req.SendWebRequest();
                    requests.Add((req, item));
                }

                // Wait for all requests in this batch to complete
                var batchStartTime = EditorApplication.timeSinceStartup;
                const double batchTimeout = 60.0; // 60s max per batch
                while (requests.Any(r => !r.request.isDone))
                {
                    if (EditorApplication.timeSinceStartup - batchStartTime > batchTimeout)
                    {
                        Debug.LogWarning($"[FigmaBridge] Download batch timed out, skipping stuck requests");
                        break;
                    }
                    await Task.Yield();
                }

                // Write files to disk
                foreach (var (request, item) in requests)
                {
                    try
                    {
                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            File.WriteAllBytes(item.FilePath, request.downloadHandler.data);
                            downloadedPaths.Add(item.FilePath);
                        }
                        else
                        {
                            Debug.LogWarning($"Download failed for '{item.FilePath}': {request.error}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Error saving '{item.FilePath}': {e.Message}");
                    }
                    finally
                    {
                        request.Dispose();
                    }

                    completedCount++;
                }

                var fraction = (float)completedCount / totalCount;
                UnityFigmaBridgeImporter.ReportProgressPublic($"Downloaded {completedCount}/{totalCount} images", fraction);
            }

            // Phase 2: Single batch AssetDatabase refresh for all downloaded files
            if (downloadedPaths.Count > 0)
            {
                UnityFigmaBridgeImporter.ReportProgressPublic("Importing assets into Unity...", 0.95f);

                try
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (var path in downloadedPaths)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                // Configure texture importers in batch (no reimport yet)
                try
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (var item in downloadItems)
                    {
                        if (!downloadedPaths.Contains(item.FilePath)) continue;
                        var textureImporter = AssetImporter.GetAtPath(item.FilePath) as TextureImporter;
                        if (textureImporter == null) continue;

                        textureImporter.textureType = TextureImporterType.Sprite;
                        textureImporter.spriteImportMode = SpriteImportMode.Single;
                        textureImporter.alphaIsTransparency = true;
                        textureImporter.mipmapEnabled = true;
                        textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                        textureImporter.sRGBTexture = true;
                        textureImporter.wrapMode = item.FileType == FigmaDownloadQueueItem.FigmaFileType.ImageFill
                            ? TextureWrapMode.Repeat
                            : TextureWrapMode.Clamp;

                        textureImporter.SaveAndReimport();
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.Refresh();
            }

            Debug.Log($"Downloaded {downloadedPaths.Count}/{totalCount} images");
        }

    
        /// <summary>
        /// Checks that existing assets are in the correct format
        /// </summary>
        public static void CheckExistingAssetProperties()
        {
            CheckImageFillTextureProperties();
        }

        /// <summary>
        /// Checks downloaded image fills
        /// </summary>
        private static void CheckImageFillTextureProperties()
        {
            foreach (var filePath in Directory.GetFiles(FigmaPaths.FigmaImageFillFolder))
            {
                var textureImporter = AssetImporter.GetAtPath(filePath) as TextureImporter;
                if (textureImporter == null) continue;
                // Previous versions may not have sRGB set
                if (textureImporter.sRGBTexture) continue;
                textureImporter.sRGBTexture = true;
                textureImporter.SaveAndReimport();
            }
        }
    }
}