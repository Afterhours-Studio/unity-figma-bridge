using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    [Serializable]
    internal sealed class SyncedFrameManifest
    {
        public string frameId;
        public string documentVersion;
        public string lastModified;
        public string contentHash;
        public string syncedAt;

        private const string FILENAME = ".synced";

        /// <summary>
        /// Load manifest from a .synced file. Returns null if file doesn't exist or is legacy format.
        /// </summary>
        public static SyncedFrameManifest Load(string folderPath)
        {
            var path = Path.Combine(folderPath, FILENAME);
            if (!File.Exists(path)) return null;

            try
            {
                var content = File.ReadAllText(path).Trim();
                if (!content.StartsWith("{")) return null; // legacy plain-text format
                return JsonConvert.DeserializeObject<SyncedFrameManifest>(content);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Save manifest to .synced file in the given folder.
        /// </summary>
        public static void Save(string folderPath, SyncedFrameManifest manifest)
        {
            Directory.CreateDirectory(folderPath);
            var path = Path.Combine(folderPath, FILENAME);
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Check if a .synced file exists (regardless of format) for backwards compatibility.
        /// </summary>
        public static bool Exists(string folderPath)
        {
            return File.Exists(Path.Combine(folderPath, FILENAME));
        }
    }
}
