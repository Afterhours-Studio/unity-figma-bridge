using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Settings
{
    public class UnityFigmaBridgeSettings : ScriptableObject
    {
       
        [Tooltip("Figma URL — supports both Document URLs and Page/Frame URLs with node-id")]
        public string DocumentUrl;
        
        [Tooltip("Generate logic and linking of screens based on FIGMA's 'Prototype' settings")]
        public bool BuildPrototypeFlow=true;
        
        [Space(10)]
        [Tooltip("Scene used for prototype assets, including canvas")]
        public string RunTimeAssetsScenePath = "Assets/Scenes/FigmaScene.unity";

        [Tooltip("Reference canvas width (pixels)")]
        public int CanvasWidth = 1080;

        [Tooltip("Reference canvas height (pixels)")]
        public int CanvasHeight = 2400;
        
        [Tooltip("Convert Figma Auto Layout to Unity Layout Groups (Horizontal/Vertical)")]
        public bool EnableAutoLayout = true;
        
        [HideInInspector]
        public string ScreenBindingNamespace = "";

        [Tooltip("Scale for rendering server images")]
        public int ServerRenderImageScale=1;

        [Tooltip("Folder to store imported Figma assets (prefabs, images, fonts)")]
        public string AssetOutputPath = "Assets/Figma";

        [Tooltip("Download missing fonts from Google Fonts automatically")]
        public bool EnableGoogleFontsDownloads = false;

        [Tooltip("Never server-render text nodes — use TMP/Text components instead of downloading images")]
        public bool SkipTextImages = true;

        [Tooltip("Text rendering backend. Auto = TMP if available, otherwise legacy Text.")]
        public TextRenderMode TextMode = TextRenderMode.Auto;

        [Tooltip("Only import nodes marked for Export in Figma (ignores all other nodes)")]
        public bool OnlyImportExportNodes = true;

        [Tooltip("If true, download only selected pages and screens")]
        public bool OnlyImportSelectedPages = false;

        [Tooltip("Layer depth for import: 0 = full depth, 1 = top-level only, 2+ = descend N levels")]
        [Range(0, 10)]
        public int SyncDepth = 5;

        [HideInInspector]
        public string SelectedSection = "";

        [HideInInspector]
        public List<FigmaPageData> PageDataList = new ();

        public string FileId
        {
            get
            {
                var info = FigmaApiUtils.ParseFigmaUrl(DocumentUrl);
                return info.IsValid ? info.FileId : "";
            }
        }

        /// <summary>
        /// Parsed node-id from URL (if page/frame URL was provided).
        /// </summary>
        public string UrlNodeId
        {
            get
            {
                var info = FigmaApiUtils.ParseFigmaUrl(DocumentUrl);
                return info.HasNodeId ? info.NodeId : "";
            }
        }
        
        public void RefreshForUpdatedPages(FigmaFile file)
        {
            // Get all pages from Figma Doc
            var pageNodeList = FigmaDataUtils.GetPageNodes(file);
            var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();

            // Get a list of all pages in the settings file
            var settingsPageDataIdList = PageDataList.Select(p => p.NodeId).ToList();

            // Build a list of all new pages to add
            var addPageIdList = downloadPageNodeIdList.Except(settingsPageDataIdList);
            foreach (var addPageId in addPageIdList)
            {
                var addNode = pageNodeList.FirstOrDefault(p => p.id == addPageId);
                PageDataList.Add(new FigmaPageData(addNode.name, addNode.id));
            }
            
            // Build a list of removed pages to remove from list
            var deletePageIdList = settingsPageDataIdList.Except(downloadPageNodeIdList);
            foreach (var deletePageId in deletePageIdList)
            {
                var index = PageDataList.FindIndex(p => p.NodeId == deletePageId);
                PageDataList.RemoveAt(index);
            }
            PageDataList.OrderBy(p => p.NodeId);
        }
    }

    public enum TextRenderMode
    {
        Auto,       // TMP if available, legacy Text fallback
        TextMeshPro,
        LegacyText,
    }

    [Serializable]
    public class FigmaPageData
    {
        public string Name;
        public string NodeId;
        public bool Selected;

        public FigmaPageData(){}

        public FigmaPageData(string name, string nodeId)
        {
            Name = name;
            NodeId = nodeId;
            Selected = true; // default is true
        }
    }
    
}