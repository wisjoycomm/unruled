using UnityEditor;
using UnityEngine;

namespace Unruled.Editor
{
    /// <summary>
    /// Builds the board renderer's material: unlit, vertex-coloured, alpha-blended (built-in Sprites/Default, which
    /// every player build includes). Safe to run again: an existing material is kept.
    /// </summary>
    public static class BoardAssetBuilder
    {
        public const string MaterialPath = "Assets/Scripts/View/BoardMaterial.mat";
        private const string ShaderName = "Sprites/Default";

        [MenuItem("Unruled/Build Board Assets")]
        public static void Build()
        {
            bool created = BuildMaterial() != null;
            Debug.Log($"Unruled board: material {(created ? "ready" : "missing")} at {MaterialPath}.");
        }

        public static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Unruled board: shader '{ShaderName}' not found.");
                return null;
            }
            material = new Material(shader) { name = "BoardMaterial" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
