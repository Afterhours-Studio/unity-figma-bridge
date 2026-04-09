using System;
using System.Collections.Generic;
using System.IO;
using UnityFigmaBridge.Editor.Fonts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.Nodes
{
    /// <summary>
    /// Root generator for figma file/document. Constructs a native UI system and all assets
    /// </summary>
    public static class FigmaAssetGenerator
    {

        
        /// <summary>
        /// Build a single frame into the scene as clean Unity UI (used by Build tab).
        /// No Figma scripts — only standard Image, TextMeshProUGUI, LayoutGroups.
        /// All INSTANCE nodes are built inline as full node trees.
        /// </summary>
        public static void BuildSingleFrame(Canvas rootCanvas, Node frameNode, Node parentNode,
            FigmaBuildContext figmaImportProcessData)
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
            Node parentNode, int depth, FigmaBuildContext processData)
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

                // Export nodes may have landed in a different frame's folder (Figma deduplicates identical exports).
                // Fall back to searching the entire Sections tree by filename.
                if (sprite == null && serverEntry.RenderType == ServerRenderType.Export)
                {
                    var fileName = FigmaPaths.MakeValidFileName(serverEntry.SourceNode.name.Trim()) + ".png";
                    var sectionsDir = FigmaPaths.FigmaSectionsFolder;
                    if (Directory.Exists(sectionsDir))
                    {
                        foreach (var match in Directory.GetFiles(sectionsDir, fileName, SearchOption.AllDirectories))
                        {
                            var assetPath = match.Replace("\\", "/");
                            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                            if (sprite != null) break;
                        }
                    }
                }

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
                    ApplyConventionTags(go, tagButton, tagRectMask);
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
            ApplyConventionTags(go, tagButton, tagRectMask);

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

        private static void ApplyConventionTags(GameObject go, bool tagButton, bool tagRectMask)
        {
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
        private static bool ApplyCleanVisual(GameObject go, Node node, FigmaBuildContext processData)
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
        private static bool ApplyCleanImage(GameObject go, Node node, FigmaBuildContext processData)
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
        private static void ApplyCleanText(GameObject go, Node node, FigmaBuildContext processData)
        {
            if (node.style == null) return;

            go.name += " (TMP)";
            var text = go.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = node.characters;
            text.fontSize = node.style.fontSize;
            text.characterSpacing = -0.7f;

            // Color
            text.color = FigmaNodeManager.ResolveTextColor(node);

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

            // Outline (stroke) → font material
            var hasStroke = node.strokes != null && node.strokes.Length > 0 && node.strokeWeight > 0;
            if (hasStroke && fontMapping?.FontAsset != null)
            {
                var outlineColor = FigmaDataUtils.GetUnityFillColor(node.strokes[0]);
                var outlineWidth = Mathf.Clamp(5f * node.strokeWeight / node.style.fontSize, 0f, 0.5f);
                var mat = Fonts.FontManager.GetEffectMaterialPreset(
                    fontMapping, false, UnityEngine.Color.white, Vector2.zero,
                    true, outlineColor, outlineWidth);
                if (mat != null) text.fontMaterial = mat;
            }

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
    }

}