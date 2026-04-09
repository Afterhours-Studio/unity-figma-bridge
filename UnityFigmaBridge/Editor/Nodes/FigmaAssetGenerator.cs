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
                FigmaPaths.GetPathForScreenPrefab(node,pageNameCount),InteractionMode.UserAction);
            figmaImportProcessData.PagePrefabs.Add(pagePrefab);
        }





        /// <summary>
        /// Build a single frame into the scene as clean Unity UI (used by Build tab).
        /// No Figma scripts — only standard Image, TextMeshProUGUI, LayoutGroups.
        /// All INSTANCE nodes are built inline as full node trees.
        /// </summary>
        public static void BuildSingleFrame(Canvas rootCanvas, Node frameNode, Node parentNode,
            FigmaImportProcessData figmaImportProcessData)
        {
            if (parentNode != null && parentNode.type == NodeType.SECTION)
                FigmaPaths.SetContext(parentNode.name, "");
            FigmaPaths.CurrentFrameName = frameNode.name;

            // Root frame = empty RectTransform container, stretched full screen
            var go = new GameObject(frameNode.name, typeof(RectTransform));
            go.transform.SetParent(rootCanvas.transform, false);
            var rt = go.transform as RectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Build children into the container
            if (frameNode.children != null)
            {
                foreach (var child in frameNode.children)
                    BuildCleanNode(child, rt, frameNode, 1, figmaImportProcessData);
            }

            FigmaPaths.ClearContext();
        }

        /// <summary>
        /// Recursively build a clean Unity UI node — no Figma scripts.
        /// </summary>
        private static GameObject BuildCleanNode(Node node, RectTransform parentTransform,
            Node parentNode, int depth, FigmaImportProcessData processData)
        {
            if (!node.visible) return null;

            // Parse convention tags: [Button], [RectMask2D], [ScrollRect]
            var nodeName = node.name;
            var tagButton = false;
            var tagRectMask = false;
            var tagScrollRect = false;
            while (nodeName.StartsWith("["))
            {
                var close = nodeName.IndexOf(']');
                if (close < 0) break;
                var tag = nodeName.Substring(1, close - 1);
                nodeName = nodeName.Substring(close + 1).TrimStart();
                switch (tag.ToLower())
                {
                    case "button": tagButton = true; break;
                    case "rectmask2d": tagRectMask = true; break;
                    case "scrollrect": tagScrollRect = true; break;
                }
            }
            if (string.IsNullOrWhiteSpace(nodeName)) nodeName = node.name;

            var go = new GameObject(nodeName, typeof(RectTransform));
            go.transform.SetParent(parentTransform, false);
            var rt = go.transform as RectTransform;

            // Check if this node was server-rendered (based on depth/export settings from import).
            // If server-rendered image exists on disk, use it and don't recurse.
            // If image NOT on disk, fall through to normal build.
            var serverEntry = processData.ServerRenderNodes.Find(s => s.SourceNode.id == node.id);
            if (serverEntry != null)
            {
                var path = FigmaPaths.GetPathForServerRenderedImage(node.id, processData.ServerRenderNodes);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    Debug.LogWarning($"[Build] Server render not found: '{node.name}' ({node.id}) → {path}");
                if (sprite != null)
                {
                    if (node.absoluteRenderBounds != null)
                        ApplyRenderBoundsTransform(rt, node, parentNode);
                    else
                    {
                        NodeTransformManager.ApplyAbsoluteBoundsFigmaTransform(rt, node, parentNode, depth > 0);
                        ExpandBoundsForEffects(rt, node);
                    }
                    var img = go.AddComponent<Image>();
                    img.sprite = sprite;
                    if (node.opacity < 1)
                        go.AddComponent<CanvasGroup>().alpha = node.opacity;
                    return go;
                }
                // No image on disk — fall through to normal build
            }

            // Normal node — apply transform (clean, no LayoutElement)
            ApplyCleanTransform(rt, node, depth > 0);

            // Figma mask node
            if (node.isMask)
            {
                var mask = go.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }

            // Apply visual: Image or TextMeshProUGUI
            bool hasImageFill = ApplyCleanVisual(go, node, processData);

            // If node has an IMAGE fill loaded successfully and no text children,
            // the image IS the complete visual — don't build redundant shape children.
            bool skipChildren = hasImageFill && !ContainsTextNode(node);

            if (!skipChildren)
            {
                // Apply layout (auto-layout → LayoutGroup, scroll → ScrollRect)
                FigmaLayoutManager.ApplyLayoutPropertiesForNode(go, node, processData, out var scrollContent);

                // Clip content
                if (node.clipsContent && node.type == NodeType.FRAME)
                    UnityUiUtils.GetOrAddComponent<RectMask2D>(go);

                // Recurse children
                if (node.children != null)
                {
                    var maxDepth = processData.Settings.SyncDepth;
                    if (maxDepth == 0 || depth < maxDepth)
                    {
                        Mask activeMask = null;
                        foreach (var child in node.children)
                        {
                            var childGo = BuildCleanNode(child, rt, node, depth + 1, processData);
                            if (childGo == null) continue;

                            var childMask = childGo.GetComponent<Mask>();
                            if (childMask != null)
                                activeMask = childMask;
                            else
                            {
                                if (activeMask != null)
                                    childGo.transform.SetParent(activeMask.transform, true);
                                if (scrollContent != null)
                                    childGo.transform.SetParent(scrollContent.transform, true);
                            }
                        }
                    }
                }
            }

            // Opacity
            if (node.opacity < 1)
            {
                var cg = go.AddComponent<CanvasGroup>();
                cg.alpha = node.opacity;
            }

            // Convention-based components
            if (tagRectMask)
                UnityUiUtils.GetOrAddComponent<RectMask2D>(go);

            if (tagButton)
            {
                var img = go.GetComponent<Image>();
                if (img == null)
                {
                    img = go.AddComponent<Image>();
                    img.color = new UnityEngine.Color(1, 1, 1, 0);
                }
                go.AddComponent<Button>();
            }

            if (tagScrollRect)
            {
                // Viewport
                var viewport = new GameObject("Viewport", typeof(RectTransform));
                viewport.transform.SetParent(go.transform, false);
                var vpRt = viewport.transform as RectTransform;
                vpRt.anchorMin = Vector2.zero;
                vpRt.anchorMax = Vector2.one;
                vpRt.offsetMin = Vector2.zero;
                vpRt.offsetMax = Vector2.zero;
                viewport.AddComponent<RectMask2D>();

                // Content
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                var cRt = content.transform as RectTransform;
                cRt.anchorMin = new Vector2(0, 1);
                cRt.anchorMax = new Vector2(1, 1);
                cRt.pivot = new Vector2(0.5f, 1);
                cRt.offsetMin = Vector2.zero;
                cRt.offsetMax = Vector2.zero;
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Reparent all children into Content
                var childrenToMove = new List<Transform>();
                for (int c = 0; c < go.transform.childCount; c++)
                {
                    var child = go.transform.GetChild(c);
                    if (child != viewport.transform)
                        childrenToMove.Add(child);
                }
                foreach (var child in childrenToMove)
                    child.SetParent(content.transform, true);

                // ScrollRect
                var scroll = go.AddComponent<ScrollRect>();
                scroll.viewport = vpRt;
                scroll.content = cRt;
                scroll.horizontal = false;
                scroll.vertical = true;
            }

            return go;
        }

        /// <summary>
        /// Apply RectTransform position, size, rotation, mirror from Figma node. No LayoutElement.
        /// </summary>
        private static void ApplyCleanTransform(RectTransform rt, Node node, bool centerPivot)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);

            if (node.relativeTransform != null)
            {
                rt.anchoredPosition = new Vector2(
                    node.relativeTransform[0, 2],
                    -node.relativeTransform[1, 2]);
                var rotation = Mathf.Rad2Deg *
                    Mathf.Atan2(-node.relativeTransform[1, 0], node.relativeTransform[0, 0]);
                rt.localRotation = Quaternion.Euler(0, 0, rotation);
            }

            if (node.relativeTransform[0, 0] < 0)
            {
                rt.localScale = new Vector3(-rt.localScale.x, rt.localScale.y, rt.localScale.z);
                rt.localRotation = Quaternion.Euler(rt.rotation.eulerAngles.x, rt.rotation.eulerAngles.y,
                    rt.rotation.eulerAngles.z - 180);
            }
            if (node.relativeTransform[1, 1] < 0)
                rt.localScale = new Vector3(rt.localScale.x, -rt.localScale.y, rt.localScale.z);

            rt.sizeDelta = new Vector2(node.size.x, node.size.y);

            if (node.type == NodeType.TEXT) centerPivot = false;
            if (centerPivot) NodeTransformManager.SetPivot(rt, new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Expand RectTransform bounds to account for effects that extend beyond absoluteBoundingBox.
        /// Used as fallback when absoluteRenderBounds is not available.
        /// </summary>
        private static void ExpandBoundsForEffects(RectTransform rt, Node node)
        {
            float expand = 0;

            // Outside/center stroke
            if (node.strokeWeight > 0)
            {
                if (node.strokeAlign == Node.StrokeAlign.OUTSIDE)
                    expand = Mathf.Max(expand, node.strokeWeight);
                else if (node.strokeAlign == Node.StrokeAlign.CENTER)
                    expand = Mathf.Max(expand, node.strokeWeight * 0.5f);
            }

            // Drop shadow, layer blur
            if (node.effects != null)
            {
                foreach (var effect in node.effects)
                {
                    if (!effect.visible) continue;
                    switch (effect.type)
                    {
                        case Effect.EffectType.DROP_SHADOW:
                            var ox = effect.offset != null ? Mathf.Abs(effect.offset.x) : 0f;
                            var oy = effect.offset != null ? Mathf.Abs(effect.offset.y) : 0f;
                            var shadowExtent = Mathf.Max(ox, oy) + effect.radius + Mathf.Max(0, effect.spread);
                            expand = Mathf.Max(expand, shadowExtent);
                            break;
                        case Effect.EffectType.LAYER_BLUR:
                        case Effect.EffectType.BACKGROUND_BLUR:
                            expand = Mathf.Max(expand, effect.radius);
                            break;
                    }
                }
            }

            if (expand <= 0) return;

            rt.sizeDelta += new Vector2(expand * 2, expand * 2);
            rt.anchoredPosition += new Vector2(-expand, expand);
        }

        /// <summary>
        /// Recursively check if a node or any of its descendants is a TEXT node.
        /// </summary>
        private static bool ContainsTextNode(Node node)
        {
            if (node.type == NodeType.TEXT) return true;
            if (node.children == null) return false;
            foreach (var child in node.children)
            {
                if (ContainsTextNode(child)) return true;
            }
            return false;
        }

        /// <summary>
        /// Apply transform using absoluteRenderBounds — accounts for shadows, blur, outside strokes.
        /// </summary>
        private static void ApplyRenderBoundsTransform(RectTransform rt, Node node, Node parentNode)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);

            var rb = node.absoluteRenderBounds;
            rt.sizeDelta = new Vector2(rb.width, rb.height);

            var parentPos = parentNode?.absoluteBoundingBox != null
                ? new Vector2(parentNode.absoluteBoundingBox.x, parentNode.absoluteBoundingBox.y)
                : Vector2.zero;

            rt.anchoredPosition = new Vector2(rb.x - parentPos.x, -(rb.y - parentPos.y));
        }

        /// <summary>
        /// Apply clean visual components — standard Image or TextMeshProUGUI only.
        /// Returns true if an IMAGE fill sprite was loaded (node visual is complete, children are redundant).
        /// </summary>
        private static bool ApplyCleanVisual(GameObject go, Node node, FigmaImportProcessData processData)
        {
            switch (node.type)
            {
                case NodeType.TEXT:
                    ApplyCleanText(go, node, processData);
                    return false;

                case NodeType.FRAME:
                case NodeType.RECTANGLE:
                case NodeType.ELLIPSE:
                case NodeType.STAR:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                case NodeType.SECTION:
                case NodeType.GROUP:
                    return ApplyCleanImage(go, node, processData);

                case NodeType.VECTOR:
                case NodeType.BOOLEAN_OPERATION:
                    var serverEntry = processData.ServerRenderNodes.Find(
                        s => s.SourceNode.id == node.id);
                    if (serverEntry != null)
                    {
                        var path = FigmaPaths.GetPathForServerRenderedImage(
                            node.id, processData.ServerRenderNodes);
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite != null)
                        {
                            var img = go.AddComponent<Image>();
                            img.sprite = sprite;
                            img.preserveAspect = true;
                            return true;
                        }
                    }
                    return false;
            }
            return false;
        }

        /// <summary>
        /// Apply a standard Unity Image for solid color or image fills.
        /// Returns true if an IMAGE fill sprite was loaded successfully.
        /// </summary>
        private static bool ApplyCleanImage(GameObject go, Node node, FigmaImportProcessData processData)
        {
            if (node.fills == null || node.fills.Length == 0)
            {
                var serverEntry = processData.ServerRenderNodes.Find(s => s.SourceNode.id == node.id);
                if (serverEntry != null)
                {
                    var path = FigmaPaths.GetPathForServerRenderedImage(node.id, processData.ServerRenderNodes);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null)
                    {
                        var sImg = go.AddComponent<Image>();
                        sImg.sprite = sprite;
                        return true;
                    }
                }
                return false;
            }

            var fill = node.fills[0];
            if (!fill.visible) return false;

            var img = go.AddComponent<Image>();

            switch (fill.type)
            {
                case Paint.PaintType.IMAGE when !string.IsNullOrEmpty(fill.imageRef):
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                        FigmaPaths.GetPathForImageFill(fill.imageRef, node.name));
                    if (sprite == null)
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                            FigmaPaths.GetPathForImageFill(fill.imageRef));
                    if (sprite != null)
                    {
                        img.sprite = sprite;
                        img.type = Image.Type.Simple;
                        img.preserveAspect = fill.scaleMode == Paint.ScaleMode.FIT;
                        return true;
                    }
                    img.color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;

                case Paint.PaintType.SOLID:
                    img.color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;

                default:
                    img.color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;
            }
        }

        /// <summary>
        /// Apply TextMeshProUGUI with all text properties from Figma.
        /// </summary>
        private static void ApplyCleanText(GameObject go, Node node, FigmaImportProcessData processData)
        {
            if (node.style == null) return;

            var text = go.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = node.characters;
            text.fontSize = node.style.fontSize;
            text.characterSpacing = -0.7f;

            // Color
            text.color = node.fills != null && node.fills.Length > 0
                ? FigmaDataUtils.GetUnityFillColor(node.fills[0])
                : UnityEngine.Color.white;

            // Font
            var fontMapping = processData.FontMap.GetFontMapping(
                node.style.fontFamily, node.style.fontWeight);
            if (fontMapping?.FontAsset != null)
                text.font = fontMapping.FontAsset;

            // Horizontal alignment
            text.horizontalAlignment = node.style.textAlignHorizontal switch
            {
                TypeStyle.TextAlignHorizontal.LEFT => TMPro.HorizontalAlignmentOptions.Left,
                TypeStyle.TextAlignHorizontal.CENTER => TMPro.HorizontalAlignmentOptions.Center,
                TypeStyle.TextAlignHorizontal.RIGHT => TMPro.HorizontalAlignmentOptions.Right,
                TypeStyle.TextAlignHorizontal.JUSTIFIED => TMPro.HorizontalAlignmentOptions.Justified,
                _ => TMPro.HorizontalAlignmentOptions.Left,
            };

            // Vertical alignment
            text.verticalAlignment = node.style.textAlignVertical switch
            {
                TypeStyle.TextAlignVertical.TOP => TMPro.VerticalAlignmentOptions.Top,
                TypeStyle.TextAlignVertical.CENTER => TMPro.VerticalAlignmentOptions.Middle,
                TypeStyle.TextAlignVertical.BOTTOM => TMPro.VerticalAlignmentOptions.Bottom,
                _ => TMPro.VerticalAlignmentOptions.Top,
            };

            // Style
            if (node.style.italic) text.fontStyle |= TMPro.FontStyles.Italic;

            text.fontStyle |= node.style.textCase switch
            {
                TypeStyle.TextCase.UPPER => TMPro.FontStyles.UpperCase,
                TypeStyle.TextCase.LOWER => TMPro.FontStyles.LowerCase,
                TypeStyle.TextCase.SMALL_CAPS => TMPro.FontStyles.SmallCaps,
                _ => 0,
            };

            text.fontStyle |= node.style.textDecoration switch
            {
                TypeStyle.TextDecoration.UNDERLINE => TMPro.FontStyles.Underline,
                TypeStyle.TextDecoration.STRIKETHROUGH => TMPro.FontStyles.Strikethrough,
                _ => 0,
            };

            // Auto resize
            if (node.style.textAutoResize != TypeStyle.TextAutoResize.NONE)
            {
                var fitter = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(go);
                switch (node.style.textAutoResize)
                {
                    case TypeStyle.TextAutoResize.HEIGHT:
                        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        break;
                    case TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT:
                        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        break;
                }
            }
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