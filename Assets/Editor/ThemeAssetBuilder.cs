using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Unruled.Editor
{
    /// <summary>
    /// Builds the UI theme's Unity-owned assets from the checked-in sources: a dynamic TextCore FontAsset next to
    /// every font file, and the PanelSettings that applies the Unruled theme style sheet. Safe to run again: existing
    /// assets are kept, only missing ones are created.
    /// </summary>
    public static class ThemeAssetBuilder
    {
        public const string ThemeFolder = "Assets/Scripts/View/UI/Theme";
        public const string FontFolder = ThemeFolder + "/Fonts";
        public const string ThemeStyleSheetPath = ThemeFolder + "/UnruledTheme.tss";
        public const string PanelSettingsPath = ThemeFolder + "/UnruledPanelSettings.asset";

        private static readonly Vector2Int ReferenceResolution = new Vector2Int(1440, 900);
        private static readonly Color Void = new Color32(0x0B, 0x12, 0x20, 0xFF);

        [MenuItem("Unruled/Build UI Theme Assets")]
        public static void Build()
        {
            int fonts = BuildFontAssets();
            bool panel = BuildPanelSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"Unruled theme: {fonts} font asset(s) created, panel settings {(panel ? "created" : "kept")}.");
        }

        private static int BuildFontAssets()
        {
            int created = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
            {
                var fontPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetPath = Path.ChangeExtension(fontPath, ".asset");
                if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) != null)
                    continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font == null)
                {
                    Debug.LogError($"Unruled theme: {fontPath} is not a font.");
                    continue;
                }

                var fontAsset = FontAsset.CreateFontAsset(font);
                if (fontAsset == null)
                {
                    Debug.LogError($"Unruled theme: could not create a font asset from {fontPath}.");
                    continue;
                }
                fontAsset.name = Path.GetFileNameWithoutExtension(fontPath);
                AssetDatabase.CreateAsset(fontAsset, assetPath);
                foreach (var atlas in fontAsset.atlasTextures)
                {
                    atlas.name = fontAsset.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                EditorUtility.SetDirty(fontAsset);
                created++;
            }
            return created;
        }

        private static bool BuildPanelSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null)
                return false;
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeStyleSheetPath);
            if (theme == null)
            {
                Debug.LogError($"Unruled theme: {ThemeStyleSheetPath} not found.");
                return false;
            }

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.clearColor = true;
            settings.colorClearValue = Void;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return true;
        }
    }
}
