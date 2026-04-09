using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Color = UnityEngine.Color;

namespace UnityFigmaBridge.Editor.Nodes
{
    public static class FigmaNodeManager
    {
        /// <summary>
        /// Adjustment to character spacing to compensate for differences between Figma and TextMeshPro rendering
        /// </summary>
        private const float FIGMA_CHARACTER_SPACING_ADJUSTMENT = -0.7f;

        private static bool ShouldUseTMP(FigmaImportProcessData data)
        {
            // If this file compiles, TMPro is present — no runtime reflection needed.
            if (data?.Settings == null) return true;
            return data.Settings.TextMode != Settings.TextRenderMode.LegacyText;
        }

        /// <summary>
        /// Applies the Figma Node properties to a Unity Game object (components created in CreateUnityComponentsForNode)
        /// </summary>
        /// <param name="nodeGameObject"></param>
        /// <param name="node"></param>
        /// <param name="figmaImportProcessData"></param>
        public static void ApplyUnityComponentPropertiesForNode(GameObject nodeGameObject,Node node, FigmaImportProcessData figmaImportProcessData)
        {

            switch (node.type)
            {
                case NodeType.FRAME:
                case NodeType.RECTANGLE:
                case NodeType.ELLIPSE:
                case NodeType.STAR:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                case NodeType.SECTION:
                    var needsImageComponent = node.fills.Length > 0 || node.strokes.Length > 0;
                    if (NodeIsSubstitution(node, figmaImportProcessData)) break;
                    if (!needsImageComponent) break;
                    // Create as needed (in case an override has specified new properties)
                    var figmaImage = nodeGameObject.GetComponent<FigmaImage>();
                    if (figmaImage == null) figmaImage = nodeGameObject.AddComponent<FigmaImage>();
                    // We use different properties, depending on Figma shape
                    switch (node.type)
                    {
                        case NodeType.ELLIPSE:
                        {
                            figmaImage.Shape = FigmaImage.ShapeType.Ellipse;
                            // Additional data for ellipse to define arc
                            if (node.arcData != null)
                            {
                                figmaImage.EllipseArcAngleRange =
                                    new Vector2(node.arcData.startingAngle, node.arcData.endingAngle);
                                figmaImage.EllipseInnerRadius = node.arcData.innerRadius;
                            }
                            break;
                        }
                        case NodeType.STAR:
                            figmaImage.Shape = FigmaImage.ShapeType.Star;
                            break;
                        default:
                            // All others
                            figmaImage.Shape = FigmaImage.ShapeType.Rectangle;
                            break;
                    }

                    // If this is a rounded rectangle, apply properties
                    if (node.rectangleCornerRadii != null || node.cornerRadius > 0)
                    {
                        // We can have either regular corner radius, or explicit for each
                        // Note that figma order is different from shader
                        var cornerRadiusArray = node.rectangleCornerRadii;
                        figmaImage.CornerRadius = node.rectangleCornerRadii != null
                            ? new Vector4(cornerRadiusArray[0], cornerRadiusArray[1], cornerRadiusArray[2], cornerRadiusArray[3])
                            : new Vector4(node.cornerRadius, node.cornerRadius, node.cornerRadius, node.cornerRadius);
                    }

                    SetupFill(figmaImage,node);
                    SetupStroke(figmaImage, node);
                    
                    
                    break;
                case NodeType.LINE:
                    break;
                case NodeType.DOCUMENT:
                    break;
                case NodeType.CANVAS:
                    break;
                case NodeType.GROUP:
                    break;
                case NodeType.VECTOR:
                    break;
                case NodeType.BOOLEAN_OPERATION:
                    break;
                case NodeType.REGULAR_POLYGON:
                    break;
                case NodeType.TEXT:
                    // Legacy Text path
                    if (!ShouldUseTMP(figmaImportProcessData))
                    {
                        var legacyText = nodeGameObject.GetComponent<Text>();
                        if (legacyText != null)
                        {
                            legacyText.text = node.characters;
                            legacyText.fontSize = Mathf.RoundToInt(node.style.fontSize);
                            legacyText.color = ResolveTextColor(node);
                            legacyText.alignment = (node.style.textAlignHorizontal, node.style.textAlignVertical) switch
                            {
                                (TypeStyle.TextAlignHorizontal.CENTER, TypeStyle.TextAlignVertical.TOP) => TextAnchor.UpperCenter,
                                (TypeStyle.TextAlignHorizontal.RIGHT, TypeStyle.TextAlignVertical.TOP) => TextAnchor.UpperRight,
                                (TypeStyle.TextAlignHorizontal.CENTER, TypeStyle.TextAlignVertical.CENTER) => TextAnchor.MiddleCenter,
                                (TypeStyle.TextAlignHorizontal.LEFT, TypeStyle.TextAlignVertical.CENTER) => TextAnchor.MiddleLeft,
                                (TypeStyle.TextAlignHorizontal.RIGHT, TypeStyle.TextAlignVertical.CENTER) => TextAnchor.MiddleRight,
                                (TypeStyle.TextAlignHorizontal.CENTER, TypeStyle.TextAlignVertical.BOTTOM) => TextAnchor.LowerCenter,
                                (TypeStyle.TextAlignHorizontal.RIGHT, TypeStyle.TextAlignVertical.BOTTOM) => TextAnchor.LowerRight,
                                (_, TypeStyle.TextAlignVertical.CENTER) => TextAnchor.MiddleLeft,
                                (_, TypeStyle.TextAlignVertical.BOTTOM) => TextAnchor.LowerLeft,
                                _ => TextAnchor.UpperLeft,
                            };
                            var fs = FontStyle.Normal;
                            if (node.style.italic) fs |= FontStyle.Italic;
                            legacyText.fontStyle = fs;
                        }
                        break;
                    }
                    // Get the best fit TextMeshPro font this font (handled when document processed)
                    var text = nodeGameObject.GetComponent<TextMeshProUGUI>();
                    var matchingFontMapping = figmaImportProcessData.FontMap.GetFontMapping(node.style.fontFamily, node.style.fontWeight);
                    if (matchingFontMapping?.FontAsset != null)
                    {
                        text.font = matchingFontMapping.FontAsset;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find font asset for '{node.style.fontFamily}' weight {node.style.fontWeight} on node '{node.name}'");
                    }

                    text.text = node.characters;
                    text.color = ResolveTextColor(node);
                    text.fontSize = node.style.fontSize;
                    text.characterSpacing = FIGMA_CHARACTER_SPACING_ADJUSTMENT;
                   
                    text.horizontalAlignment = node.style.textAlignHorizontal switch
                    {
                        TypeStyle.TextAlignHorizontal.LEFT => HorizontalAlignmentOptions.Left,
                        TypeStyle.TextAlignHorizontal.CENTER => HorizontalAlignmentOptions.Center,
                        TypeStyle.TextAlignHorizontal.JUSTIFIED => HorizontalAlignmentOptions.Justified,
                        TypeStyle.TextAlignHorizontal.RIGHT => HorizontalAlignmentOptions.Right,
                        _ => HorizontalAlignmentOptions.Left
                    };

                    text.verticalAlignment = node.style.textAlignVertical switch
                    {
                        TypeStyle.TextAlignVertical.TOP => VerticalAlignmentOptions.Top,
                        TypeStyle.TextAlignVertical.CENTER => VerticalAlignmentOptions.Middle,
                        TypeStyle.TextAlignVertical.BOTTOM => VerticalAlignmentOptions.Bottom,
                        _ => VerticalAlignmentOptions.Top,
                    };
                    
                    // Add on styling attributes depending on text case
                    text.fontStyle |= node.style.textCase switch
                    {
                        TypeStyle.TextCase.LOWER => FontStyles.LowerCase,
                        TypeStyle.TextCase.UPPER => FontStyles.UpperCase,
                        TypeStyle.TextCase.SMALL_CAPS => FontStyles.SmallCaps,
                        _ => 0
                    };
                    
                    // Add on styling attributes depending on text decoration
                    text.fontStyle |= node.style.textDecoration switch
                    {
                        TypeStyle.TextDecoration.UNDERLINE => FontStyles.Underline,
                        TypeStyle.TextDecoration.STRIKETHROUGH => FontStyles.Strikethrough,
                        _ => 0
                    };
                    
                    // We only use TextMeshPro's italic functionality for now
                    if (node.style.italic) text.fontStyle |= FontStyles.Italic;

                    // We use material variants for TextMeshPro to apply text effects
                    var hasShadowEffect = false;
                    Effect shadowEffect=null;
                    if (node.effects == null) break;
                    foreach (var effect in node.effects)
                    {
                        if (effect.type == Effect.EffectType.DROP_SHADOW)
                        {
                            shadowEffect = effect;
                            hasShadowEffect = true;
                        }
                    }
                    
                    // Handle text auto resize
                    if (node.style.textAutoResize != TypeStyle.TextAutoResize.NONE)
                    {
                        var contentSizeFitter = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(nodeGameObject);
                        
                        switch (node.style.textAutoResize)
                        {
                            case TypeStyle.TextAutoResize.NONE:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                                break;
                            case TypeStyle.TextAutoResize.HEIGHT:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                                break;
                            case TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                                break;
                            case TypeStyle.TextAutoResize.TRUNCATE:
                                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                                break;
                        }
                    }
                    
                    // If no material variation, ignore
                    if (!hasShadowEffect && (node.strokes == null || node.strokes.Length == 0)) return;
                    
                    var shadowColor = hasShadowEffect
                        ? FigmaDataUtils.ToUnityColor(shadowEffect.color) : UnityEngine.Color.white;
                    var shadowDistance = shadowEffect != null
                        ? new Vector2(shadowEffect.offset.x, -shadowEffect.offset.y) : Vector2.zero;

                    var hasStrokes = node.strokes != null && node.strokes.Length > 0;
                    var outlineColor = hasStrokes
                        ? FigmaDataUtils.GetUnityFillColor(node.strokes[0]) : UnityEngine.Color.white;
                    var outlineWidth = 0f;
                    if (hasStrokes)
                    {
                        // We'll calculate target outline width as a factor of font size to match 
                        outlineWidth = 4.0f*node.strokeWeight / node.style.fontSize;
                        // Clamp to 0.5
                        outlineWidth = Mathf.Clamp(outlineWidth, 0, 0.5f);
                    }
                    var effectMaterialPreset = FontManager.GetEffectMaterialPreset(matchingFontMapping,
                        hasShadowEffect, shadowColor, shadowDistance, hasStrokes, outlineColor, outlineWidth);
                    text.fontMaterial = effectMaterialPreset;

                    
                    
                    break;
                case NodeType.SLICE:
                    break;
                case NodeType.COMPONENT_SET:
                    break;
                case NodeType.STICKY:
                    // Sticky note - unused
                    break;
                case NodeType.SHAPE_WITH_TEXT:
                    break;
                case NodeType.CONNECTOR:
                    break;
            }
            
            // Setup opacity - this is done by applying a CanvasGroup
            // Only apply if the opacity is less than 1, or of there is a CanvasGroup already
            if (node.opacity < 1 || nodeGameObject.GetComponent<CanvasGroup>()!=null)
            {
                var canvasGroup = nodeGameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = nodeGameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = node.opacity;
            }
            // Setup visibility
            nodeGameObject.SetActive(node.visible);
        }

        /// <summary>
        /// Resolves text color from node.fills, falling back to node.style.fills, then black.
        /// Figma API sometimes omits node-level fills for TEXT nodes.
        /// </summary>
        public static UnityEngine.Color ResolveTextColor(Node node)
        {
            var fills = node.fills != null && node.fills.Length > 0 ? node.fills
                : node.style?.fills != null && node.style.fills.Length > 0 ? node.style.fills
                : null;

            if (fills == null) return UnityEngine.Color.black;

            // Find first visible SOLID fill
            foreach (var fill in fills)
            {
                if (fill == null || !fill.visible) continue;
                if (fill.type == Paint.PaintType.SOLID && fill.color != null)
                    return FigmaDataUtils.GetUnityFillColor(fill);
            }

            // Fallback: try first fill regardless of type
            return fills[0] != null ? FigmaDataUtils.GetUnityFillColor(fills[0]) : UnityEngine.Color.black;
        }

        private static void SetupStroke(FigmaImage figmaImage, Node node)
        {
            if (node.strokes.Length > 0)
            {
                // Use stroke weight as outline width
                figmaImage.StrokeWidth = node.strokeWeight;
                figmaImage.StrokeColor = FigmaDataUtils.GetUnityFillColor(node.strokes[0]);
                if (node.fills.Length == 0)
                {
                   // Stroke only, fill colour should be transparent
                   figmaImage.FillColor = new Color(1f, 1f, 1f, 0f); // Transparent
                }
            }
            else
            {
                figmaImage.StrokeWidth = 0;
            }
        }

        private static void SetupFill(FigmaImage figmaImage, Node node)
        {
            if (node.fills.Length > 0)
            {
                var firstFill = node.fills[0];
                switch (firstFill.type)
                {
                    case Paint.PaintType.IMAGE:
                        SetupImageFill(figmaImage, firstFill);
                        break;
                    case Paint.PaintType.GRADIENT_LINEAR:
                    case Paint.PaintType.GRADIENT_RADIAL:
                        figmaImage.FillGradient = FigmaDataUtils.ToUnityGradient(firstFill);
                        figmaImage.Fill = firstFill.type == Paint.PaintType.GRADIENT_RADIAL
                            ? FigmaImage.FillStyle.RadialGradient
                            : FigmaImage.FillStyle.LinearGradient;

                        var gradientHandlePositions = firstFill.gradientHandlePositions;
                        if (gradientHandlePositions.Length == 3)
                        {
                            figmaImage.GradientHandlePositions = new[]
                            {
                                FigmaDataUtils.ToUnityVector(gradientHandlePositions[0]),
                                FigmaDataUtils.ToUnityVector(gradientHandlePositions[1]),
                                FigmaDataUtils.ToUnityVector(gradientHandlePositions[2])
                            };
                        }

                        break;
                    case Paint.PaintType.SOLID:
                        // Default, fill colour set below
                        break;
                    case Paint.PaintType.GRADIENT_ANGULAR:
                        // Unsupported
                        break;
                    case Paint.PaintType.GRADIENT_DIAMOND:
                        // Unsupported
                        break;
                    case Paint.PaintType.EMOJI:
                        // Unsupported
                        break;
                }

                // for invisible fills, disable
                if (!firstFill.visible) figmaImage.enabled = false;

                // We don't use the base "color" attribute - this is reserved for transparency groups etc
                // So as not to apply to both stroke and fill
                figmaImage.FillColor = FigmaDataUtils.GetUnityFillColor(firstFill);
            }
            else
                figmaImage.FillColor =
                            new Color(0, 0, 0, 0); // Transparent fill - TODO find neater solution
        }


        /// <summary>
        /// Setup image fill depending on parameters
        /// </summary>
        /// <param name="figmaImage"></param>
        /// <param name="fill"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void SetupImageFill(FigmaImage figmaImage,Paint fill)
        {
            // Assign image fill, load from asset database
            figmaImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    FigmaPaths.GetPathForImageFill(fill.imageRef));

            switch (fill.scaleMode)
            {
                case Paint.ScaleMode.FIT:
                    figmaImage.ScaleMode = FigmaImage.ImageScaleMode.Fit;
                    break;
                case Paint.ScaleMode.FILL:
                    figmaImage.ScaleMode = FigmaImage.ImageScaleMode.Fill;
                    break;
                case Paint.ScaleMode.TILE:
                    // Use the image size to determine UVs. 
                    figmaImage.ScaleMode = FigmaImage.ImageScaleMode.Tile;
                    // Apply scaling factor from document
                    figmaImage.ImageScaleFactor = fill.scalingFactor;
                    break;
                case Paint.ScaleMode.STRETCH:
                    figmaImage.ScaleMode = FigmaImage.ImageScaleMode.Stretch;
                    figmaImage.ImageTransform=FigmaDataUtils.ToUnityVector3Array(fill.imageTransform);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }


        /// <summary>
        /// Create all required components for figma node
        /// </summary>
        /// <param name="nodeGameObject"></param>
        /// <param name="node"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void CreateUnityComponentsForNode(GameObject nodeGameObject,Node node,FigmaImportProcessData figmaImportProcessData)
        {
            // Background fills
            switch (node.type)
            {
                case NodeType.FRAME:
                case NodeType.RECTANGLE:
                case NodeType.ELLIPSE:
                case NodeType.STAR:
                case NodeType.COMPONENT:
                case NodeType.INSTANCE:
                    if (NodeIsSubstitution(node, figmaImportProcessData)) return;
                    // No longer need to add here - will be generated above as needed
                    break;
                case NodeType.TEXT:
                    if (ShouldUseTMP(figmaImportProcessData))
                    {
                        nodeGameObject.AddComponent<TextMeshProUGUI>();
                        nodeGameObject.name += " (TMP)";
                    }
                    else
                    {
                        nodeGameObject.AddComponent<Text>();
                        nodeGameObject.name += " (Text)";
                    }
                    break;
                case NodeType.DOCUMENT:
                    break;
                case NodeType.CANVAS:
                    break;
                case NodeType.GROUP:
                    break;
                case NodeType.VECTOR:
                    break;
                case NodeType.BOOLEAN_OPERATION:
                    break;
                case NodeType.LINE:
                    break;
                case NodeType.REGULAR_POLYGON:
                    break;
                case NodeType.SLICE:
                    break;
                case NodeType.COMPONENT_SET:
                    break;
                case NodeType.STICKY:
                    break;
                case NodeType.SHAPE_WITH_TEXT:
                    break;
                case NodeType.CONNECTOR:
                    break;
                case NodeType.SECTION:
                    break;
                case NodeType.TABLE:
                case NodeType.TABLE_CELL:
                case NodeType.WASHI_TAPE:
                default:
                    // Unimplemented type
                    break;
            }
        }

        public static bool NodeIsSubstitution(Node node, FigmaImportProcessData figmaImportProcessData)
        {
            switch (node.type)
            {
                // If this is an instance and the component is a server render node
                case NodeType.INSTANCE when
                    figmaImportProcessData.ServerRenderNodes.Find(serverRenderNode=>serverRenderNode.SourceNode.id==node.componentId && serverRenderNode.RenderType== ServerRenderType.Substitution)!=null:
                // This is is a component and is a server render node
                case NodeType.COMPONENT when
                    figmaImportProcessData.ServerRenderNodes.Find(serverRenderNode=>serverRenderNode.SourceNode.id==node.id && serverRenderNode.RenderType== ServerRenderType.Substitution)!=null:
                    return true;
                default:
                    return false;
            }
        }
    }
}