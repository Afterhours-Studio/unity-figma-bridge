using System.Linq;
using System.Text.RegularExpressions;

namespace Afterhours.FigmaBridge.Editor
{
    /// <summary>
    /// Converts raw Figma node names into clean Unity GameObject names.
    /// Rules:
    ///   - [Tag] prefixes are stripped
    ///   - Special chars (/, -, &, ., etc.) are removed
    ///   - Empty result → FigmaElement_NNN (auto-incrementing, reset per frame)
    ///   - No spaces remaining → returned as-is
    ///   - All words start with uppercase → PascalCase  (Button Blue → ButtonBlue)
    ///   - Otherwise → snake_case, original per-word casing  (btn blue → btn_blue)
    /// </summary>
    internal static class FigmaNodeNaming
    {
        private static int _emptyCounter;

        public static void ResetCounter() => _emptyCounter = 0;

        public static string FormatName(string rawName)
        {
            // 1. Strip [Tag] prefixes
            var name = Regex.Replace(rawName ?? "", @"\[.*?\]", "").Trim();

            // 2. Remove special chars — keep letters, digits, spaces, underscores
            name = Regex.Replace(name, @"[^a-zA-Z0-9 _]", "");

            // 3. Collapse multiple spaces
            name = Regex.Replace(name, @" {2,}", " ").Trim();

            // 4. Empty fallback
            if (string.IsNullOrEmpty(name))
                return $"FigmaElement_{(++_emptyCounter):D3}";

            // 5. No spaces — already formatted
            if (!name.Contains(' '))
                return name;

            // 6. Split and determine casing
            var words = name.Split(' ');
            return words.All(w => w.Length > 0 && char.IsUpper(w[0]))
                ? string.Concat(words)           // PascalCase
                : string.Join("_", words);       // snake_case (preserve per-word case)
        }

        public static string FormatTextName(string rawName) => FormatName(rawName) + " (TMP)";
    }
}
