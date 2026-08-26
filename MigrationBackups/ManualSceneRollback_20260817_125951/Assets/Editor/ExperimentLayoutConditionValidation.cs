using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentLayoutConditionValidation
{
    // Revision 3 also rejects the legacy serialized XRI manager.
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/ExperimentLayoutConditionValidation.request";
    private const string ReportPath = "Temp/ExperimentLayoutConditionValidation.txt";

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

    [MenuItem("Tools/Stream Manager/Distractors/Validate All Layout Conditions")]
    public static void Run()
    {
        Scene scene = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ExperimentDistractorLayoutGenerator generator =
            UnityEngine.Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        SceneObjectRegistry registry = UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        if (generator == null || registry == null || generator.PlacementSurface == null)
            throw new InvalidOperationException("Generator, registry, or placement surface is missing.");

        var lines = new List<string>
        {
            $"Scene: {ScenePath}",
            $"Surface: {generator.PlacementSurface.name}",
            $"Surface bounds: {generator.PlacementSurface.bounds}"
        };
        ValidateControlPanel(scene, lines);
        Validate(generator, registry, ExperimentLayoutSelection.Low, 4, lines);
        Validate(generator, registry, ExperimentLayoutSelection.Medium, 8, lines);
        Validate(generator, registry, ExperimentLayoutSelection.High, 10, lines);
        generator.ApplySelection(ExperimentLayoutSelection.Clear, "editor_validation");
        registry.Refresh();
        if (generator.GeneratedCount != 0 || registry.Count != 8)
            throw new InvalidOperationException("CLEAR did not restore the clean eight-target state.");
        lines.Add("CLEAR\tPASS\tgenerated=0\tregistry=8");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllLines(Path.GetFullPath(ReportPath), lines);
        Debug.Log("[LayoutConditionValidation] PASS. All conditions fit the tabletop; clean base restored.");
    }

    private static void ValidateControlPanel(Scene scene, List<string> lines)
    {
        GameObject[] objects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .ToArray();
        GameObject panel = objects.FirstOrDefault(item =>
            item.name == "VR Experiment Scene Selector");
        GameObject ray = objects.FirstOrDefault(item =>
            item.name == "PICO Experiment Layout Ray");
        ExperimentVrLayoutSelector selector =
            panel?.GetComponent<ExperimentVrLayoutSelector>();
        PicoExperimentLayoutRaySelector raySelector =
            ray?.GetComponent<PicoExperimentLayoutRaySelector>();
        if (selector == null || raySelector == null ||
            raySelector.RayOrigin == null || raySelector.PicoHand == null)
        {
            throw new InvalidOperationException(
                "Static layout panel or configured PICO ray is missing.");
        }

        string[] buttonNames =
            { "Button Low", "Button Medium", "Button High", "Button Clear" };
        foreach (string buttonName in buttonNames)
        {
            GameObject button = objects.FirstOrDefault(item => item.name == buttonName);
            if (button == null || button.GetComponent<Collider>() == null ||
                button.GetComponent<ExperimentVrLayoutButton>() == null)
            {
                throw new InvalidOperationException(
                    $"Control panel button is incomplete: {buttonName}.");
            }
        }

        if (UnityEngine.Object.FindObjectOfType<ExperimentRuntimeUiBootstrap>(true) != null)
        {
            throw new InvalidOperationException(
                "Runtime UI bootstrap must remain absent from the PICO scene.");
        }
        if (objects.Any(item => item.name == "XR Interaction Manager"))
        {
            throw new InvalidOperationException(
                "Legacy serialized XR Interaction Manager must be removed.");
        }

        int missingScripts = objects.Sum(
            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
        if (missingScripts != 0)
        {
            throw new InvalidOperationException(
                $"Scene contains {missingScripts} missing script reference(s).");
        }

        lines.Add(
            $"CONTROL_PANEL\tPASS\tbuttons={buttonNames.Length}\t" +
            "ray=PICO HandRight/RayPose\truntimeUiBootstrap=absent\t" +
            "xriManager=codeBootstrap\tmissingScripts=0");
    }

    private static void Validate(
        ExperimentDistractorLayoutGenerator generator,
        SceneObjectRegistry registry,
        ExperimentLayoutSelection selection,
        int expected,
        List<string> lines)
    {
        generator.ApplySelection(selection, "editor_validation");
        registry.Refresh();
        GeneratedDistractorMarker[] markers = UnityEngine.Object
            .FindObjectsOfType<GeneratedDistractorMarker>(true);
        Bounds surface = generator.PlacementSurface.bounds;
        int colliderCount = 0;
        float maximumBottomError = 0f;
        foreach (GeneratedDistractorMarker marker in markers)
        {
            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"{marker.name} has no Renderer.");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            const float tolerance = 0.0025f;
            if (bounds.min.x < surface.min.x - tolerance || bounds.max.x > surface.max.x + tolerance ||
                bounds.min.z < surface.min.z - tolerance || bounds.max.z > surface.max.z + tolerance)
                throw new InvalidOperationException($"{marker.name} extends beyond the tabletop: {bounds}.");
            float bottomError = Mathf.Abs(bounds.min.y - surface.max.y);
            maximumBottomError = Mathf.Max(maximumBottomError, bottomError);
            if (bottomError > tolerance)
                throw new InvalidOperationException($"{marker.name} is not on the tabletop; bottom error={bottomError:F5}.");
            if (marker.GetComponentInChildren<Collider>(true) != null) colliderCount++;
        }

        int uniqueIds = registry.Objects.Select(item => item.StableId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (markers.Length != expected || generator.GeneratedCount != expected ||
            registry.Count != expected + 8 || uniqueIds != expected + 8 || colliderCount != expected)
            throw new InvalidOperationException(
                $"{selection} failed: markers={markers.Length}, generated={generator.GeneratedCount}, " +
                $"registry={registry.Count}, uniqueIds={uniqueIds}, colliders={colliderCount}.");
        lines.Add($"{selection.ToString().ToUpperInvariant()}\tPASS\tgenerated={expected}\t" +
            $"registry={registry.Count}\tcolliders={colliderCount}\tmaxBottomError={maximumBottomError:F5}");
    }
}
