using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Afterhours.FigmaBridge.Editor
{
    /// <summary>
    /// Computes a deterministic content hash of a Figma node subtree.
    /// Used by incremental sync to detect which frames changed.
    /// </summary>
    internal static class NodeHasher
    {
        private static readonly JsonSerializerSettings s_Settings = new()
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter { AllowIntegerValues = true } },
            Error = (sender, args) => { args.ErrorContext.Handled = true; },
        };

        /// <summary>
        /// Serialize a node subtree to JSON and return its SHA256 hex string.
        /// </summary>
        public static string ComputeHash(Node node)
        {
            var json = JsonConvert.SerializeObject(node, s_Settings);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
