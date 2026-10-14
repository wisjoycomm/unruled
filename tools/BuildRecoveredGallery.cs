using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildRecoveredGallery
{
    const string RecoveredRoot = "Assets/RecoveredAssets/Recovered3D";

    public static void Run()
    {
        const string root = RecoveredRoot;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string[] names = {"blaster", "captain_hendrik", "chef", "cop", "doc", "explorer", "hunter", "hector", "molly", "eleonora", "freya", "beastmaster"};
        int count = 0;
        foreach (string name in names)
        {
            string path = root + "/Prefabs/Characters/battle_hero_" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new Exception("Missing gallery prefab: " + path);
            var holder = new GameObject(name + " display");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            holder.transform.rotation = Quaternion.Euler(0,180,0);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.ToLowerInvariant().Contains("shadow")) renderer.enabled = false;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            {
                var clips = animator.runtimeAnimatorController?.animationClips;
                var idle = clips?.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle")) ?? clips?.FirstOrDefault();
                if (idle != null) idle.SampleAnimation(animator.gameObject, idle.length * .15f);
            }
            var renderers = instance.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) throw new Exception("No visible gallery geometry: " + path);
            var bounds = VisibleBounds(instance);
            float scale = Mathf.Min(2.3f / bounds.size.y, 2.7f / bounds.size.x);
            holder.transform.localScale = Vector3.one * scale;
            holder.transform.position = new Vector3((count%4)*3.2f, (2-count/4)*3f,0) - bounds.center*scale;
            count++;
        }
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.25f;
        light.transform.eulerAngles = new Vector3(35,-30,0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.65f,.65f,.65f);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 4.8f;
        camera.transform.position = new Vector3(4.8f,3f,-20f);
        camera.transform.LookAt(new Vector3(4.8f,3f,0));
        camera.backgroundColor = new Color(.16f,.20f,.26f); camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.SaveScene(scene,root + "/Demo/CharacterGallery.unity");
        AssetDatabase.SaveAssets();
        ValidateRecovered3D.RenderGallery();
    }
    public static void Export()
    {
        string destination = Path.GetFullPath(Path.Combine(Application.dataPath, $"../../{Application.productName}_3D.unitypackage"));
        AssetDatabase.ExportPackage(RecoveredRoot, destination, ExportPackageOptions.Recurse);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static Bounds VisibleBounds(GameObject instance)
    {
        Bounds bounds = new Bounds(); bool initialized = false;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>().Where(r => r.enabled))
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin != null)
            {
                var mesh = new Mesh(); skin.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 point = skin.transform.TransformPoint(vertex);
                    if (!initialized) { bounds = new Bounds(point,Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            else
            {
                if (!initialized) { bounds = renderer.bounds; initialized = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }
    public static void FitDisplays()
    {
        int count = 0;
        foreach (var holder in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name.EndsWith(" display")))
        {
            holder.transform.localScale = Vector3.one; holder.transform.position = Vector3.zero;
            var bounds = VisibleBounds(holder);
            float scale = Mathf.Min(2.3f/bounds.size.y, 2.7f/bounds.size.x);
            holder.transform.localScale = Vector3.one*scale;
            holder.transform.position = new Vector3((count%4)*3.2f,(2-count/4)*3f,0)-bounds.center*scale;
            count++;
        }
    }
}
