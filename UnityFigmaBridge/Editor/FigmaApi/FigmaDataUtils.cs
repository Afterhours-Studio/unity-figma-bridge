using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    /// Utilities to convert from Figma data types to Unity data types, and query Figma Data structures
    /// </summary>
    public static class FigmaDataUtils
    {
        /// <summary>
        /// Keyword in node name that triggers server-side rendering
        /// </summary>
        private const string SERVER_RENDER_KEYWORD = "render";


        /// <summary>
        /// Converts from Figma Paint Fill Color to Unity color
        /// </summary>
        /// <param name="paint"></param>
        /// <returns></returns>
        public static UnityEngine.Color GetUnityFillColor(Paint paint)
        {
            // Make sure 
            if(paint != null && !paint.visible) return new UnityEngine.Color(0,0,0,0);
            return paint?.color == null ? new UnityEngine.Color(1,1,1,paint?.opacity ?? 1) : new UnityEngine.Color(paint.color.r, paint.color.g, paint.color.b, paint.color.a*paint.opacity);
        }

        /// <summary>
        /// Create a Unity Gradient from Figma gradient
        /// </summary>
        /// <param name="fill"></param>
        /// <returns></returns>
        public static Gradient ToUnityGradient(Paint fill)
        {
            var figmaGradientStops = fill.gradientStops;
            
            // Create array of keys for gradient color and alpha
            var unityColorKeys = new GradientColorKey[figmaGradientStops.Length];
            var unityAlphaKeys = new GradientAlphaKey[figmaGradientStops.Length];

            // Cycle through figma gradient and convert keys to Unity
            for (var i = 0; i < figmaGradientStops.Length; i++)
            {
                unityColorKeys[i].color = ToUnityColor(figmaGradientStops[i].color);
                unityColorKeys[i].time = figmaGradientStops[i].position;
                unityAlphaKeys[i].alpha = figmaGradientStops[i].color.a;
                unityAlphaKeys[i].time=figmaGradientStops[i].position;
            }

            // Create new Unity gradient
            var gradient = new Gradient
            {
                mode = GradientMode.Blend
            };
            gradient.SetKeys(unityColorKeys, unityAlphaKeys);
            return gradient;
        }
        
        /// <summary>
        /// Convert Figma Vector2 to Unity Vector2
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static Vector2 ToUnityVector(Vector vector)
        {
            return new Vector2(vector.x, vector.y);
        }

        /// <summary>
        /// Convert Figma Color to Unity Color
        /// </summary>
        /// <param name="color"></param>
        /// <returns></returns>
        public static UnityEngine.Color ToUnityColor(Color color)
        {
            return new UnityEngine.Color(color.r, color.g, color.b, color.a);
        }

        /// <summary>
        /// Convert to array of Unity Vector3
        /// </summary>
        /// <param name="inputArray"></param>
        /// <returns></returns>
        public static Vector3[] ToUnityVector3Array(float[,] inputArray)
        {
            var length=inputArray.GetLength(0);
            var outputArray = new Vector3[length];
            for (var i = 0; i < length; i++)
            {
                outputArray[i] = new Vector3(inputArray[i,0], inputArray[i,1], inputArray[i,2]);
            }
            return outputArray;
        }
        
        
        /// <summary>
        /// Create a fast-lookup dictionary
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static Dictionary<string, Node> BuildNodeLookupDictionary(FigmaFile file)
        {
            var dictionary = new Dictionary<string, Node>();
            PopulateDictionaryWithNodes(dictionary, file.document);
            return dictionary;
        }

        /// <summary>
        /// Recursively populate dictionary with all nodes in a figma file
        /// </summary>
        /// <param name="dictionary"></param>
        /// <param name="node"></param>
        private static void PopulateDictionaryWithNodes(Dictionary<string, Node> dictionary, Node node)
        {
            dictionary[node.id] = node;
            if (node.children == null) return;
            foreach (var childNode in node.children)
            {
                PopulateDictionaryWithNodes(dictionary, childNode);
            }
        }

        /// <summary>
        /// Fast lookup of a node by ID using a pre-built dictionary
        /// </summary>
        public static Node GetFigmaNodeWithId(Dictionary<string, Node> nodeLookup, string nodeId)
        {
            return nodeLookup.TryGetValue(nodeId, out var node) ? node : null;
        }

        /// <summary>
        /// Searches a Figma file to find a specific figmaNode
        /// Note - this is slow, so avoid if possible. Prefer the dictionary-based overload.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public static Node GetFigmaNodeWithId(FigmaFile file, string nodeId)
        {
            return GetFigmaNodeInChildren(file.document,nodeId);
        }


      

        /// <summary>
        /// Find a specific figmaNode within figma figmaNode tree (recursive)
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public static Node GetFigmaNodeInChildren(Node node,string nodeId)
        {
            if (node.children == null) return null;
            foreach (var childNode in node.children)
            {
                if (childNode.id == nodeId) return childNode;
                var nodeFoundInChildren = GetFigmaNodeInChildren(childNode, nodeId);
                if (nodeFoundInChildren != null) return nodeFoundInChildren;
            }
            // Not found
            return null;
        }

        /// <summary>
        /// Returns the full hierarchical path for a given node in a document - helpful for debugging
        /// </summary>
        /// <param name="node"></param>
        /// <param name="figmaFile"></param>
        /// <returns></returns>
        public static string GetFullPathForNode(Node node,FigmaFile figmaFile)
        {
            var pathStack = new Stack<string>();
            var found=GetPathForNodeRecursive(figmaFile.document, node,pathStack );
            return string.Join("/",pathStack.Reverse());
        }
        
        /// <summary>
        /// Recursively search for a node in a figma file and push/pop to stack to track heirarchy
        /// </summary>
        /// <param name="searchNode"></param>
        /// <param name="targetNode"></param>
        /// <param name="pathStack"></param>
        /// <returns></returns>
        private static bool GetPathForNodeRecursive(Node searchNode, Node targetNode, Stack<string> pathStack)
        {
            pathStack.Push(searchNode.name);
            if (searchNode == targetNode) return true;
            if (searchNode.children != null)
            {
                foreach (var childNode in searchNode.children)
                {
                    if (GetPathForNodeRecursive(childNode, targetNode, pathStack)) return true;
                }
            }
            pathStack.Pop(); // Not found, remove from stack
            return false;
        }
       
        
        
        /// <summary>
        /// Replace any characters that are invlid for saving
        /// </summary>
        /// <param name="NodeId"></param>
        /// <returns></returns>
        public static string ReplaceUnsafeFileCharactersForNodeId (string NodeId)
        {
            return NodeId.Replace(":", "_");
        }


        /// <summary>
        /// Get all figma fills from within a figma file
        /// </summary>
        /// <param name="file"></param>
        /// <param name="downloadPageIdList"></param>
        /// <returns></returns>
        /// <summary>
        /// Context info for where an image fill belongs in the folder structure.
        /// </summary>
        public struct ImageFillContext
        {
            public string SectionName;
            public string FrameName;
            public string NodeName;
        }

        public static Dictionary<string, ImageFillContext> GetAllImageFillIdsFromFile(FigmaFile file,
            List<string> downloadPageIdList, string sectionFilter = "", bool exportOnly = false,
            List<string> selectedFrameIds = null)
        {
            var imageFillMap = new Dictionary<string, ImageFillContext>();
            foreach (var page in file.document.children)
            {
                var includedPage = downloadPageIdList.Contains(page.id);
                GetAllImageFillIdsForNode(page, imageFillMap, 0, includedPage, false,
                    sectionFilter, exportOnly, "", "", selectedFrameIds);
            }
            return imageFillMap;
        }

        private static void GetAllImageFillIdsForNode(Node node, Dictionary<string, ImageFillContext> imageFillMap,
            int recursiveDepth, bool includedPage, bool withinComponentDefinition,
            string sectionFilter, bool exportOnly, string currentSection, string currentFrame,
            List<string> selectedFrameIds)
        {
            // Section filter
            if (recursiveDepth == 1 && !string.IsNullOrEmpty(sectionFilter))
            {
                if (node.type != NodeType.SECTION || node.name != sectionFilter)
                    return;
            }

            // Track section/frame context
            if (node.type == NodeType.SECTION) currentSection = node.name;
            if (IsScreenNode(node, null) || (recursiveDepth == 2 && node.type == NodeType.FRAME))
                currentFrame = node.name;

            // Frame filter: if specific frames selected, skip non-matching screen frames
            if (selectedFrameIds != null && selectedFrameIds.Count > 0
                && (IsScreenNode(node, null) || (recursiveDepth == 2 && node.type == NodeType.FRAME)))
            {
                if (!selectedFrameIds.Contains(node.id)) return;
            }

            // Export-only: skip non-export nodes but ALWAYS recurse into containers
            if (exportOnly && recursiveDepth > 1
                && node.type != NodeType.CANVAS && node.type != NodeType.SECTION
                && node.type != NodeType.FRAME && node.type != NodeType.COMPONENT
                && node.type != NodeType.INSTANCE && node.type != NodeType.GROUP)
            {
                if (node.exportSettings == null || node.exportSettings.Length == 0)
                    return;
            }

            // Collect image fills
            var ignoreNodeFill = recursiveDepth <= 1 && node.type != NodeType.FRAME && node.type != NodeType.COMPONENT;
            if (!includedPage && !withinComponentDefinition) ignoreNodeFill = true;
            if (node.fills != null && !ignoreNodeFill)
            {
                foreach (var fill in node.fills)
                {
                    if (fill == null || fill.type != Paint.PaintType.IMAGE) continue;
                    if (string.IsNullOrEmpty(fill.imageRef)) continue;
                    if (!imageFillMap.ContainsKey(fill.imageRef))
                        imageFillMap[fill.imageRef] = new ImageFillContext
                        {
                            SectionName = currentSection,
                            FrameName = currentFrame,
                            NodeName = node.name,
                        };
                }
            }

            if (node.type == NodeType.COMPONENT || node.type == NodeType.INSTANCE)
                withinComponentDefinition = true;

            if (node.children == null) return;
            foreach (var childNode in node.children)
                GetAllImageFillIdsForNode(childNode, imageFillMap, recursiveDepth + 1,
                    includedPage, withinComponentDefinition, sectionFilter, exportOnly,
                    currentSection, currentFrame, selectedFrameIds);
        }
        
        /// <summary>
        /// Recursively search for nodes of a specific type
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeType"></param>
        /// <param name="nodeList"></param>
        /// <param name="nodeDepth"></param>
        public static void FindAllNodesOfType(Node node,NodeType nodeType,List<Node> nodeList,
            int nodeDepth)
        {
            if (node.type == nodeType)  nodeList.Add(node);
            if (node.children == null) return;

            foreach (var childNode in node.children)
                FindAllNodesOfType(childNode, nodeType,nodeList,nodeDepth+1);
        }

        /// <summary>
        /// Finds all components of a specific iD
        /// </summary>
        /// <param name="node"></param>
        /// <param name="componentId"></param>
        /// <param name="nodeList"></param>
        /// <param name="nodeDepth"></param>
        private static void FindAllComponentInstances(Node node,string componentId,List<Node> nodeList, int nodeDepth)
        {
            if (node.type == NodeType.INSTANCE && node.componentId==componentId)  nodeList.Add(node);
            if (node.children == null) return;

            foreach (var childNode in node.children)
                FindAllComponentInstances(childNode, componentId,nodeList,nodeDepth+1);
        }


        /// <summary>
        /// Find all nodes within a document that we need to render server-side
        /// </summary>
        /// <param name="file">Figma document</param>
        /// <param name="missingComponentIds"></param>
        /// <param name="downloadPageIdList"></param>
        /// <returns>List of figmaNode IDs to replace</returns>
        public static List<ServerRenderNodeData> FindAllServerRenderNodesInFile(FigmaFile file,
            List<string> missingComponentIds, List<string> downloadPageIdList,
            string sectionFilter = "", bool exportOnly = false, int syncDepth = 0,
            List<string> selectedFrameIds = null)
        {
            var renderSubstitutionNodeList = new List<ServerRenderNodeData>();
            foreach (var page in file.document.children)
            {
                var isSelectedPage = downloadPageIdList.Contains(page.id);
                AddRenderSubstitutionsForFigmaNode(page, renderSubstitutionNodeList, 0,
                    missingComponentIds, isSelectedPage, false, sectionFilter, exportOnly, "", "",
                    syncDepth, selectedFrameIds);
            }

            return renderSubstitutionNodeList;
        }

        private static void AddRenderSubstitutionsForFigmaNode(Node figmaNode,
            List<ServerRenderNodeData> substitutionNodeList, int recursiveNodeDepth,
            List<string> missingComponentIds, bool isSelectedPage, bool withinComponentDefinition,
            string sectionFilter, bool exportOnly, string currentSection, string currentFrame,
            int syncDepth, List<string> selectedFrameIds)
        {
            if (!figmaNode.visible) return;

            var hasExport = figmaNode.exportSettings != null && figmaNode.exportSettings.Length > 0;

            // Section filter
            if (recursiveNodeDepth == 1 && !string.IsNullOrEmpty(sectionFilter))
            {
                if (figmaNode.type != NodeType.SECTION || figmaNode.name != sectionFilter)
                    return;
            }

            // Track section/frame context
            if (figmaNode.type == NodeType.SECTION) currentSection = figmaNode.name;

            var isScreenFrame = IsScreenNode(figmaNode, null)
                || (recursiveNodeDepth == 2 && figmaNode.type == NodeType.FRAME);
            if (isScreenFrame) currentFrame = figmaNode.name;

            // Frame filter: skip non-selected screen frames
            if (selectedFrameIds != null && selectedFrameIds.Count > 0 && isScreenFrame)
            {
                if (!selectedFrameIds.Contains(figmaNode.id)) return;
            }

            // Calculate depth relative to frame (for syncDepth limit)
            // Structural nodes (page, section, screen frame) don't count
            var canAdd = isSelectedPage || withinComponentDefinition;

            // Debug: log every node with export settings to trace why some are missed
            if (hasExport)
                Debug.Log($"[ServerRender] CHECK: '{figmaNode.name}' ({figmaNode.id}) type={figmaNode.type} depth={recursiveNodeDepth} canAdd={canAdd} isSelectedPage={isSelectedPage} withinComponent={withinComponentDefinition} exportOnly={exportOnly}");

            // Node with export settings → download as server-rendered image, don't recurse deeper
            // Skip screen frames — they are layout containers, not flat images
            if (canAdd && hasExport && recursiveNodeDepth > 0 && !isScreenFrame)
            {
                Debug.Log($"[ServerRender] EXPORT: '{figmaNode.name}' ({figmaNode.id}) type={figmaNode.type} depth={recursiveNodeDepth} section={currentSection} frame={currentFrame}");
                substitutionNodeList.Add(new ServerRenderNodeData
                {
                    RenderType = ServerRenderType.Export,
                    SourceNode = figmaNode,
                    SectionName = currentSection,
                    FrameName = currentFrame,
                });
                return;
            }

            // Standard server-render substitution (vector-only containers, not screen frames)
            if (!exportOnly && !isScreenFrame && canAdd && GetNodeSubstitutionStatus(figmaNode, recursiveNodeDepth))
            {
                substitutionNodeList.Add(new ServerRenderNodeData
                {
                    RenderType = ServerRenderType.Substitution,
                    SourceNode = figmaNode,
                    SectionName = currentSection,
                    FrameName = currentFrame,
                });
                return;
            }

            // Recurse into children
            if (figmaNode.children == null) return;
            if (figmaNode.type == NodeType.COMPONENT || figmaNode.type == NodeType.INSTANCE)
                withinComponentDefinition = true;

            foreach (var childNode in figmaNode.children)
                AddRenderSubstitutionsForFigmaNode(childNode, substitutionNodeList, recursiveNodeDepth + 1,
                    missingComponentIds, isSelectedPage, withinComponentDefinition, sectionFilter, exportOnly,
                    currentSection, currentFrame, syncDepth, selectedFrameIds);
        }

        /// <summary>
        /// Defines whether a given figma node should be substituted with server-side render
        /// </summary>
        /// <param name="node"></param>
        /// <param name="recursiveNodeDepth"></param>
        /// <returns></returns>
        private static bool GetNodeSubstitutionStatus(Node node,int recursiveNodeDepth)
        {
            // We never substitute screens or pages
            if (node.type == NodeType.CANVAS) return false;
            if (recursiveNodeDepth <=1 && node.type== NodeType.FRAME) return false;
            
            // If a given node has the word "render", mark for rendering
            if (node.name.ToLower().Contains(SERVER_RENDER_KEYWORD)) return true;

            // Some types we always render server-side. This may change if we support native vector rendering
            switch (node.type)
            {
                case NodeType.VECTOR:
                case NodeType.BOOLEAN_OPERATION:
                    return true;
            }

            // Server-render nodes whose children are purely visual shapes (no TEXT).
            // If all children are shapes/vectors/frames, render the whole thing as one image.
            var validNodeTypesForRender = new NodeType[]
            {
                NodeType.VECTOR, NodeType.BOOLEAN_OPERATION,
                NodeType.RECTANGLE, NodeType.ELLIPSE, NodeType.STAR, NodeType.LINE, NodeType.REGULAR_POLYGON,
                NodeType.GROUP, NodeType.FRAME, NodeType.COMPONENT, NodeType.INSTANCE,
            };
            var nodeTypeCount = new int[validNodeTypesForRender.Length];
            var onlyValidTypesFound =
                GetNodeChildrenExclusivelyOfTypes(node, validNodeTypesForRender, nodeTypeCount);

            // At least one shape child, no TEXT children
            var shapeCount = nodeTypeCount[0] + nodeTypeCount[1] + nodeTypeCount[2] +
                             nodeTypeCount[3] + nodeTypeCount[4] + nodeTypeCount[5] + nodeTypeCount[6];
            if (onlyValidTypesFound && shapeCount > 0) return true;
            
            return false;
        }

        /// <summary>
        /// Tests whether a given node only has children of a specific type
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeTypes"></param>
        /// <param name="nodeTypeCount"></param>
        /// <returns></returns>
        private static bool GetNodeChildrenExclusivelyOfTypes(Node node, NodeType[] nodeTypes,int[] nodeTypeCount)
        {
            // If this doesnt match return false
            if (!nodeTypes.Contains(node.type)) return false;
            
            // Increment count for matching node type for this node
            for (var i = 0; i < nodeTypes.Length; i++)
            {
                if (node.type == nodeTypes[i]) nodeTypeCount[i]++;
            }

            if (node.children == null) return true;
            foreach (var childNode in node.children)
            {
                var isMatching = GetNodeChildrenExclusivelyOfTypes(childNode, nodeTypes, nodeTypeCount);
                if (!isMatching) return false;
            }
            return true;
        }

        /// <summary>
        /// Finds all component IDs that are used in the figma file, that dont have a matching definition
        /// </summary>
        /// <returns></returns>
        public static List<string> FindMissingComponentDefinitions(FigmaFile file)
        {
            return (from componentKeyPair in file.components select componentKeyPair.Key into componentId let foundNode = GetFigmaNodeWithId(file, componentId) where foundNode == null select componentId).ToList();
        }

        /// <summary>
        /// Finds all missing components and 
        /// </summary>
        /// <param name="figmaFile"></param>
        /// <param name="missingComponentDefinitionList"></param>
        public static void ReplaceMissingComponents(FigmaFile figmaFile, List<string> missingComponentDefinitionList)
        {
            foreach (var componentId in missingComponentDefinitionList)
            {
                var allInstances = new List<Node>();
                FindAllComponentInstances(figmaFile.document, componentId, allInstances, 0);
                if (allInstances.Count==0) continue;
                var firstInstance = allInstances[0];
                firstInstance.type = NodeType.COMPONENT;
                // Remap all other instances to use this component
                for (var i = 1; i < allInstances.Count; i++)
                {
                    allInstances[i].componentId = firstInstance.id;
                }
            }
        }

        /// <summary>
        /// Finds Flow Starting Point id, from first page where one found
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static string FindPrototypeFlowStartScreenId(FigmaFile sourceFile)
        {
            foreach (var canvasNode in sourceFile.document.children)
            {
                if (canvasNode.flowStartingPoints != null && canvasNode.flowStartingPoints.Length > 0)
                    return canvasNode.flowStartingPoints[0].nodeId;
            }

            return string.Empty;
        }

        /// <summary>
        /// Lists all prototype flow starting points in a given Figma file
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static List<string> GetAllPrototypeFlowStartingPoints(FigmaFile sourceFile)
        {
            var allFlowStartingPoints = new List<string>();
            foreach (var canvasNode in sourceFile.document.children)
            {
                if (canvasNode.flowStartingPoints == null) continue;
                allFlowStartingPoints.AddRange(canvasNode.flowStartingPoints.Select(flowStartingPoint => flowStartingPoint.nodeId));
            }
            return allFlowStartingPoints;
        }
        
        /// <summary>
        /// Lists all Page Nodes in a given Figma file
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static List<Node> GetPageNodes(FigmaFile sourceFile)
        {
            var pageNodes = new List<Node>();
            foreach (var canvasNode in sourceFile.document.children)
            {
                pageNodes.Add(canvasNode);
            }
            return pageNodes;
        }

        private static void SearchScreenNodes(Node node, Node parentNode, List<Node> screenNodes)
        {
            if (IsScreenNode(node,parentNode))
            {
                screenNodes.Add(node);
            }

            if (node.children == null) return;

            foreach (var childNode in node.children)
            {
                SearchScreenNodes(childNode, node, screenNodes);
            }
        }

        /// <summary>
        /// Lists all Screen Nodes in a given Figma file
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static List<Node> GetScreenNodes(FigmaFile sourceFile)
        {
            var screenNodes = new List<Node>();
            foreach (var node in sourceFile.document.children)
            {
                SearchScreenNodes(node, null, screenNodes);
            }
            return screenNodes;
        }

        /// <summary>
        /// Check for Node is Screen Node
        /// </summary>
        public static bool IsScreenNode(Node node, Node parentNode)
        {
            if (node.type != NodeType.FRAME) return false;
            if (parentNode == null) return false;
            if (parentNode is { type: NodeType.CANVAS or NodeType.SECTION }) return true;
            return false;
        }
    }
}
