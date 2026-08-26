using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class UncontrolledApparatusCleanup
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath =
        "Temp/UncontrolledApparatusCleanup.request";
    private const string ReportPath =
        "Temp/UncontrolledApparatusCleanup.txt";

    // Scene instances only. Source models and prefabs are intentionally untouched.
    private static readonly string[] ApparatusRootNames =
    {
        "glass support (1)",
        "crucible_and_cover (1)",
        "Glass_Lab_test_tube (4)",
        "Glass_Lab_test_tube (5)",
        "Erlenmeyer_flask (2)",
        "Erlenmeyer_flask (3)",
        "Crucible_Tongs (1)",
        "glass support (4)",
        "glass support (2)",
        "Erlenmeyer_flask with water",
        "Glass_Lab_test_tube with liquid",
        "Glass_Lab_test_tube with liquid (1)",
        "Glass_Lab_test_tube with liquid (2)",
        "Glass_Lab_test_tube with liquid (3)",
        "Erlenmeyer_flask with water (1)",
        "crucible_and_cover (2)",
        "Glass_Lab_test_tube (7)",
        "Glass_Lab_test_tube (6)",
        "Glass_Lab_test_tube with liquid (5)",
        "Glass_Lab_test_tube with liquid (4)",
        "Glass_Lab_test_tube with liquid (6)",
        "Boiling_Liquid_Stand (3)",
        "forceps (2)",
        "analytical_lab_instruments (3)",
        "Glass_Lab_test_tube with liquid (10)",
        "Glass_Lab_test_tube with liquid (9)"
    };

    private static readonly string[] RequiredStructuralRoots =
    {
        "water Shelf (1)",
        "wall01 (4)",
        "wall01 (5)",
        "shelf showcase (1)",
        "shelf showcase (2)",
        "shelf (1)",
        "floor (1)",
        "glass_frame (1)",
        "glass_frame (2)",
        "glass",
        "glass (1)",
        "stand glass",
        "stand glass (1)",
        "rack (2)",
        "rack (12)",
        "roof",
        "roof (1)",
        "roof (4)",
        "roof (6)",
        "Spot Light",
        "Area Light (13)",
        "pillar (5)",
        "drawer handle (2)",
        "drawer handle (3)",
        "PICO XR Origin",
        "streamcamera"
    };

    private static readonly string[] RequiredStableIds =
    {
        "china_dish",
        "graduated_cylinder",
        "beaker",
        "dropper",
        "florence_flask",
        "spirit_lamp",
        "test_tube_rack",
        "glass_funnel"
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
            Debug.Log($"[ApparatusCleanup] Report written to {ReportPath}.");
        };
    }

    [MenuItem("Tools/Stream Manager/Remove All Uncontrolled Apparatus")]
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
                $"Expected 8 protected targets before cleanup, found {targets.Count}.");
        }

        Dictionary<string, GameObject> roots = scene.GetRootGameObjects()
            .GroupBy(root => root.name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);

        foreach (string requiredName in RequiredStructuralRoots)
        {
            if (!roots.ContainsKey(requiredName))
            {
                throw new InvalidOperationException(
                    $"Required structural root '{requiredName}' was not found; cleanup aborted.");
            }
        }

        var removed = new List<string>();
        var alreadyAbsent = new List<string>();
        foreach (string apparatusName in ApparatusRootNames)
        {
            if (!roots.TryGetValue(apparatusName, out GameObject candidate))
            {
                alreadyAbsent.Add(apparatusName);
                continue;
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
                    $"Refusing to delete protected object '{apparatusName}'. " +
                    $"target={isOrContainsTarget}, semantic={hasSemanticObject}, " +
                    $"xrGrab={hasGrabInteractable}.");
            }

            UnityEngine.Object.DestroyImmediate(candidate);
            removed.Add(apparatusName);
        }

        foreach (PreparationSequenceController.Step step in sequence.Steps)
        {
            if (step?.target == null)
            {
                throw new InvalidOperationException(
                    "A sequence target reference was lost during cleanup.");
            }
        }

        foreach (string requiredName in RequiredStructuralRoots)
        {
            if (scene.GetRootGameObjects().All(root => root.name != requiredName))
            {
                throw new InvalidOperationException(
                    $"Required structural root '{requiredName}' was lost during cleanup.");
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

        foreach (string stableId in RequiredStableIds)
        {
            if (!registry.TryGet(stableId, out SemanticObject registeredObject) ||
                registeredObject == null)
            {
                throw new InvalidOperationException(
                    $"Protected registry entry '{stableId}' was lost during cleanup.");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save cleaned AStage scene.");
        }

        var report = new List<string>
        {
            $"Scene: {ScenePath}",
            $"Removed apparatus roots: {removed.Count}",
            $"Already absent: {alreadyAbsent.Count}",
            $"Remaining sequence targets: {targets.Count}",
            $"Remaining semantic objects: {registry.Count}",
            $"Verified structural roots: {RequiredStructuralRoots.Length}"
        };
        report.AddRange(removed.Select(name => $"REMOVED\t{name}"));
        report.AddRange(alreadyAbsent.Select(name => $"ABSENT\t{name}"));
        File.WriteAllLines(Path.GetFullPath(ReportPath), report);
        Debug.Log(
            $"[ApparatusCleanup] Removed {removed.Count} uncontrolled apparatus " +
            "roots. Eight protected targets and structural roots remain.");
    }
}
