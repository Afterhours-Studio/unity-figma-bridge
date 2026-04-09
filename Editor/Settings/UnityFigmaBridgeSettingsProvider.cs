using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterhours.FigmaBridge.Editor
{
    public class UnityFigmaBridgeSettingsProvider : SettingsProvider
    {
        private UnityFigmaBridgeSettings _settingsAsset;
        private SerializedObject _serializedSettings;

        public UnityFigmaBridgeSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settingsAsset = FindUnityBridgeSettingsAsset();
            if (_settingsAsset != null)
                _serializedSettings = new SerializedObject(_settingsAsset);
        }

        public static UnityFigmaBridgeSettings FindUnityBridgeSettingsAsset()
        {
            var assets = AssetDatabase.FindAssets($"t:{typeof(UnityFigmaBridgeSettings).Name}");
            if (assets == null || assets.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<UnityFigmaBridgeSettings>(AssetDatabase.GUIDToAssetPath(assets[0]));
        }

        public override void OnGUI(string searchContext)
        {
            if (_settingsAsset == null)
            {
                GUILayout.Label("Create Unity Figma Bridge Settings Asset");
                if (GUILayout.Button("Create..."))
                {
                    _settingsAsset = GenerateUnityFigmaBridgeSettingsAsset();
                    _serializedSettings = new SerializedObject(_settingsAsset);
                }
                return;
            }

            SettingsInspectorDrawer.DrawSettings(_settingsAsset, _serializedSettings);
        }

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider()
        {
            return new UnityFigmaBridgeSettingsProvider("Project/Unity Figma Bridge", SettingsScope.Project);
        }

        public static UnityFigmaBridgeSettings GenerateUnityFigmaBridgeSettingsAsset()
        {
            var newSettingsAsset = UnityFigmaBridgeSettings.CreateInstance<UnityFigmaBridgeSettings>();
            AssetDatabase.CreateAsset(newSettingsAsset, "Assets/UnityFigmaBridgeSettings.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Generating UnityFigmaBridgeSettings asset", newSettingsAsset);
            return newSettingsAsset;
        }
    }
}