using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Afterhours.FigmaBridge.Editor
{
    public static class FigmaPaths
    {
        private const string DEFAULT_ROOT = "Assets/Figma";

        /// <summary>
        /// Root folder for assets — reads from settings if available, otherwise uses default.
        /// </summary>
        public static string FigmaAssetsRootFolder
        {
            get
            {
                var settings = UnityFigmaBridgeImporter.Settings;
                var path = settings != null ? settings.AssetOutputPath : null;
                return string.IsNullOrEmpty(path) ? DEFAULT_ROOT : path;
            }
        }

        // ─── Structural folders ──────────────────────────
        public static string FigmaSectionsFolder => $"{FigmaAssetsRootFolder}/Sections";
        public static string FigmaFontMaterialPresetsFolder => $"{FigmaAssetsRootFolder}/FontMaterialPresets";
        public static string FigmaFontsFolder => $"{FigmaAssetsRootFolder}/Fonts";

        // Legacy flat folders — kept for backwards compat lookups
        public static string FigmaImageFillFolder => $"{FigmaAssetsRootFolder}/ImageFills";

        // ─── Current context (set during import) ─────────
        // These are set by the importer/generator as it traverses the tree
        // so that path helpers can organize files into section/frame subfolders.
        public static string CurrentSectionName { get; set; } = "";
        public static string CurrentFrameName { get; set; } = "";

        /// <summary>
        /// Get the context-aware folder for an explicit section/frame pair.
        /// Structure: Root/Sections/{Section}/{Frame}/
        /// </summary>
        public static string GetContextFolder(string sectionName, string frameName)
        {
            var root = FigmaSectionsFolder;
            if (!string.IsNullOrEmpty(sectionName))
                root = $"{root}/{MakeValidFileName(sectionName)}";
            if (!string.IsNullOrEmpty(frameName))
                root = $"{root}/{MakeValidFileName(frameName)}";
            return root;
        }

        private static string GetContextFolder() => GetContextFolder(CurrentSectionName, CurrentFrameName);

        // ─── Path helpers (context-aware) ────────────────

        public static string GetPathForImageFill(string imageId, string nodeName = null)
        {
            var fileName = !string.IsNullOrEmpty(nodeName) ? MakeValidFileName(nodeName.Trim()) : imageId;
            return $"{GetContextFolder()}/{fileName}.png";
        }

        public static string GetPathForServerRenderedImage(string nodeId,
            List<ServerRenderNodeData> serverRenderNodeData)
        {
            var entry = serverRenderNodeData.FirstOrDefault(n => n.SourceNode.id == nodeId);
            var folder = entry != null
                ? GetContextFolder(entry.SectionName, entry.FrameName)
                : GetContextFolder();

            if (entry != null && entry.RenderType == ServerRenderType.Export)
                return $"{folder}/{MakeValidFileName(StripConventionTags(entry.SourceNode.name.Trim()))}.png";

            var safeNodeId = FigmaDataUtils.ReplaceUnsafeFileCharactersForNodeId(nodeId);
            return $"{folder}/{safeNodeId}.png";
        }

        public static string GetPathForScreenPrefab(Node node, int duplicateCount)
        {
            return $"{GetContextFolder()}/{GetFileNameForNode(node, duplicateCount)}.prefab";
        }

        // ─── Utilities ───────────────────────────────────

        public static string GetFileNameForNode(Node node, int duplicateCount)
        {
            var safeNodeTitle = ReplaceUnsafeCharacters(node.name);
            if (duplicateCount > 0) safeNodeTitle += $"_{duplicateCount}";
            return safeNodeTitle;
        }

        private static string ReplaceUnsafeCharacters(string inputFilename)
        {
            var safeFilename = inputFilename.Trim();
            return MakeValidFileName(safeFilename);
        }

        /// <summary>
        /// Strip convention tags like [Button], [9Slice:24], [RectMask2D] from a node name.
        /// </summary>
        public static string StripConventionTags(string name)
        {
            while (name.StartsWith("["))
            {
                var close = name.IndexOf(']');
                if (close < 0) break;
                name = name.Substring(close + 1).TrimStart();
            }
            return name;
        }

        public static string MakeValidFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(
                new string(Path.GetInvalidFileNameChars()));
            invalidChars += ".";
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }

        /// <summary>
        /// Set the current context for path generation.
        /// Call this as you traverse sections/frames during import.
        /// </summary>
        public static void SetContext(string sectionName, string frameName)
        {
            CurrentSectionName = sectionName ?? "";
            CurrentFrameName = frameName ?? "";
        }

        public static void ClearContext()
        {
            CurrentSectionName = "";
            CurrentFrameName = "";
        }

        public static void CreateRequiredDirectories()
        {
            var requiredDirs = new[]
            {
                FigmaSectionsFolder,
                FigmaFontMaterialPresetsFolder,
                FigmaFontsFolder,
            };
            foreach (var dir in requiredDirs)
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// Ensure a directory exists for a given file path.
        /// </summary>
        public static void EnsureDirectoryForFile(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
