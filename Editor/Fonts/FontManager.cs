using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using Color = UnityEngine.Color;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    
    public class FigmaFontMapEntry
    {
        public string FontFamily;
        public int FontWeight;
        public TMP_FontAsset FontAsset;
        public List<FontMaterialVariation> FontmaterialVariations = new List<FontMaterialVariation>();
    }
    
    
    /// <summary>
    /// Class to map text effects (outline and shadow) to material presets
    /// </summary>
    public class FontMaterialVariation
    {
        public bool OutlineEnabled;
        public Color OutlineColor;
        public float OutlineThickness;
        
        public bool ShadowEnabled;
        public Color ShadowColor;
        public Vector2 ShadowDistance;
        
        public Material MaterialPreset;
       
    }
    
    
    public class FigmaFontMap
    {
        public List<FigmaFontMapEntry> FontMapEntries = new List<FigmaFontMapEntry>();

        public FigmaFontMapEntry GetFontMapping(string fontFamily, int fontWeight)
        {
            return FontMapEntries.FirstOrDefault(fontMapEntry => fontMapEntry.FontFamily == fontFamily && fontMapEntry.FontWeight == fontWeight);
        }
    }
    
    /// <summary>
    /// Functionality to manage fonts, retrive and generate font assets
    /// </summary>
    public static class FontManager
    {
        /// <summary>
        /// Outline thickness buckets — snaps continuous Figma outline values to a small
        /// fixed set so we create far fewer material presets.
        /// Each entry: (upper threshold, quantized value, human label).
        /// </summary>
        private static readonly (float threshold, float value, string label)[] OutlineBuckets =
        {
            (0.15f, 0.10f, "Thin"),
            (0.30f, 0.22f, "Medium"),
            (0.50f, 0.40f, "Thick"),
        };

        /// <summary>
        /// Snap a raw outline width (0–0.5) to the nearest preset bucket.
        /// Returns the quantized width and a human-readable label for material naming.
        /// </summary>
        public static (float width, string label) QuantizeOutlineWidth(float rawWidth)
        {
            foreach (var bucket in OutlineBuckets)
            {
                if (rawWidth <= bucket.threshold)
                    return (bucket.value, bucket.label);
            }
            // Beyond all thresholds → clamp to the largest bucket
            var last = OutlineBuckets[OutlineBuckets.Length - 1];
            return (last.value, last.label);
        }
        /// <summary>
        /// Generates a map of fonts found int the document and font to map to
        /// </summary>
        /// <param name="figmaFile"></param>
        /// <param name="enableGoogleFontsDownload"></param>
        /// <returns></returns>
        public static async Task<FigmaFontMap> GenerateFontMapForDocument(FigmaFile figmaFile, bool enableGoogleFontsDownload,
            List<string> selectedFrameIds = null, string sectionFilter = null)
        {
            FigmaFontMap fontMap = new FigmaFontMap();
            var textNodes = new List<Node>();

            // Collect text nodes only from the frames being imported
            foreach (var page in figmaFile.document.children ?? System.Array.Empty<Node>())
            {
                if (page.children == null) continue;
                foreach (var child in page.children)
                {
                    if (child.type == NodeType.SECTION)
                    {
                        if (!string.IsNullOrEmpty(sectionFilter) && child.name != sectionFilter) continue;
                        if (child.children == null) continue;
                        foreach (var frame in child.children)
                        {
                            if (frame.type != NodeType.FRAME) continue;
                            if (selectedFrameIds != null && selectedFrameIds.Count > 0 && !selectedFrameIds.Contains(frame.id)) continue;
                            FigmaDataUtils.FindAllNodesOfType(frame, NodeType.TEXT, textNodes, 0);
                        }
                    }
                    else if (child.type == NodeType.FRAME)
                    {
                        if (!string.IsNullOrEmpty(sectionFilter)) continue;
                        if (selectedFrameIds != null && selectedFrameIds.Count > 0 && !selectedFrameIds.Contains(child.id)) continue;
                        FigmaDataUtils.FindAllNodesOfType(child, NodeType.TEXT, textNodes, 0);
                    }
                }
            }
            
            var allProjectFontAssets = AssetDatabase.FindAssets($"t:TMP_FontAsset").Select(guid => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid))).ToList();

            // Cycle through each node, to see if we have a match for each
            foreach (var textNode in textNodes)
            {
                var fontFamily = textNode.style.fontFamily;
                var fontWeight = textNode.style.fontWeight;
                var fontMapEntry = fontMap.GetFontMapping(fontFamily, fontWeight);
                if (fontMapEntry != null) continue;
                
                var newFontMapEntry = new FigmaFontMapEntry
                {
                    FontFamily = fontFamily,
                    FontWeight = fontWeight
                };
                fontMap.FontMapEntries.Add(newFontMapEntry);
                if (GoogleFontLibraryManager.CheckFontExistsLocally(fontFamily, fontWeight))
                {
                    newFontMapEntry.FontAsset = GoogleFontLibraryManager.GetFontAsset(fontFamily, fontWeight);
                }
                else if (enableGoogleFontsDownload && GoogleFontLibraryManager.CheckFontAvailableForDownload(fontFamily, fontWeight))
                {
                    var downloadTask = GoogleFontLibraryManager.ImportFont(fontFamily, fontWeight);
                    await downloadTask;
                    if (downloadTask.Result)
                    {
                        // Success
                        newFontMapEntry.FontAsset=GoogleFontLibraryManager.GetFontAsset(fontFamily, fontWeight);
                    }
                }

                if (newFontMapEntry.FontAsset == null)
                    newFontMapEntry.FontAsset = GetClosestFont(allProjectFontAssets, fontFamily, fontWeight);
            }

            // Second pass: re-scan project fonts (includes anything just downloaded) for still-unresolved entries
            var updatedFontAssets = AssetDatabase.FindAssets("t:TMP_FontAsset")
                .Select(guid => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(f => f != null).ToList();
            foreach (var entry in fontMap.FontMapEntries)
            {
                if (entry.FontAsset != null) continue;
                entry.FontAsset = GetClosestFont(updatedFontAssets, entry.FontFamily, entry.FontWeight);
            }

            return fontMap;
        }
        
        
        
        static string StripFontDetailsFromName(TMP_FontAsset fontAsset)
        {
            // By default fonts are added with a hyphen to denote weight variations, so strip everything from hyphen
            var fontName = fontAsset.name.ToLower();
            var hyphenPoint = fontName.IndexOf('-');
            if (hyphenPoint > -1) fontName = fontName.Substring(0, hyphenPoint);
            // Remove any extra keywords
            var stripWords = new string[]
            {
                "sdf",
                "regular",
                "bold",
                "italic",
                " "
            };
            foreach (var stripWord in stripWords)
            {
                fontName= fontName.Replace(stripWord, "");
            }
            return fontName;
        }

        private static TMP_FontAsset GetClosestFont(List<TMP_FontAsset> projectFonts,string fontFamily,int fontWeight)
        {
            var lowestMatchScore = 10000000;
            TMP_FontAsset closestMatch = null;
            
            // Make lower case and strip spaces
            var inputNameLower = fontFamily.ToLower().Replace(" ", "");;
            
            // Use Levenshtein distance to calculate best match from available strings
            foreach (var font in projectFonts)
            {
                var strippedFontName = StripFontDetailsFromName(font);
               
                var newScore = MathUtils.LeventshteinStringDistance(inputNameLower, strippedFontName);
                //Debug.Log($"Checking font name {strippedFontName} vs {inputNameLower} score {newScore}");
                if (newScore < lowestMatchScore)
                {
                    closestMatch = font;
                    lowestMatchScore = newScore;
                }
            }
            return closestMatch;
        }
        
        public static Material GetEffectMaterialPreset(FigmaFontMapEntry fontMapEntry, bool shadow, Color shadowColor,
            Vector2 shadowDistance, bool outline,
            Color outlineColor, float outlineThickness)
        {
            // Do we have a matching material?
            var materialPresets = fontMapEntry.FontmaterialVariations.Count;
            
            
            foreach (var materialPreset in fontMapEntry.FontmaterialVariations)
            {
                bool isMatch = true;
                if (materialPreset.ShadowEnabled != shadow) isMatch = false;
                if (shadow && materialPreset.ShadowColor!=shadowColor) isMatch = false;
                if (shadow && materialPreset.ShadowDistance!=shadowDistance) isMatch = false;
                
                if (materialPreset.OutlineEnabled != outline) isMatch = false;
                if (outline && materialPreset.OutlineColor != outlineColor) isMatch = false;
                if (outline && materialPreset.OutlineThickness != outlineThickness) isMatch = false;

                if (isMatch) return materialPreset.MaterialPreset;
            }
            // No match, create new preset
            var newMaterialPreset = new Material(fontMapEntry.FontAsset.material);
            // We use a modified shader that handles distance from edge better
            newMaterialPreset.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            
            var nameSuffix = new System.Text.StringBuilder();
            string outlineLabel = null;
            if (outline)
            {
                var q = QuantizeOutlineWidth(outlineThickness);
                outlineThickness = q.width;
                outlineLabel = q.label;
                nameSuffix.Append($"_Outline_{outlineLabel}");
            }
            if (shadow) nameSuffix.Append("_Shadow");
            var materialName = $"{fontMapEntry.FontAsset.name}{nameSuffix}";
            newMaterialPreset.name = materialName;

            // Use EnableKeyword/DisableKeyword — shader_feature keywords must be toggled as global keywords
            if (shadow)
            {
                newMaterialPreset.EnableKeyword("UNDERLAY_ON");
                newMaterialPreset.SetFloat("_UnderlayOffsetX", 0);
                newMaterialPreset.SetFloat("_UnderlayOffsetY", -0.6f);
                newMaterialPreset.SetFloat("_UnderlayDilate", 0);
                newMaterialPreset.SetFloat("_UnderlaySoftness", 0);
                newMaterialPreset.SetColor("_UnderlayColor", shadowColor);
            }
            else
            {
                newMaterialPreset.DisableKeyword("UNDERLAY_ON");
                newMaterialPreset.DisableKeyword("UNDERLAY_INNER");
                newMaterialPreset.SetFloat("_UnderlayOffsetX", 0);
                newMaterialPreset.SetFloat("_UnderlayOffsetY", 0);
                newMaterialPreset.SetFloat("_UnderlayDilate", 0);
                newMaterialPreset.SetFloat("_UnderlaySoftness", 0);
                newMaterialPreset.SetColor("_UnderlayColor", new Color(0, 0, 0, 0));
            }

            if (outline)
            {
                newMaterialPreset.EnableKeyword("OUTLINE_ON");
                // Dilate face by outlineThickness so the outline renders fully outside the glyph
                newMaterialPreset.SetFloat("_FaceDilate", outlineThickness);
                newMaterialPreset.SetFloat("_OutlineWidth", outlineThickness);
                newMaterialPreset.SetFloat("_OutlineSoftness", 0f);
                newMaterialPreset.SetColor("_OutlineColor", outlineColor);
            }
            else
            {
                newMaterialPreset.DisableKeyword("OUTLINE_ON");
                newMaterialPreset.SetFloat("_FaceDilate", 0f);
                newMaterialPreset.SetFloat("_OutlineWidth", 0f);
                newMaterialPreset.SetFloat("_OutlineSoftness", 0f);
                newMaterialPreset.SetColor("_OutlineColor", Color.clear);
            }
            
            AssetDatabase.CreateAsset(newMaterialPreset, $"{FigmaPaths.FigmaFontMaterialPresetsFolder}/{materialName}.mat");

            fontMapEntry.FontmaterialVariations.Add(new FontMaterialVariation
            {
                ShadowEnabled=shadow,
                ShadowColor = shadowColor,
                ShadowDistance = shadowDistance,
                OutlineEnabled = outline,
                OutlineColor = outlineColor,
                OutlineThickness = outlineThickness,
                MaterialPreset = newMaterialPreset
            });
            return newMaterialPreset;
        }
        
    }
}
