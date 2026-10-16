using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Unruled.View;
using Unruled.View.UI;
using Unruled.View.UI.Theme;

namespace Unruled.Editor
{
    /// <summary>
    /// Builds the bootstrap scene: the board camera, the HUD document with <see cref="MainScreenView"/>, the board
    /// mesh with <see cref="MapBoardRenderer"/> and the <see cref="GameSession"/> that runs the game. Rebuilding
    /// replaces the scene file; the scene is first in the build list.
    /// </summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        private const string MainScreenPath = "Assets/Scripts/View/UI/MainScreen.uxml";

        [MenuItem("Unruled/Build Main Scene")]
        public static void Build()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                BuildScene();
        }

        /// <summary>Builds and saves the scene without asking about the open one (its changes are dropped).</summary>
        public static void BuildScene()
        {
            // Load the assets only after NewScene: opening a scene unloads unused assets loaded before it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainScreenPath);
            if (layout == null)
            {
                Debug.LogError($"Unruled scene: {MainScreenPath} not found.");
                return;
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(ThemeAssetBuilder.PanelSettingsPath);
            if (panel == null)
            {
                Debug.LogError($"Unruled scene: {ThemeAssetBuilder.PanelSettingsPath} not found; run Unruled/Build Theme Assets.");
                return;
            }
            var material = BoardAssetBuilder.BuildMaterial();
            if (material == null)
                return;

            var cameraObject = new GameObject("Board Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UnruledPalette.Void;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            var hud = new GameObject("HUD");
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = layout;
            var screen = hud.AddComponent<MainScreenView>();
            SetReference(screen, "_document", document);

            var boardObject = new GameObject("Board");
            var board = boardObject.AddComponent<MapBoardRenderer>();
            boardObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            SetReference(board, "_camera", camera);

            var session = new GameObject("Game Session").AddComponent<GameSession>();
            SetReference(session, "_screen", screen);
            SetReference(session, "_board", board);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Unruled scene: could not save {ScenePath}.");
                return;
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"Unruled scene: built {ScenePath}.");
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
                throw new MissingReferenceException($"Unruled scene: {target.GetType().Name}.{field} not found.");
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
