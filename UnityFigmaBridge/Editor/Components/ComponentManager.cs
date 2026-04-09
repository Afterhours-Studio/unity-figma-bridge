using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor.Components
{
    public static class ComponentManager
    {
        /// <summary>
        /// Remove component placeholders that are used to mark instantiation locations
        /// </summary>
        public static void RemoveAllTemporaryNodeComponents(FigmaImportProcessData figmaImportProcessData)
        {
            // Remove from screens
            foreach (var framePrefab in figmaImportProcessData.ScreenPrefabs)
            {
                if (framePrefab != null)
                    RemoveTemporaryNodeComponents(framePrefab);
            }
            // Remove from pages
            foreach (var pagePrefab in figmaImportProcessData.PagePrefabs)
            {
                if (pagePrefab != null)
                    RemoveTemporaryNodeComponents(pagePrefab);
            }
        }

        private static void RemoveTemporaryNodeComponents(GameObject sourcePrefab)
        {
            var assetPath = AssetDatabase.GetAssetPath(sourcePrefab);
            var prefabContents = PrefabUtility.LoadPrefabContents(assetPath);
            var allPlaceholderComponents = prefabContents.GetComponentsInChildren<FigmaNodeObject>();
            foreach (var placeholder in allPlaceholderComponents)
                Object.DestroyImmediate(placeholder);
            PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }
    }
}
