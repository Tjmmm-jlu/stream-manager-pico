using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentDistractorLayoutSmokeTest
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath =
        "Temp/ExperimentDistractorLayoutSmokeTest.request";
    private const string ReportPath =
        "Temp/ExperimentDistractorLayoutSmokeTest.txt";

    [InitializeOnLoadMethod]
    private static void RunRequestedTest()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath)))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedTest();
                return;
            }

            Run();
            File.Delete(Path.GetFullPath(RequestPath));
        };
    }

    [MenuItem("Tools/Stream Manager/Distractors/Run Smoke Test")]
    public static void Run()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ExperimentDistractorLayoutGenerator generator =
            UnityEngine.Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        SceneObjectRegistry registry =
            UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        if (generator == null || registry == null)
        {
            throw new InvalidOperationException("Generator or registry is missing.");
        }

        generator.Generate(ExperimentLayoutComplexity.Low, generator.RandomSeed);
        registry.Refresh();
        int generated = generator.GeneratedCount;
        int generatedSemanticCount = registry.Count;
        string[] ids = registry.Objects.Select(item => item.StableId).ToArray();
        int uniqueIdCount = ids.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int colliderCount = UnityEngine.Object
            .FindObjectsOfType<GeneratedDistractorMarker>(true)
            .Count(marker =>
                marker.GetComponentInChildren<Collider>(true) != null);
        const int expectedLowCount = 4;
        const int expectedRegistryCount = 8 + expectedLowCount;
        if (generated != expectedLowCount ||
            generatedSemanticCount != expectedRegistryCount ||
            uniqueIdCount != expectedRegistryCount ||
            colliderCount != expectedLowCount)
        {
            generator.ClearGenerated();
            throw new InvalidOperationException(
                $"Low-density test failed: generated={generated}, " +
                $"registry={generatedSemanticCount}, uniqueIds={uniqueIdCount}, " +
                $"colliders={colliderCount}.");
        }

        generator.ClearGenerated();
        registry.Refresh();
        int finalGenerated = generator.GeneratedCount;
        int finalRegistry = registry.Count;
        if (finalGenerated != 0 || finalRegistry != 8)
        {
            throw new InvalidOperationException(
                $"Cleanup test failed: generated={finalGenerated}, registry={finalRegistry}.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllLines(
            Path.GetFullPath(ReportPath),
            new[]
            {
                "Low-density generation: PASS",
                $"Generated during test: {generated}",
                $"Registry during test: {generatedSemanticCount}",
                $"Unique IDs during test: {uniqueIdCount}",
                $"Generated objects with colliders: {colliderCount}",
                "Clear generated: PASS",
                $"Final generated distractors: {finalGenerated}",
                $"Final semantic registry count: {finalRegistry}"
            });
        Debug.Log("[DistractorLayoutSmokeTest] PASS. Clean base restored.");
    }
}
