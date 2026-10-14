using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ValidateRecoveredScenes
{
    [Serializable] class Entry
    {
        public string path;
        public int objects, renderers, missingScripts, missingReferences;
        public List<string> problems = new List<string>();
    }
    [Serializable] class Report
    {
        public string unityVersion;
        public List<Entry> scenes = new List<Entry>();
    }
    public static void Run()
    {
        var report = new Report { unityVersion = Application.unityVersion };
        foreach (string path in Directory.GetFiles("Assets/WhiteoutSurvival/Recovered3D/Scenes", "*.unity"))
        {
            var entry = new Entry { path = path };
            try
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    entry.objects++;
                    entry.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    if (transform.GetComponent<Renderer>() != null) entry.renderers++;
                    foreach (var component in transform.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        using (var serialized = new SerializedObject(component))
                        {
                            var property = serialized.GetIterator();
                            while (property.NextVisible(true))
                                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                    property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                {
                                    entry.missingReferences++;
                                    entry.problems.Add(transform.name + "/" + component.GetType().Name + ":" + property.propertyPath);
                                }
                        }
                    }
                }
            }
            catch (Exception e) { entry.problems.Add(e.Message); }
            report.scenes.Add(entry);
        }
        string reports = Path.GetFullPath(Path.Combine(Application.dataPath, "../../reports"));
        File.WriteAllText(Path.Combine(reports, "unity_scene_validation.json"), JsonUtility.ToJson(report, true));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Scene report records completeness; media validation exports its own verified package.
        ValidateMediaData.Run();
    }
}
