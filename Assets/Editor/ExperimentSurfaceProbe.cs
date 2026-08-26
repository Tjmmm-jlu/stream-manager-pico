using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentSurfaceProbe
{
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/ExperimentSurfaceProbe.request";
    private const string ReportPath = "Temp/ExperimentSurfaceProbe.txt";

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

    [MenuItem("Tools/Stream Manager/Distractors/Probe Placement Surfaces")]
    public static void Run()
    {
        Scene scene = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PreparationSequenceController sequence = UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        if (sequence == null) throw new InvalidOperationException("Sequence controller missing.");
        List<GameObject> targets = sequence.Steps.Where(s => s?.target != null).Select(s => s.target).Distinct().ToList();
        var targetSet = new HashSet<GameObject>(targets);
        var lines = new List<string> { $"Scene: {ScenePath}", $"Targets: {targets.Count}", "", "TARGETS" };

        foreach (GameObject target in targets)
        {
            Bounds targetBounds = BoundsOf(target);
            lines.Add($"TARGET\t{target.name}\tpos={target.transform.position}\tminY={targetBounds.min.y:F4}\tbounds={targetBounds}");
            IEnumerable<Renderer> supports = scene.GetRootGameObjects()
                .Where(root => !targetSet.Contains(root) && root.name != "Experiment Distractor Layout")
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null &&
                    renderer.bounds.min.x <= targetBounds.center.x && renderer.bounds.max.x >= targetBounds.center.x &&
                    renderer.bounds.min.z <= targetBounds.center.z && renderer.bounds.max.z >= targetBounds.center.z &&
                    renderer.bounds.max.y <= targetBounds.min.y + 0.08f)
                .OrderByDescending(renderer => renderer.bounds.max.y)
                .Take(5);
            foreach (Renderer support in supports)
            {
                lines.Add($"SUPPORT\t{PathOf(support.transform)}\ttopY={support.bounds.max.y:F4}\tbounds={support.bounds}");
            }
        }

        Bounds combined = BoundsOf(targets[0]);
        foreach (GameObject target in targets.Skip(1)) combined.Encapsulate(BoundsOf(target));
        lines.Add("");
        lines.Add($"COMBINED\tcenter={combined.center}\tsize={combined.size}");
        lines.Add("NEARBY_ROOT_BOUNDS");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds bounds = BoundsOf(root);
            Vector2 delta = new Vector2(bounds.center.x - combined.center.x, bounds.center.z - combined.center.z);
            if (delta.magnitude <= 4f)
                lines.Add($"ROOT\t{root.name}\tcenter={bounds.center}\tsize={bounds.size}\tminY={bounds.min.y:F4}\tmaxY={bounds.max.y:F4}");
        }
        File.WriteAllLines(Path.GetFullPath(ReportPath), lines);
        Debug.Log("[ExperimentSurfaceProbe] Report written.");
    }

    private static Bounds BoundsOf(GameObject gameObject)
    {
        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(gameObject.transform.position, Vector3.one * 0.01f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static string PathOf(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }
}
