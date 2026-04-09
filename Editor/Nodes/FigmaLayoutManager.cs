using System;
using UnityEngine;
using UnityEngine.UI;

namespace Afterhours.FigmaBridge.Editor
{
    /// <summary>
    /// Manages layout functionality for Figma nodes
    /// </summary>
    public static class FigmaLayoutManager
    {
        /// <summary>
        /// Applies layout properties for a given node to a gameObject, using Vertical/Horizontal layout groups
        /// </summary>
        /// <param name="nodeGameObject"></param>
        /// <param name="node"></param>
        /// <param name="figmaImportProcessData"></param>
        /// <param name="scrollContentGameObject">Generated scroll content object (if generated)</param>
        public static void ApplyLayoutPropertiesForNode( GameObject nodeGameObject,Node node,
            FigmaBuildContext figmaImportProcessData,out GameObject scrollContentGameObject)
        {
            
            // Depending on whether scrolling is applied, we may want to add layout to this object or to the content
            // holder
            
            var targetLayoutObject = nodeGameObject;
            scrollContentGameObject = null;
            
            // Check scrolling requirements
            var implementScrolling = node.type == NodeType.FRAME && node.overflowDirection != Node.OverflowDirection.NONE;
            if (implementScrolling)
            {
                // This Frame implements scrolling, so we need to add in appropriate functionality
                
                // Add in a rect mask to implement clipping
                if (node.clipsContent) UnityUiUtils.GetOrAddComponent<RectMask2D>(nodeGameObject);

                // Create the content clip and parent to this object
                scrollContentGameObject = new GameObject($"{node.name}_ScrollContent", typeof(RectTransform));
                var scrollContentRectTransform = scrollContentGameObject.transform as RectTransform;
                scrollContentRectTransform.pivot = new Vector2(0, 1);
                scrollContentRectTransform.anchorMin = scrollContentRectTransform.anchorMax =new Vector2(0,1);
                scrollContentRectTransform.anchoredPosition=Vector2.zero;
                scrollContentRectTransform.SetParent(nodeGameObject.transform, false);
                
                var scrollRectComponent = UnityUiUtils.GetOrAddComponent<ScrollRect>(nodeGameObject);
                scrollRectComponent.content = scrollContentGameObject.transform as RectTransform;
                scrollRectComponent.horizontal =
                    node.overflowDirection is Node.OverflowDirection.HORIZONTAL_SCROLLING 
                        or Node.OverflowDirection.HORIZONTAL_AND_VERTICAL_SCROLLING;

                scrollRectComponent.vertical =
                    node.overflowDirection is Node.OverflowDirection.VERTICAL_SCROLLING 
                        or Node.OverflowDirection.HORIZONTAL_AND_VERTICAL_SCROLLING;


                // If using layout, we need to use content size fitter to ensure proper sizing for child components
                if (node.layoutMode != Node.LayoutMode.NONE)
                {
                    var contentSizeFitter = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(scrollContentGameObject);
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }

                // Apply layout to this content clip
                targetLayoutObject = scrollContentGameObject;
            }
            
            
            // Ignore if layout mode is NONE or layout disabled
            if (node.layoutMode == Node.LayoutMode.NONE || !figmaImportProcessData.Settings.EnableAutoLayout) return;
            
            // Remove existing layout groups before applying new one
            var existingLayoutGroup = targetLayoutObject.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (existingLayoutGroup!=null) UnityEngine.Object.DestroyImmediate(existingLayoutGroup);
            var existingGridLayout = targetLayoutObject.GetComponent<GridLayoutGroup>();
            if (existingGridLayout!=null) UnityEngine.Object.DestroyImmediate(existingGridLayout);
            
            // --- Grid layout (Figma Grid auto-layout) ---
            if (node.layoutMode == Node.LayoutMode.GRID)
            {
                var gridLayout = UnityUiUtils.GetOrAddComponent<GridLayoutGroup>(targetLayoutObject);

                // Cell size from first child
                var cellSize = new Vector2(100, 100);
                if (node.children != null && node.children.Length > 0)
                {
                    var first = node.children[0];
                    if (first.size != null)
                        cellSize = new Vector2(first.size.x, first.size.y);
                }
                gridLayout.cellSize = cellSize;

                gridLayout.spacing = new Vector2(node.gridColumnGap, node.gridRowGap);
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = Mathf.Max(1, node.gridColumnCount);
                gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
                gridLayout.childAlignment = MapAlignment(node.primaryAxisAlignItems, node.counterAxisAlignItems);
                gridLayout.padding = new RectOffset(
                    Mathf.RoundToInt(node.paddingLeft), Mathf.RoundToInt(node.paddingRight),
                    Mathf.RoundToInt(node.paddingTop), Mathf.RoundToInt(node.paddingBottom));
                return;
            }

            HorizontalOrVerticalLayoutGroup layoutGroup = null;

            switch (node.layoutMode)
            {
                case Node.LayoutMode.VERTICAL:
                    layoutGroup= UnityUiUtils.GetOrAddComponent<VerticalLayoutGroup>(targetLayoutObject);
                    layoutGroup.childForceExpandWidth= layoutGroup.childForceExpandHeight = false;
                    // Setup alignment according to Figma layout. Primary is Vertical
                    switch (node.primaryAxisAlignItems)
                    {
                        // Upper Alignment
                        case Node.PrimaryAxisAlignItems.MIN:
                            layoutGroup.childAlignment = node.counterAxisAlignItems switch
                            {
                                Node.CounterAxisAlignItems.MIN => TextAnchor.UpperLeft,
                                Node.CounterAxisAlignItems.CENTER => TextAnchor.UpperCenter,
                                Node.CounterAxisAlignItems.MAX => TextAnchor.UpperRight,
                                _ => layoutGroup.childAlignment
                            };
                            break;
                        // Center alignment
                        case Node.PrimaryAxisAlignItems.CENTER:
                            layoutGroup.childAlignment = node.counterAxisAlignItems switch
                            {
                                Node.CounterAxisAlignItems.MIN => TextAnchor.MiddleLeft,
                                Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleCenter,
                                Node.CounterAxisAlignItems.MAX => TextAnchor.MiddleRight,
                                _ => layoutGroup.childAlignment
                            };
                            break;
                        // Lower alignment
                        case Node.PrimaryAxisAlignItems.MAX:
                            layoutGroup.childAlignment = node.counterAxisAlignItems switch
                            {
                                Node.CounterAxisAlignItems.MIN => TextAnchor.LowerLeft,
                                Node.CounterAxisAlignItems.CENTER => TextAnchor.LowerCenter,
                                Node.CounterAxisAlignItems.MAX => TextAnchor.LowerRight,
                                _ => layoutGroup.childAlignment
                            };
                            break;
                        case Node.PrimaryAxisAlignItems.SPACE_BETWEEN:
                            layoutGroup.childAlignment = node.counterAxisAlignItems switch
                            {
                                Node.CounterAxisAlignItems.MIN => TextAnchor.UpperLeft,
                                Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleLeft,
                                Node.CounterAxisAlignItems.MAX => TextAnchor.LowerLeft,
                                _ => layoutGroup.childAlignment
                            };
                            break;
                        default:
                            break;
                    }

                    break;
                case Node.LayoutMode.HORIZONTAL:
                    // --- Wrap (Horizontal + flex-wrap) → GridLayoutGroup ---
                    if (node.layoutWrap == Node.LayoutWrap.WRAP)
                    {
                        var wrapGrid = UnityUiUtils.GetOrAddComponent<GridLayoutGroup>(targetLayoutObject);

                        var wrapCellSize = new Vector2(100, 100);
                        if (node.children != null && node.children.Length > 0)
                        {
                            var first = node.children[0];
                            if (first.size != null)
                                wrapCellSize = new Vector2(first.size.x, first.size.y);
                        }
                        wrapGrid.cellSize = wrapCellSize;

                        var ySpacing = node.counterAxisSpacing >= 0 ? node.counterAxisSpacing : node.itemSpacing;
                        wrapGrid.spacing = new Vector2(node.itemSpacing, ySpacing);
                        wrapGrid.constraint = GridLayoutGroup.Constraint.Flexible;
                        wrapGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                        wrapGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
                        wrapGrid.childAlignment = MapAlignment(node.primaryAxisAlignItems, node.counterAxisAlignItems);
                        wrapGrid.padding = new RectOffset(
                            Mathf.RoundToInt(node.paddingLeft), Mathf.RoundToInt(node.paddingRight),
                            Mathf.RoundToInt(node.paddingTop), Mathf.RoundToInt(node.paddingBottom));
                        return;
                    }

                    layoutGroup= UnityUiUtils.GetOrAddComponent<HorizontalLayoutGroup>(targetLayoutObject);
                    layoutGroup.childForceExpandWidth= layoutGroup.childForceExpandHeight = false;
                    // Setup alignment according to Figma layout. Primary is Horizontal
                    layoutGroup.childAlignment = node.primaryAxisAlignItems switch
                    {
                        // Left Alignment
                        Node.PrimaryAxisAlignItems.MIN => node.counterAxisAlignItems switch
                        {
                            Node.CounterAxisAlignItems.MIN => TextAnchor.UpperLeft,
                            Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleLeft,
                            Node.CounterAxisAlignItems.MAX => TextAnchor.LowerLeft,
                            _ => layoutGroup.childAlignment
                        },
                        // Center alignment
                        Node.PrimaryAxisAlignItems.CENTER => node.counterAxisAlignItems switch
                        {
                            Node.CounterAxisAlignItems.MIN => TextAnchor.UpperCenter,
                            Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleCenter,
                            Node.CounterAxisAlignItems.MAX => TextAnchor.LowerCenter,
                            _ => layoutGroup.childAlignment
                        },
                        // Right alignment
                        Node.PrimaryAxisAlignItems.MAX => node.counterAxisAlignItems switch
                        {
                            Node.CounterAxisAlignItems.MIN => TextAnchor.UpperRight,
                            Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleRight,
                            Node.CounterAxisAlignItems.MAX => TextAnchor.LowerRight,
                            _ => layoutGroup.childAlignment
                        },
                        // SPACE_BETWEEN: distribute evenly, default to left alignment
                        Node.PrimaryAxisAlignItems.SPACE_BETWEEN => node.counterAxisAlignItems switch
                        {
                            Node.CounterAxisAlignItems.MIN => TextAnchor.UpperLeft,
                            Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleLeft,
                            Node.CounterAxisAlignItems.MAX => TextAnchor.LowerLeft,
                            _ => layoutGroup.childAlignment
                        },
                        _ => layoutGroup.childAlignment
                    };
                    break;
            }

            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;

            layoutGroup.padding = new RectOffset(Mathf.RoundToInt(node.paddingLeft), Mathf.RoundToInt(node.paddingRight),
                Mathf.RoundToInt(node.paddingTop), Mathf.RoundToInt(node.paddingBottom));
            layoutGroup.spacing = node.itemSpacing;
        }

        private static TextAnchor MapAlignment(Node.PrimaryAxisAlignItems primary, Node.CounterAxisAlignItems counter)
        {
            return primary switch
            {
                Node.PrimaryAxisAlignItems.MIN => counter switch
                {
                    Node.CounterAxisAlignItems.MIN => TextAnchor.UpperLeft,
                    Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleLeft,
                    Node.CounterAxisAlignItems.MAX => TextAnchor.LowerLeft,
                    _ => TextAnchor.UpperLeft
                },
                Node.PrimaryAxisAlignItems.CENTER => counter switch
                {
                    Node.CounterAxisAlignItems.MIN => TextAnchor.UpperCenter,
                    Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleCenter,
                    Node.CounterAxisAlignItems.MAX => TextAnchor.LowerCenter,
                    _ => TextAnchor.UpperCenter
                },
                Node.PrimaryAxisAlignItems.MAX => counter switch
                {
                    Node.CounterAxisAlignItems.MIN => TextAnchor.UpperRight,
                    Node.CounterAxisAlignItems.CENTER => TextAnchor.MiddleRight,
                    Node.CounterAxisAlignItems.MAX => TextAnchor.LowerRight,
                    _ => TextAnchor.UpperRight
                },
                _ => TextAnchor.UpperLeft
            };
        }
    }
}