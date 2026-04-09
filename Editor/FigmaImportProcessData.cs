using System.Collections.Generic;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    /// Wrapper for all data regarding current import process
    /// </summary>
    public class FigmaImportProcessData
    {
        /// <summary>
        /// The current importer settings
        /// </summary>
        public UnityFigmaBridgeSettings Settings;
        /// <summary>
        /// The source FIGMA file
        /// </summary>
        public FigmaFile SourceFile;

        /// <summary>
        /// Mapping of document fonts to TextMeshPro fonts and material variants
        /// </summary>
        public FigmaFontMap FontMap;
        
        /// <summary>
        /// Nodes that should be used for server-side rendering substitution
        /// </summary>
        public List<ServerRenderNodeData> ServerRenderNodes = new List<ServerRenderNodeData>();
        
        /// <summary>
        /// this is set when the figma unity UI document is generated
        /// </summary>
        public PrototypeFlowController PrototypeFlowController;

        /// <summary>
        /// Generated page prefabs
        /// </summary>
        public List<GameObject> PagePrefabs = new();
        
        /// <summary>
        /// Generated screens
        /// </summary>
        public List<GameObject> ScreenPrefabs = new List<GameObject>();
        
        /// <summary>
        /// Count of flowScreen prefabs created with a specific name (to prevent name collision)
        /// </summary>
        public Dictionary<string, int> ScreenPrefabNameCounter = new();
        
        /// <summary>
        /// Count of page prefab created with a specific name (to prevent name collision)
        /// </summary>
        public Dictionary<string, int> PagePrefabNameCounter = new();

        /// <summary>
        /// List of all prototype flow starting points
        /// </summary>
        public List<string> PrototypeFlowStartPoints = new();

        /// <summary>
        /// List of all page nodes to import
        /// </summary>
        public List<Node> SelectedPagesForImport = new();
        
        /// <summary>
        /// Allow faster lookup of nodes by ID
        /// </summary>
        public Dictionary<string,Node> NodeLookupDictionary = new();
    }

}