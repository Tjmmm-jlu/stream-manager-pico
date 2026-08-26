using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AStageFixedHighBuilder
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/AStageFixedHighBuilder.request";
    private const string ReportPath = "Temp/AStageFixedHighBuilder.txt";
    private const string OutputPath =
        "Builds/stream-manager-pico-astage-high.apk";

    [InitializeOnLoadMethod]
    private static void RunRequestedBuild()
    {
        string request = Path.GetFullPath(RequestPath);
        if (!File.Exists(request))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedBuild();
                return;
            }

            File.Delete(request);
            BuildFixedHighScene();
        };
    }

    [MenuItem("Tools/Stream Manager/Build/Fixed High PICO AStage APK")]
    public static void BuildFixedHighScene()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException(
                "Unable to resolve the Unity project root.");
        string output = Path.Combine(
            projectRoot,
            OutputPath.Replace('/', Path.DirectorySeparatorChar));
        string reportPath = Path.Combine(
            projectRoot,
            ReportPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException(
                "Unable to resolve the APK output directory."));

        DateTime started = DateTime.Now;
        var reportLines = new List<string>
        {
            $"Started: {started:O}",
            $"Scene: {ScenePath}",
            "Layout: fixed High",
            "Expected runtime distractors: 10",
            "Expected serialized distractors: 0",
            "Legacy layout panel: removed"
        };

        try
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            int removedObjects = RemoveLegacyLayoutControls(scene);

            ExperimentDistractorLayoutGenerator generator =
                UnityEngine.Object.FindObjectOfType<
                    ExperimentDistractorLayoutGenerator>(true);
            SceneObjectRegistry registry =
                UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
            if (generator == null || registry == null)
            {
                throw new InvalidOperationException(
                    "AStage generator or scene registry is missing.");
            }

            generator.ApplySelection(
                ExperimentLayoutSelection.Clear,
                "fixed_high_scene_cleanup");
            int removedSerializedDistractors =
                RemoveSerializedDistractors(scene);
            int removedOrphanedDistractors =
                RemoveOrphanedDistractorSemantics(scene);
            registry.Refresh();

            int missingScripts = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Sum(transform =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject));
            bool hasLegacyControls = UnityEngine.Object
                .FindObjectsOfType<ExperimentVrLayoutSelector>(true)
                .Any(item => item.gameObject.scene == scene) ||
                UnityEngine.Object
                    .FindObjectsOfType<ExperimentVrLayoutButton>(true)
                    .Any(item => item.gameObject.scene == scene) ||
                UnityEngine.Object
                    .FindObjectsOfType<PicoExperimentLayoutRaySelector>(true)
                    .Any(item => item.gameObject.scene == scene);

            if (generator.GeneratedCount != 0 || registry.Count != 8 ||
                missingScripts != 0 || hasLegacyControls)
            {
                throw new InvalidOperationException(
                    "Fixed High validation failed: " +
                    $"generated={generator.GeneratedCount}, " +
                    $"registry={registry.Count}, missingScripts={missingScripts}, " +
                    $"legacyControls={hasLegacyControls}, " +
                    $"removedMarkers={removedSerializedDistractors}, " +
                    $"removedOrphans={removedOrphanedDistractors}, " +
                    $"ids=[{string.Join(",", registry.Objects.Select(item => item.StableId))}].");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    "Could not save the fixed High AStage scene.");
            }

            reportLines.Add($"Removed control objects: {removedObjects}");
            reportLines.Add(
                $"Removed serialized distractors: {removedSerializedDistractors}");
            reportLines.Add(
                $"Removed orphaned distractors: {removedOrphanedDistractors}");
            reportLines.Add(
                $"Serialized distractors: {generator.GeneratedCount}");
            reportLines.Add($"Registry objects: {registry.Count}");
            reportLines.Add($"Missing scripts: {missingScripts}");
            reportLines.Add("Runtime fixed High bootstrap: enabled");
            reportLines.Add("Scene validation: PASS");

            BuildReport buildReport = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.CleanBuildCache
                });
            BuildSummary summary = buildReport.summary;
            reportLines.Add($"Finished: {DateTime.Now:O}");
            reportLines.Add($"Build result: {summary.result}");
            reportLines.Add($"Build errors: {summary.totalErrors}");
            reportLines.Add($"Build warnings: {summary.totalWarnings}");
            reportLines.Add($"APK: {output}");
            File.WriteAllLines(reportPath, reportLines);

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Fixed High APK build failed. See the build report.");
            }

            Debug.Log(
                $"[AStageFixedHighBuilder] Build succeeded: {output}");
        }
        catch (Exception exception)
        {
            reportLines.Add($"Failed: {DateTime.Now:O}");
            reportLines.Add($"Exception: {exception}");
            File.WriteAllLines(reportPath, reportLines);
            Debug.LogException(exception);
            throw;
        }
    }

    private static int RemoveLegacyLayoutControls(Scene scene)
    {
        var controls = new HashSet<GameObject>();
        foreach (ExperimentVrLayoutSelector selector in UnityEngine.Object
                     .FindObjectsOfType<ExperimentVrLayoutSelector>(true))
        {
            if (selector.gameObject.scene == scene)
            {
                controls.Add(selector.gameObject);
            }
        }
        foreach (PicoExperimentLayoutRaySelector ray in UnityEngine.Object
                     .FindObjectsOfType<PicoExperimentLayoutRaySelector>(true))
        {
            if (ray.gameObject.scene == scene)
            {
                controls.Add(ray.gameObject);
            }
        }
        foreach (ExperimentVrLayoutButton button in UnityEngine.Object
                     .FindObjectsOfType<ExperimentVrLayoutButton>(true))
        {
            if (button.gameObject.scene == scene)
            {
                controls.Add(button.gameObject);
            }
        }

        string[] legacyNames =
        {
            "VR Experiment Scene Selector",
            "PICO Experiment Layout Ray"
        };
        foreach (Transform transform in scene.GetRootGameObjects()
                     .SelectMany(root =>
                         root.GetComponentsInChildren<Transform>(true)))
        {
            if (legacyNames.Contains(transform.name))
            {
                controls.Add(transform.gameObject);
            }
        }

        GameObject[] roots = controls
            .Where(item => item != null &&
                !controls.Any(other => other != null &&
                    other != item && item.transform.IsChildOf(other.transform)))
            .ToArray();
        foreach (GameObject control in roots)
        {
            UnityEngine.Object.DestroyImmediate(control);
        }
        return roots.Length;
    }

    private static int RemoveSerializedDistractors(Scene scene)
    {
        GeneratedDistractorMarker[] markers = UnityEngine.Object
            .FindObjectsOfType<GeneratedDistractorMarker>(true)
            .Where(marker => marker.gameObject.scene == scene)
            .ToArray();
        foreach (GeneratedDistractorMarker marker in markers)
        {
            if (marker != null)
            {
                UnityEngine.Object.DestroyImmediate(marker.gameObject);
            }
        }
        return markers.Length;
    }

    private static int RemoveOrphanedDistractorSemantics(Scene scene)
    {
        SemanticObject[] distractors = UnityEngine.Object
            .FindObjectsOfType<SemanticObject>(true)
            .Where(item => item.gameObject.scene == scene)
            .Where(item => item.StableId.IndexOf(
                "_distractor_",
                StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        foreach (SemanticObject distractor in distractors)
        {
            if (distractor != null)
            {
                UnityEngine.Object.DestroyImmediate(distractor.gameObject);
            }
        }
        return distractors.Length;
    }
}
