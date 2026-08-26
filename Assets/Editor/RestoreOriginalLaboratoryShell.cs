using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RestoreOriginalLaboratoryShell
{
    private const string SourceScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    private const string ExperimentScenePath =
        "Assets/Scenes/Preparation Experiment Scene.unity";
    private const string SessionKey =
        "PreparationExperiment.RestoreOriginalShellV2";

    private static readonly Bounds ShellBounds =
        new Bounds(
            new Vector3(5.35f, 2f, 5.45f),
            new Vector3(5.5f, 5f, 5.5f));

    private static double _runAt;

    static RestoreOriginalLaboratoryShell()
    {
        if (!Application.isBatchMode &&
            !SessionState.GetBool(SessionKey, false))
        {
            SessionState.SetBool(SessionKey, true);
            _runAt = EditorApplication.timeSinceStartup + 1d;
            EditorApplication.update += RunWhenReady;
        }
    }

    [MenuItem("PICO/Restore Local Laboratory Shell", priority = 22)]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene experiment =
            EditorSceneManager.OpenScene(
                ExperimentScenePath,
                OpenSceneMode.Single);

        foreach (GameObject root in experiment.GetRootGameObjects().ToArray())
        {
            if (IsStructuralRoot(root.name))
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        Scene source =
            EditorSceneManager.OpenScene(
                SourceScenePath,
                OpenSceneMode.Additive);
        int restored = 0;
        foreach (GameObject sourceRoot in source.GetRootGameObjects())
        {
            if (!IsStructuralRoot(sourceRoot.name) ||
                !IntersectsLocalShell(sourceRoot))
            {
                continue;
            }

            GameObject clone = UnityEngine.Object.Instantiate(sourceRoot);
            clone.name = sourceRoot.name;
            SceneManager.MoveGameObjectToScene(clone, experiment);
            restored++;
        }

        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(experiment);
        EditorSceneManager.MarkSceneDirty(experiment);
        EditorSceneManager.SaveScene(experiment, ExperimentScenePath);
        AssetDatabase.SaveAssets();

        int rendererCount =
            UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length;
        Debug.Log(
            $"[Preparation Shell V2] Restored {restored} local structural roots. " +
            $"Total renderers={rendererCount}, bounds={ShellBounds}.");
    }

    private static void RunWhenReady()
    {
        if (EditorApplication.timeSinceStartup < _runAt ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        EditorApplication.update -= RunWhenReady;
        Run();
    }

    private static bool IntersectsLocalShell(GameObject root)
    {
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        return renderers.Any(
            renderer =>
                renderer != null &&
                renderer.bounds.Intersects(ShellBounds));
    }

    private static bool IsStructuralRoot(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.StartsWith("floor", StringComparison.Ordinal) ||
               lower.StartsWith("wall01", StringComparison.Ordinal) ||
               lower.StartsWith("roof", StringComparison.Ordinal) ||
               lower.StartsWith("pillar", StringComparison.Ordinal);
    }
}
