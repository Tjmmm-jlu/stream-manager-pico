using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Performs a full Android rebuild for the A-stage PICO scene.
/// This deliberately bypasses the Build Settings "Scripts Only Build" state.
/// </summary>
public static class AStagePicoCleanBuilder
{
    // Recovery revision 8: supports staged scene reconstruction validation.
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string OutputPath = "Builds/stream-manager-pico-astage-clean.apk";
    private const string RequestPath = "Temp/AStagePicoCleanBuilder.request";
    private const string ReportPath = "Temp/AStagePicoCleanBuilder.txt";
    private const string OriginalScenePath = "Assets/Scenes/Preparation Experiment Scene.unity";
    private const string OriginalOutputPath = "Builds/stream-manager-pico-original-diagnostic.apk";
    private const string OriginalRequestPath = "Temp/AStagePicoOriginalDiagnosticBuilder.request";
    private const string OriginalReportPath = "Temp/AStagePicoOriginalDiagnosticBuilder.txt";
    private const string FinalOutputPath = "Builds/stream-manager-pico-astage-fixed.apk";
    private const string FinalRequestPath = "Temp/AStagePicoFullBuilder.request";
    private const string FinalReportPath = "Temp/AStagePicoFullBuilder.txt";
    private const string InfrastructureRequestPath = "Temp/AStageInfrastructureRecovery.request";
    private const string InfrastructureReportPath = "Temp/AStageInfrastructureRecovery.txt";
    private const string UiRequestPath = "Temp/AStageUiRecovery.request";
    private const string UiReportPath = "Temp/AStageUiRecovery.txt";

    [InitializeOnLoadMethod]
    private static void RunRequestedBuild()
    {
        string request = Path.GetFullPath(RequestPath);
        if (!File.Exists(request)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedBuild();
                return;
            }

            File.Delete(request);
            BuildCleanApk();
        };
    }

    [InitializeOnLoadMethod]
    private static void RunRequestedOriginalDiagnosticBuild()
    {
        string request = Path.GetFullPath(OriginalRequestPath);
        if (!File.Exists(request)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedOriginalDiagnosticBuild();
                return;
            }

            File.Delete(request);
            BuildOriginalDiagnosticApk();
        };
    }

    [InitializeOnLoadMethod]
    private static void RunRequestedFinalBuild()
    {
        string request = Path.GetFullPath(FinalRequestPath);
        if (!File.Exists(request)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedFinalBuild();
                return;
            }

            File.Delete(request);
            BuildFinalAStageApk();
        };
    }

    [InitializeOnLoadMethod]
    private static void RunRequestedInfrastructureRecovery()
    {
        string request = Path.GetFullPath(InfrastructureRequestPath);
        if (!File.Exists(request)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedInfrastructureRecovery();
                return;
            }

            File.Delete(request);
            ExperimentDuplicateInstanceCleanup.Cleanup();
            UncontrolledApparatusCleanup.Cleanup();
            ExperimentDistractorLayoutInstaller.InstallOrRefresh();
            File.WriteAllLines(Path.GetFullPath(InfrastructureReportPath), new[]
            {
                $"Finished: {DateTime.Now:O}",
                "Duplicate instance cleanup: complete",
                "Uncontrolled apparatus cleanup: complete",
                "Distractor layout generator: installed",
                "Expected clean registry: 8",
                "Expected generated distractors: 0"
            });
        };
    }

    [InitializeOnLoadMethod]
    private static void RunRequestedUiRecovery()
    {
        string request = Path.GetFullPath(UiRequestPath);
        if (!File.Exists(request)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedUiRecovery();
                return;
            }

            File.Delete(request);
            PicoOfficialHandPrefabInstaller.Install();
            ExperimentVrLayoutSelectorInstaller.Install();
            PicoExperimentLayoutRayInstaller.Install();
            File.WriteAllLines(Path.GetFullPath(UiReportPath), new[]
            {
                $"Finished: {DateTime.Now:O}",
                "PICO official HandLeft/HandRight: installed",
                "VR layout selector: installed",
                "PICO right-hand layout ray: installed"
            });
        };
    }

    [MenuItem("Tools/Stream Manager/Build/Clean PICO AStage APK")]
    public static void BuildCleanApk()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
        string absoluteOutput = Path.Combine(projectRoot, OutputPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)
            ?? throw new InvalidOperationException("Unable to resolve the APK output directory."));

        if (!File.Exists(Path.Combine(projectRoot, ScenePath.Replace('/', Path.DirectorySeparatorChar))))
            throw new FileNotFoundException("A-stage build scene is missing.", ScenePath);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = absoluteOutput,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // CleanBuildCache forces Unity to rebuild level0/data.unity3d instead of
            // reusing the corrupted PlayerDataCache produced by a scripts-only build.
            options = BuildOptions.CleanBuildCache
        };

        DateTime started = DateTime.Now;
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        string[] lines =
        {
            $"Started: {started:O}",
            $"Finished: {DateTime.Now:O}",
            $"Result: {summary.result}",
            $"Output: {absoluteOutput}",
            $"Scene: {ScenePath}",
            $"Total size: {summary.totalSize}",
            $"Errors: {summary.totalErrors}",
            $"Warnings: {summary.totalWarnings}",
            "Build mode: full player data rebuild",
            "Build options: CleanBuildCache (Scripts Only disabled)"
        };
        File.WriteAllLines(Path.Combine(projectRoot, ReportPath.Replace('/', Path.DirectorySeparatorChar)), lines);

        if (summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Clean PICO build failed. See the Unity Console and build report.");

        Debug.Log($"[AStagePicoCleanBuilder] Clean APK built successfully: {absoluteOutput}");
    }

    [MenuItem("Tools/Stream Manager/Build/Diagnostic Original Scene APK")]
    public static void BuildOriginalDiagnosticApk()
    {
        BuildSceneApk(
            OriginalScenePath,
            OriginalOutputPath,
            OriginalReportPath,
            BuildOptions.None,
            "full player rebuild for original-scene comparison");
    }

    [MenuItem("Tools/Stream Manager/Build/Full PICO AStage Fixed APK")]
    public static void BuildFinalAStageApk()
    {
        BuildSceneApk(
            ScenePath,
            FinalOutputPath,
            FinalReportPath,
            BuildOptions.None,
            "full A-stage player rebuild (Scripts Only disabled)");
    }

    private static void BuildSceneApk(
        string scenePath,
        string outputPath,
        string reportPath,
        BuildOptions buildOptions,
        string buildMode)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
        string absoluteOutput = Path.Combine(projectRoot, outputPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)
            ?? throw new InvalidOperationException("Unable to resolve the APK output directory."));

        DateTime started = DateTime.Now;
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = absoluteOutput,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = buildOptions
        });
        BuildSummary summary = report.summary;
        File.WriteAllLines(Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar)), new[]
        {
            $"Started: {started:O}",
            $"Finished: {DateTime.Now:O}",
            $"Result: {summary.result}",
            $"Output: {absoluteOutput}",
            $"Scene: {scenePath}",
            $"Total size: {summary.totalSize}",
            $"Errors: {summary.totalErrors}",
            $"Warnings: {summary.totalWarnings}",
            $"Build mode: {buildMode}",
            $"Build options: {buildOptions}"
        });

        if (summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Android build failed for scene: {scenePath}");
    }
}
