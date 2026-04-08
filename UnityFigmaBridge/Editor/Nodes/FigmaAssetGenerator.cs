using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.Components;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.PrototypeFlow;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    /// Root generator for figma file/document. Constructs a native UI system and all assets
    /// </summary>
    public static class FigmaAssetGenerator
    {
        /// <summary>
        /// Builds a native unity UI given input figma data
        /// </summary>
        /// <param name="rootCanvas">Root canvas for generation</param>
        /// <param name="figmaImportProcessData"></param>
        public static void BuildFigmaFile(Canvas rootCanvas, FigmaImportProcessData figmaImportProcessData)
        {
            // Save prefab for each page
            var downloadPageIdList = figmaImportProcessData.SelectedPagesForImport.Select(p => p.id).ToList();
            
            // Cycle through all pages and create
            var createdPages = new List<(Node,GameObject)>();
            foreach (var figmaCanvasNode in figmaImportProcessData.SourceFile.document.children)
            {
                bool includedPageObject = downloadPageIdList.Contains(figmaCanvasNode.id);
                UnityFigmaBridgeImporter.ReportProgressPublic($"Generating Page {figmaCanvasNode.name}", 0);
                var pageGameObject = BuildFigmaPage(figmaCanvasNode, rootCanvas.transform as RectTransform, figmaImportProcessData,includedPageObject);
                createdPages.Add((figmaCanvasNode,pageGameObject));
            }
            
            // Save prefab for each page
            for (var i = 0; i < createdPages.Count; i++)
            {
                if (!downloadPageIdList.Contains(createdPages[i].Item1.id)) continue;
                SaveFigmaPageAsPrefab(createdPages[i].Item1, createdPages[i].Item2, figmaImportProcessData);
            }

            // Destroy pages NOT included in the download list (keep imported ones in scene)
            foreach (var createdPage in createdPages)
            {
                if (!downloadPageIdList.Contains(createdPage.Item1.id))
                    Object.DestroyImmediate(createdPage.Item2);
            }
            
            // Instantiate all components
            ComponentManager.InstantiateAllComponentPrefabs(figmaImportProcessData);

            // Remove all temporary components that were created along the way
            ComponentManager.RemoveAllTemporaryNodeComponents(figmaImportProcessData);
            
            // At the very end, we want to apply figmaNode behaviour where required
            BehaviourBindingManager.BindBehaviours(figmaImportProcessData);
        }


        /// <summary>
        /// Builds an individual page (Canvas object in Figma API)
        /// </summary>
        /// <param name="pageNode"></param>
        /// <param name="parentTransform"></param>
        /// <param name="figmaImportProcessData"></param>
        /// <param name="includedPageObject"></param>
        /// <returns></returns>
        private static GameObject BuildFigmaPage(Node pageNode, RectTransform parentTransform,
            FigmaImportProcessData figmaImportProcessData, bool includedPageObject)
        {
           
            var pageGameObject = new GameObject(pageNode.name, typeof(RectTransform));
            var pageTransform = pageGameObject.transform as RectTransform;
            pageTransform.SetParent(parentTransform, false);
            
            // Setup transform for page
            pageTransform.pivot = new Vector2(0, 1);
            pageTransform.anchorMin = pageTransform.anchorMax = new Vector2(0, 1); // Top left
            
            // Generate all child nodes, respecting section filter if configured
            var sectionFilter = figmaImportProcessData.Settings.SelectedSection;
            foreach (var childNode in pageNode.children)
            {
                // Section filter: if a section is selected, skip non-matching sections
                if (!string.IsNullOrEmpty(sectionFilter) && childNode.type == NodeType.SECTION
                    && childNode.name != sectionFilter)
                    continue;

                // Set section context for folder structure
                if (childNode.type == NodeType.SECTION)
                    FigmaPaths.SetContext(childNode.name, "");

                if (CheckNodeValidForGeneration(childNode, figmaImportProcessData))
                    BuildFigmaNode(childNode, pageTransform, pageNode, 0, figmaImportProcessData, includedPageObject, false);
            }
            FigmaPaths.ClearContext();

            return pageGameObject;
        }

        private static bool CheckNodeValidForGeneration(Node node, FigmaImportProcessData figmaImportProcessData)
        {
            // When ExportOnly is enabled, only import nodes that have export settings
            if (figmaImportProcessData.Settings.OnlyImportExportNodes)
                return node.exportSettings != null && node.exportSettings.Length > 0;

            return true;
        }


        /// <summary>
        /// Build an individual Figma Node - this can be of any type, eg FRAME, RECTANGLE, ELLIPSE, TEXT. Frames at depth 0 are treated as screens 
        /// </summary>
        /// <param name="figmaNode">The source figma node</param>
        /// <param name="parentTransform">The parent transform figmaNode</param>
        /// <param name="parentFigmaNode">The parent figma node</param>
        /// <param name="nodeRecursionDepth">Depth of recursion</param>
        /// <param name="figmaImportProcessData"></param>
        /// <param name="includedPageObject"></param>
        /// <param name="withinComponentDefinition"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static GameObject BuildFigmaNode(Node figmaNode, RectTransform parentTransform,  Node parentFigmaNode,
            int nodeRecursionDepth, FigmaImportProcessData figmaImportProcessData,bool includedPageObject, bool withinComponentDefinition)
        {
            // Set frame context for folder structure when this is a screen node
            if (FigmaDataUtils.IsScreenNode(figmaNode, parentFigmaNode))
                FigmaPaths.CurrentFrameName = figmaNode.name;

            // Create a gameObject for this figma node and parent to parent transform
            var nodeGameObject = new GameObject(figmaNode.name, typeof(RectTransform));
            nodeGameObject.transform.SetParent(parentTransform, false);
            var nodeRectTransform = nodeGameObject.transform as RectTransform;

            // In some cases we want nodes to be substituted a server-rendered bitmap
            var matchingServerRenderEntry = figmaImportProcessData.ServerRenderNodes.FirstOrDefault((testNode) => testNode.SourceNode.id == figmaNode.id);

            // Apply transform. For server render entries, use absolute bounding box
            if (matchingServerRenderEntry!=null) NodeTransformManager.ApplyAbsoluteBoundsFigmaTransform(nodeRectTransform, figmaNode, parentFigmaNode,nodeRecursionDepth >0);
            else NodeTransformManager.ApplyFigmaTransform(nodeRectTransform, figmaNode, parentFigmaNode,nodeRecursionDepth >0);

            // Add on a figmaNode to store the reference to the FIGMA figmaNode id
            nodeGameObject.AddComponent<FigmaNodeObject>().NodeId=figmaNode.id;

            // If this is a Figma mask object we'll add a mask component (but dont render)
            if (figmaNode.isMask)
            {
                var mask=nodeGameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }

            // Handle component instance placeholder
            if (TryHandleComponentInstance(figmaNode, parentFigmaNode, nodeGameObject, figmaImportProcessData))
                return nodeGameObject;

            if (figmaNode.type == NodeType.COMPONENT) withinComponentDefinition = true;

            // Handle server-rendered node substitution
            if (matchingServerRenderEntry != null)
                return HandleServerRenderNode(figmaNode, parentFigmaNode, nodeGameObject, figmaImportProcessData);

            // Build standard node with components, properties, effects, layout, and children
            BuildStandardNode(figmaNode, parentFigmaNode, nodeGameObject, nodeRectTransform,
                nodeRecursionDepth, figmaImportProcessData, includedPageObject, withinComponentDefinition);

            // If this node is not visible, mark the game object as inactive
            if (!figmaNode.visible) nodeGameObject.SetActive(false);

            return nodeGameObject;
        }

        /// <summary>
        /// Checks if node is a component instance with existing definition, and marks with placeholder if so
        /// </summary>
        /// <returns>True if handled as component instance placeholder</returns>
        private static bool TryHandleComponentInstance(Node figmaNode, Node parentFigmaNode, GameObject nodeGameObject, FigmaImportProcessData figmaImportProcessData)
        {
            if (figmaNode.type != NodeType.INSTANCE) return false;
            if (figmaImportProcessData.ComponentData.MissingComponentDefinitionsList.Contains(figmaNode.componentId)) return false;

            nodeGameObject.AddComponent<FigmaComponentNodeMarker>().Initialise(figmaNode.id, parentFigmaNode.id, figmaNode.componentId);
            return true;
        }

        /// <summary>
        /// Handles nodes that should be substituted with server-rendered bitmaps
        /// </summary>
        private static GameObject HandleServerRenderNode(Node figmaNode, Node parentFigmaNode, GameObject nodeGameObject, FigmaImportProcessData figmaImportProcessData)
        {
            nodeGameObject.AddComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                FigmaPaths.GetPathForServerRenderedImage(figmaNode.id, figmaImportProcessData.ServerRenderNodes));

            PrototypeFlowManager.ApplyPrototypeFunctionalityToNode(figmaNode, nodeGameObject, figmaImportProcessData);

            if (figmaNode.type == NodeType.COMPONENT)
                ComponentManager.GenerateComponentAssetFromNode(figmaNode, parentFigmaNode, nodeGameObject, figmaImportProcessData);

            return nodeGameObject;
        }

        /// <summary>
        /// Builds a standard (non-server-render, non-component-instance) node with all properties
        /// </summary>
        private static void BuildStandardNode(Node figmaNode, Node parentFigmaNode, GameObject nodeGameObject, RectTransform nodeRectTransform,
            int nodeRecursionDepth, FigmaImportProcessData figmaImportProcessData, bool includedPageObject, bool withinComponentDefinition)
        {
            // Create and apply unity components and properties
            FigmaNodeManager.CreateUnityComponentsForNode(nodeGameObject, figmaNode, figmaImportProcessData);
            FigmaNodeManager.ApplyUnityComponentPropertiesForNode(nodeGameObject, figmaNode, figmaImportProcessData);

            if (figmaNode.effects != null)
                EffectManager.ApplyAllFigmaEffectsToUnityNode(nodeGameObject, figmaNode, figmaImportProcessData);

            FigmaLayoutManager.ApplyLayoutPropertiesForNode(nodeGameObject, figmaNode, figmaImportProcessData, out var scrollContentGameObject);

            // Build children
            BuildChildNodes(figmaNode, nodeRectTransform, nodeRecursionDepth, figmaImportProcessData,
                includedPageObject, withinComponentDefinition, scrollContentGameObject);

            // Resize scroll content to fit children if no layout mode
            if (scrollContentGameObject != null && figmaNode.layoutMode == Node.LayoutMode.NONE)
            {
                var boundsRect = NodeTransformManager.GetRelativeBoundsForAllChildNodes(figmaNode);
                ((RectTransform)scrollContentGameObject.transform).sizeDelta = new Vector2(boundsRect.xMax, boundsRect.yMax);
            }

            // Apply prototype elements (after children, as some button variations need children)
            PrototypeFlowManager.ApplyPrototypeFunctionalityToNode(figmaNode, nodeGameObject, figmaImportProcessData);

            // Handle node type-specific actions (save as screen/component prefab, register section)
            HandleNodeTypeActions(figmaNode, parentFigmaNode, nodeGameObject, nodeRectTransform,
                figmaImportProcessData, includedPageObject);
        }

        /// <summary>
        /// Builds all child nodes and handles masking and scroll content parenting
        /// </summary>
        private static void BuildChildNodes(Node figmaNode, RectTransform nodeRectTransform,
            int nodeRecursionDepth, FigmaImportProcessData figmaImportProcessData,
            bool includedPageObject, bool withinComponentDefinition, GameObject scrollContentGameObject)
        {
            if (figmaNode.children == null) return;

            // Layer depth limit: if SyncDepth > 0 and we've reached the limit, stop recursing
            var maxDepth = figmaImportProcessData.Settings.SyncDepth;
            if (maxDepth > 0 && nodeRecursionDepth >= maxDepth)
                return;

            Mask activeMaskObject = null;
            foreach (var childNode in figmaNode.children)
            {
                var childGameObject = BuildFigmaNode(childNode, nodeRectTransform, figmaNode,
                    nodeRecursionDepth + 1, figmaImportProcessData, includedPageObject, withinComponentDefinition);
                if (childGameObject == null) continue;

                var childGameObjectMask = childGameObject.GetComponent<Mask>();
                if (childGameObjectMask != null)
                {
                    activeMaskObject = childGameObjectMask;
                }
                else
                {
                    if (activeMaskObject != null) childGameObject.transform.SetParent(activeMaskObject.transform, true);
                    if (scrollContentGameObject != null) childGameObject.transform.SetParent(scrollContentGameObject.transform, true);
                }
            }
        }

        /// <summary>
        /// Handles node type-specific actions like saving prefabs or registering sections
        /// </summary>
        private static void HandleNodeTypeActions(Node figmaNode, Node parentFigmaNode, GameObject nodeGameObject,
            RectTransform nodeRectTransform, FigmaImportProcessData figmaImportProcessData, bool includedPageObject)
        {
            switch (figmaNode.type)
            {
                case NodeType.FRAME:
                    if (includedPageObject && FigmaDataUtils.IsScreenNode(figmaNode, parentFigmaNode))
                        SaveFigmaScreenAsPrefab(figmaNode, parentFigmaNode, nodeRectTransform, figmaImportProcessData);
                    break;
                case NodeType.COMPONENT:
                    ComponentManager.GenerateComponentAssetFromNode(figmaNode, parentFigmaNode, nodeGameObject, figmaImportProcessData);
                    break;
                case NodeType.SECTION:
                    RegisterFigmaSection(figmaNode, figmaImportProcessData);
                    break;
            }
        }


        /// <summary>
        /// Create a flowScreen prefab from a generated figma asset
        /// </summary>
        /// <param name="node"></param>
        /// <param name="parentNode"></param>
        /// <param name="screenRectTransform"></param>
        /// <param name="figmaImportProcessData"></param>
        private static void SaveFigmaScreenAsPrefab(Node node, Node parentNode,RectTransform screenRectTransform, FigmaImportProcessData figmaImportProcessData)
        {
            var screenNameCount = figmaImportProcessData.ScreenPrefabNameCounter.TryGetValue(node.name, out var value)
                ? value : 0;
            
            // Increment count to ensure no naming collisions
            figmaImportProcessData.ScreenPrefabNameCounter[node.name] = screenNameCount + 1;
            
            // We want prefab to be stored with a default position, so reset and restore
            var current = screenRectTransform.anchoredPosition;
            screenRectTransform.anchoredPosition = Vector2.zero;
            // Write prefab
            var screenPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(screenRectTransform.gameObject,
                    FigmaPaths.GetPathForScreenPrefab(node,screenNameCount), InteractionMode.UserAction);
            // Restore original position
            screenRectTransform.anchoredPosition = current;

            // If we are building the prototype flow, add this to the current flowScreen controller
            if (figmaImportProcessData.Settings.BuildPrototypeFlow)
            {
                figmaImportProcessData.PrototypeFlowController.RegisterFigmaScreen(new FigmaFlowScreen
                {
                    FigmaScreenPrefab = screenPrefab,
                    FigmaNodeId = node.id,
                    FigmaScreenName = FigmaPaths.GetFileNameForNode(node, screenNameCount),
                    // Store the section that this is part of (if applicable)
                    ParentSectionNodeId = parentNode is { type: NodeType.SECTION } ? parentNode.id : string.Empty
                });
            }
            
            figmaImportProcessData.ScreenPrefabs.Add(screenPrefab);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="pageGameObject"></param>
        /// <param name="figmaImportProcessData"></param>
        private static void SaveFigmaPageAsPrefab(Node node, GameObject pageGameObject, FigmaImportProcessData figmaImportProcessData)
        {
           
            var pageNameCount = figmaImportProcessData.PagePrefabNameCounter.TryGetValue(node.name, out var value)
                ? value : 0;
            
            // Increment count to ensure no naming collisions
            figmaImportProcessData.PagePrefabNameCounter[node.name] = pageNameCount + 1;

            var pagePrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(pageGameObject,
                FigmaPaths.GetPathForPagePrefab(node,pageNameCount),InteractionMode.UserAction);
            figmaImportProcessData.PagePrefabs.Add(pagePrefab);
        }





        /// <summary>
        /// Registers a figma section. This is needed for flow controller to properly transition between sections
        /// </summary>
        /// <param name="node"></param>
        /// <param name="figmaImportProcessData"></param>
        private static void RegisterFigmaSection(Node node, FigmaImportProcessData figmaImportProcessData)
        {
            if (figmaImportProcessData.Settings.BuildPrototypeFlow)
            {
                
                // We want to find the default start point for this section
                var prototypeFlowStartNodeId = string.Empty;
                var prototypeFlowStartNodeName = string.Empty;
                // Search through all start points and see if they are a child of this section
                foreach (var flowStartId in figmaImportProcessData.PrototypeFlowStartPoints)
                {
                    var matchingNode = FigmaDataUtils.GetFigmaNodeInChildren(node, flowStartId);
                    if (matchingNode == null) continue;
                    // We've found a match
                    prototypeFlowStartNodeId = flowStartId;
                    prototypeFlowStartNodeName = matchingNode.name;
                }
                
                figmaImportProcessData.PrototypeFlowController.RegisterFigmaSection(new FigmaSection
                {
                    FigmaNodeId = node.id,
                    FigmaPrototypeFlowStartNodeId = prototypeFlowStartNodeId,
                    FigmaPrototypeFlowStartNodeName= prototypeFlowStartNodeName,
                    FigmaNodeName = node.name,
                });
            }
        }
    }

}