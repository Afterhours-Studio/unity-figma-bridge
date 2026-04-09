using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    /// <summary>
    /// Caches the Figma document JSON to disk so the Build tab can read it without re-fetching.
    /// </summary>
    public static class FigmaDocumentCache
    {
        private const string CACHE_FILENAME = ".figma-cache.json";

        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            DefaultValueHandling = DefaultValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter { AllowIntegerValues = true } },
            Error = (sender, args) => { args.ErrorContext.Handled = true; },
        };

        /// <summary>
        /// Path to the cache file inside the asset output folder.
        /// </summary>
        public static string CachePath => Path.Combine(FigmaPaths.FigmaAssetsRootFolder, CACHE_FILENAME);

        /// <summary>
        /// Whether a cached document exists on disk.
        /// </summary>
        public static bool Exists => File.Exists(CachePath);

        /// <summary>
        /// Save a FigmaFile to disk as JSON.
        /// </summary>
        public static void Save(FigmaFile file)
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(file, Formatting.None, SerializerSettings);
            File.WriteAllText(CachePath, json);
            Debug.Log($"[FigmaBridge] Document cached to {CachePath}");
        }

        /// <summary>
        /// Load a cached FigmaFile from disk. Returns null on failure.
        /// </summary>
        public static FigmaFile Load()
        {
            if (!Exists)
            {
                Debug.LogWarning("[FigmaBridge] No cached document found.");
                return null;
            }

            try
            {
                var json = File.ReadAllText(CachePath);
                return JsonConvert.DeserializeObject<FigmaFile>(json, SerializerSettings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigmaBridge] Failed to load cached document: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the last write time of the cache file, or null if it doesn't exist.
        /// </summary>
        public static DateTime? LastModified => Exists ? File.GetLastWriteTime(CachePath) : null;
    }
}
