using System;
using System.Collections.Generic;
using System.IO;
using Afterhours.FigmaBridge.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Afterhours.FigmaBridge.Editor
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
            FigmaNodeNaming.ResetCounter();

            if (parentNode != null && parentNode.type == NodeType.SECTION)
                FigmaPaths.SetContext(parentNode.name, "");
            FigmaPaths.CurrentFrameName = frameNode.name;

            // Root frame = empty RectTransform container, stretched full screen
            var frameName = figmaImportProcessData.Settings.SmartNaming
                ? FigmaNodeNaming.FormatName(frameNode.name)
                : frameNode.name;
            var go = new GameObject(frameName, typeof(RectTransform));
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
            var tag9SliceBorder = 0f;
            while (nodeName.StartsWith("["))
            {
                var close = nodeName.IndexOf(']');
                if (close < 0) break;
                var tag = nodeName.Substring(1, close - 1);
                nodeName = nodeName.Substring(close + 1).TrimStart();
                var tagLower = tag.ToLower();
                if (tagLower == "button") tagButton = true;
                else if (tagLower == "rectmask2d") tagRectMask = true;
                else if (tagLower == "scrollrect") tagScrollRect = true;
                else if (tagLower.StartsWith("9slice"))
                {
                    // [9Slice] = auto, [9Slice:24] or [9Slice_24] = explicit border
                    var sepIdx = tag.IndexOfAny(new[] { ':', '_' }, 6);
                    if (sepIdx >= 0 && float.TryParse(tag.Substring(sepIdx + 1), out var border))
                        tag9SliceBorder = border;
                    else
                        tag9SliceBorder = -1; // auto
                }
            }
            if (string.IsNullOrWhiteSpace(nodeName)) nodeName = node.name;

            if (processData.Settings.SmartNaming)
                nodeName = FigmaNodeNaming.FormatName(nodeName);

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
                    var fileName = FigmaPaths.MakeValidFileName(FigmaPaths.StripConventionTags(serverEntry.SourceNode.name.Trim())) + ".png";
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
                    // 9-slice: [9Slice] tag → explicit, cornerRadius → auto-detect
                    var sliceBorder = Resolve9SliceBorder(node, sprite, tag9SliceBorder, processData.Settings.AutoSlice9);
                    if (sliceBorder > 0)
                    {
                        EnsureSpriteBorder(sprite, sliceBorder);
                        img.type = Image.Type.Sliced;
                    }
                    if (node.opacity < 1)
                        go.AddComponent<CanvasGroup>().alpha = node.opacity;
                    ApplyConventionTags(go, tagButton, tagRectMask);
                    return go;
                }
                // No image on disk — fall through to normal build
            }

            // Normal node — apply transform (clean, no LayoutElement)
            ApplyCleanTransform(rt, node, parentNode, depth > 0);

            // Figma mask node
            if (node.isMask)
            {
                var mask = go.AddComponent<Mask>();
                mask.showMaskGraphic = false;
            }

            // Apply visual: Image or TextMeshProUGUI
            bool hasImageFill = ApplyCleanVisual(go, node, processData, tag9SliceBorder);

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
        private static void ApplyCleanTransform(RectTransform rt, Node node, Node parentNode, bool centerPivot)
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

            // Apply Figma constraints → Unity anchors
            if (node.constraints != null && parentNode != null)
                NodeTransformManager.ApplyFigmaConstraints(rt, node, parentNode);

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

            // Use node.size (actual design size) instead of absoluteRenderBounds
            // (which includes children's effects like shadows and expands beyond the node)
            rt.sizeDelta = new Vector2(node.size.x, node.size.y);

            var parentPos = parentNode?.absoluteBoundingBox != null
                ? new Vector2(parentNode.absoluteBoundingBox.x, parentNode.absoluteBoundingBox.y)
                : Vector2.zero;

            rt.anchoredPosition = new Vector2(
                node.absoluteBoundingBox.x - parentPos.x,
                -(node.absoluteBoundingBox.y - parentPos.y));

            if (node.constraints != null && parentNode != null)
                NodeTransformManager.ApplyFigmaConstraints(rt, node, parentNode);
        }

        /// <summary>
        /// Apply clean visual components — standard Image or TextMeshProUGUI only.
        /// Returns true if an IMAGE fill sprite was loaded (node visual is complete, children are redundant).
        /// </summary>
        private static bool ApplyCleanVisual(GameObject go, Node node, FigmaBuildContext processData, float tag9SliceBorder = 0f)
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
                    return ApplyCleanImage(go, node, processData, tag9SliceBorder);

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
        private static bool ApplyCleanImage(GameObject go, Node node, FigmaBuildContext processData, float tag9SliceBorder = 0f)
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
                        var img = go.AddComponent<Image>();
                        img.sprite = sprite;
                        var sliceBorder = Resolve9SliceBorder(node, sprite, tag9SliceBorder, processData.Settings.AutoSlice9);
                        if (sliceBorder > 0)
                        {
                            EnsureSpriteBorder(sprite, sliceBorder);
                            img.type = Image.Type.Sliced;
                        }
                        else
                        {
                            img.type = Image.Type.Simple;
                        }
                        img.preserveAspect = fill.scaleMode == Paint.ScaleMode.FIT;
                        return true;
                    }
                    go.AddComponent<Image>().color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;

                case Paint.PaintType.GRADIENT_LINEAR:
                case Paint.PaintType.GRADIENT_RADIAL:
                    var figmaImg = go.AddComponent<FigmaImage>();
                    figmaImg.FillGradient = FigmaDataUtils.ToUnityGradient(fill);
                    figmaImg.Fill = fill.type == Paint.PaintType.GRADIENT_RADIAL
                        ? FigmaImage.FillStyle.RadialGradient
                        : FigmaImage.FillStyle.LinearGradient;
                    // Flip Y: Figma Y=0 top → Unity Y=0 bottom
                    if (fill.gradientHandlePositions != null && fill.gradientHandlePositions.Length == 3)
                    {
                        figmaImg.GradientHandlePositions = new[]
                        {
                            new UnityEngine.Vector2(fill.gradientHandlePositions[0].x, 1f - fill.gradientHandlePositions[0].y),
                            new UnityEngine.Vector2(fill.gradientHandlePositions[1].x, 1f - fill.gradientHandlePositions[1].y),
                            new UnityEngine.Vector2(fill.gradientHandlePositions[2].x, 1f - fill.gradientHandlePositions[2].y),
                        };
                    }
                    // Shape type
                    figmaImg.Shape = node.type switch
                    {
                        NodeType.ELLIPSE => FigmaImage.ShapeType.Ellipse,
                        NodeType.STAR => FigmaImage.ShapeType.Star,
                        _ => FigmaImage.ShapeType.Rectangle,
                    };
                    // Corner radius — normalize to 0-1 range (fraction of half shortest side)
                    var halfShort = Mathf.Min(node.size.x, node.size.y) * 0.5f;
                    if (halfShort > 0)
                    {
                        float NormR(float r) => Mathf.Clamp01(r / halfShort);
                        if (node.rectangleCornerRadii != null && node.rectangleCornerRadii.Length == 4)
                            figmaImg.CornerRadius = new Vector4(
                                NormR(node.rectangleCornerRadii[0]), NormR(node.rectangleCornerRadii[1]),
                                NormR(node.rectangleCornerRadii[2]), NormR(node.rectangleCornerRadii[3]));
                        else if (node.cornerRadius > 0)
                        {
                            var nr = NormR(node.cornerRadius);
                            figmaImg.CornerRadius = new Vector4(nr, nr, nr, nr);
                        }
                    }
                    return false;

                case Paint.PaintType.SOLID:
                    go.AddComponent<Image>().color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;

                default:
                    go.AddComponent<Image>().color = FigmaDataUtils.GetUnityFillColor(fill);
                    return false;
            }
        }

        /// <summary>
        /// Resolve 9-slice border size from [9Slice] tag, 3×3 grid pattern, or cornerRadius.
        /// Returns 0 if no 9-slice should be applied.
        /// </summary>
        private static float Resolve9SliceBorder(Node node, Sprite sprite, float tag9SliceBorder, bool autoSlice)
        {
            // [9Slice:N] tag — explicit border
            if (tag9SliceBorder > 0) return tag9SliceBorder;

            // [9Slice] tag without value — auto = 25% of shortest side
            if (tag9SliceBorder < 0 && sprite != null && sprite.texture != null)
                return Mathf.Min(sprite.texture.width, sprite.texture.height) * 0.25f;

            if (!autoSlice) return 0;

            // Auto-detect 3×3 grid pattern (9 Rectangle children with IMAGE fills → 9-slice)
            var gridBorder = Detect9SliceGrid(node);
            if (gridBorder > 0) return gridBorder;

            // Auto-detect from cornerRadius (+1 padding to avoid curve edge)
            var maxRadius = FigmaDataUtils.GetMaxCornerRadius(node);
            if (maxRadius > 0 && (node.type == NodeType.RECTANGLE || node.type == NodeType.FRAME
                                  || node.type == NodeType.COMPONENT || node.type == NodeType.INSTANCE))
                return maxRadius + 1;

            return 0;
        }

        /// <summary>
        /// Detect 9-slice 3×3 grid pattern: exactly 9 RECTANGLE children with IMAGE fills,
        /// arranged in a 3×3 grid where the 4 corners share the same size.
        /// Returns max(corner width, corner height) as border, or 0 if not detected.
        /// </summary>
        private static float Detect9SliceGrid(Node node)
        {
            if (node.children == null || node.children.Length != 9) return 0;

            // All children must be RECTANGLE with IMAGE fill
            foreach (var child in node.children)
            {
                if (child.type != NodeType.RECTANGLE) return 0;
                if (child.fills == null || child.fills.Length == 0) return 0;
                if (child.fills[0].type != Paint.PaintType.IMAGE) return 0;
            }

            // Sort by position: top-to-bottom, then left-to-right
            var sorted = new Node[9];
            System.Array.Copy(node.children, sorted, 9);
            System.Array.Sort(sorted, (a, b) =>
            {
                var ay = a.relativeTransform != null ? a.relativeTransform[1, 2] : 0f;
                var by = b.relativeTransform != null ? b.relativeTransform[1, 2] : 0f;
                var cmp = ay.CompareTo(by);
                if (cmp != 0) return cmp;
                var ax = a.relativeTransform != null ? a.relativeTransform[0, 2] : 0f;
                var bx = b.relativeTransform != null ? b.relativeTransform[0, 2] : 0f;
                return ax.CompareTo(bx);
            });

            // Verify 3 distinct rows
            float Row(Node n) => n.relativeTransform != null ? n.relativeTransform[1, 2] : 0f;
            var r0 = Row(sorted[0]);
            var r1 = Row(sorted[3]);
            var r2 = Row(sorted[6]);
            if (Mathf.Approximately(r0, r1) || Mathf.Approximately(r1, r2)) return 0;

            // Corner sizes: top-left [0], top-right [2], bottom-left [6], bottom-right [8]
            var tl = sorted[0].size;
            var tr = sorted[2].size;
            var bl = sorted[6].size;
            var br = sorted[8].size;

            // All corners should have matching width and matching height
            if (!Mathf.Approximately(tl.x, bl.x) || !Mathf.Approximately(tr.x, br.x)) return 0;
            if (!Mathf.Approximately(tl.y, tr.y) || !Mathf.Approximately(bl.y, br.y)) return 0;
            if (!Mathf.Approximately(tl.x, tr.x)) return 0; // left corners width == right corners width

            return Mathf.Max(tl.x, tl.y);
        }

        /// <summary>
        /// Ensure a sprite has 9-slice borders set based on corner radius.
        /// Re-imports the texture if the border needs updating.
        /// </summary>
        private static void EnsureSpriteBorder(Sprite sprite, float borderSize)
        {
            var path = AssetDatabase.GetAssetPath(sprite);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            var border = Mathf.CeilToInt(borderSize);
            var newBorder = new Vector4(border, border, border, border);

            if (importer.spriteBorder == newBorder) return;

            importer.spriteBorder = newBorder;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Apply TextMeshProUGUI with all text properties from Figma.
        /// </summary>
        private static void ApplyCleanText(GameObject go, Node node, FigmaBuildContext processData)
        {
            if (node.style == null) return;

            go.name = processData.Settings.SmartNaming
                ? FigmaNodeNaming.FormatTextName(go.name)
                : go.name + " (TMP)";
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
                // Quantize to nearest bucket to reduce material preset count
                outlineWidth = FontManager.QuantizeOutlineWidth(outlineWidth).width;
                var mat = FontManager.GetEffectMaterialPreset(
                    fontMapping, false, UnityEngine.Color.white, Vector2.zero,
                    true, outlineColor, outlineWidth);
                if (mat != null) text.fontMaterial = mat;
            }

            // Auto resize
            switch (node.style.textAutoResize)
            {
                case TypeStyle.TextAutoResize.NONE:
                    text.textWrappingMode = TMPro.TextWrappingModes.Normal;
                    break;
                case TypeStyle.TextAutoResize.HEIGHT:
                    var fitterH = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(go);
                    fitterH.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    break;
                case TypeStyle.TextAutoResize.WIDTH_AND_HEIGHT:
                    var fitterWH = UnityUiUtils.GetOrAddComponent<ContentSizeFitter>(go);
                    fitterWH.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fitterWH.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    break;
                case TypeStyle.TextAutoResize.TRUNCATE:
                    text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                    text.textWrappingMode = TMPro.TextWrappingModes.Normal;
                    break;
            }
        }
    }

}