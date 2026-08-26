using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class ExperimentDuplicateInstanceCleanup
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath =
        "Temp/ExperimentDuplicateInstanceCleanup.request";
    private const string ReportPath =
        "Temp/ExperimentDuplicateInstanceCleanup.txt";

    private static readonly string[] DuplicateRootNames =
    {
        "Beaker (1)",
        "Beaker water (1)",
        "Beaker water (8)",
        "Graduated_Cylinder (2)",
        "glass_funnel (2)",
        "Glass_medical_dropper (1)",
        "Test_tube_rack (1)",
        "Test_tube_rack (3)",
        "Spirit_Lamp with water (7)",
        "Spirit_Lamp with water (8)"
    };

    [InitializeOnLoadMethod]
    private static void RunRequestedCleanup()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath)))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedCleanup();
                return;
            }

            Cleanup();
            File.Delete(Path.GetFullPath(RequestPath));
            Debug.Log($"[InstanceCleanup] Report written to {ReportPath}.");
        };
    }

    [MenuItem("Tools/Stream Manager/Remove Uncontrolled Duplicate Instances")]
    public static void Cleanup()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        if (sequence == null)
        {
            throw new InvalidOperationException(
                "PreparationSequenceController was not found.");
        }

        var targets = new HashSet<GameObject>(
            sequence.Steps
                .Where(step => step?.target != null)
                .Select(step => step.target));
        if (targets.Count != 8)
        {
            throw new InvalidOperationException(
                $"Expected 8 sequence targets before cleanup, found {targets.Count}.");
        }

        Dictionary<string, GameObject> roots = scene.GetRootGameObjects()
            .GroupBy(root => root.name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);
        var removed = new List<string>();
        foreach (string duplicateName in DuplicateRootNames)
        {
            if (!roots.TryGetValue(duplicateName, out GameObject candidate))
            {
                throw new InvalidOperationException(
                    $"Expected duplicate root '{duplicateName}' was not found.");
            }

            bool isOrContainsTarget = targets.Any(target =>
                target == candidate || target.transform.IsChildOf(candidate.transform));
            bool hasSemanticObject =
                candidate.GetComponentInChildren<SemanticObject>(true) != null;
            bool hasGrabInteractable =
                candidate.GetComponentInChildren<XRGrabInteractable>(true) != null;
            if (isOrContainsTarget || hasSemanticObject || hasGrabInteractable)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete protected object '{duplicateName}'. " +
                    $"target={isOrContainsTarget}, semantic={hasSemanticObject}, " +
                    $"xrGrab={hasGrabInteractable}.");
            }

            UnityEngine.Object.DestroyImmediate(candidate);
            removed.Add(duplicateName);
        }

        foreach (PreparationSequenceController.Step step in sequence.Steps)
        {
            if (step?.target == null)
            {
                throw new InvalidOperationException(
                    "A sequence target reference was lost during cleanup.");
            }
        }

        SceneObjectRegistry registry =
            UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        if (registry == null)
        {
            throw new InvalidOperationException("SceneObjectRegistry was not found.");
        }
        registry.Refresh();
        if (registry.Count != 8)
        {
            throw new InvalidOperationException(
                $"Registry contains {registry.Count} semantic objects after cleanup; expected 8.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save cleaned AStage scene.");
        }

        var report = new List<string>
        {
            $"Scene: {ScenePath}",
            $"Removed: {removed.Count}",
            $"Remaining sequence targets: {targets.Count}",
            $"Remaining semantic objects: {registry.Count}"
        };
        report.AddRange(removed.Select(name => $"REMOVED\t{name}"));
        File.WriteAllLines(Path.GetFullPath(ReportPath), report);
        Debug.Log(
            $"[InstanceCleanup] Removed {removed.Count} uncontrolled duplicate " +
            $"instances. Eight protected targets remain registered.");
    }
}
