using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class ExperimentXrInteractorAudit
{
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/ExperimentXrInteractorAudit.request";
    private const string ReportPath = "Temp/ExperimentXrInteractorAudit.txt";

    [InitializeOnLoadMethod]
    private static void RunRequested()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath))) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequested();
                return;
            }
            Run();
            File.Delete(Path.GetFullPath(RequestPath));
        };
    }

    public static void Run()
    {
        if (!string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.OrdinalIgnoreCase))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var lines = new List<string>();
        MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours
            .Where(item => item is IXRInteractor || item.GetType().Name.IndexOf("Interactor", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(item => PathOf(item.transform)))
        {
            lines.Add($"INTERACTOR\t{PathOf(behaviour.transform)}\t{behaviour.GetType().FullName}\tenabled={behaviour.enabled}\tactive={behaviour.gameObject.activeInHierarchy}");
        }
        XRSimpleInteractable[] buttons = UnityEngine.Object.FindObjectsOfType<XRSimpleInteractable>(true);
        lines.Add($"SIMPLE_INTERACTABLES\t{buttons.Length}");
        foreach (XRSimpleInteractable button in buttons.Where(item => PathOf(item.transform).Contains("VR Experiment Scene Selector")))
            lines.Add($"BUTTON\t{PathOf(button.transform)}\tcollider={button.GetComponent<Collider>() != null}");
        File.WriteAllLines(Path.GetFullPath(ReportPath), lines);
    }

    private static string PathOf(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }
}
