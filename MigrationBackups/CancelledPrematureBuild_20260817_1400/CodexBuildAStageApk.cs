using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class CodexBuildAStageApk
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string SessionKey = "Codex.BuildAStageComplex.20260817.1";

    static CodexBuildAStageApk()
    {
        EditorApplication.delayCall += BuildWhenReady;
    }

    private static void BuildWhenReady()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += BuildWhenReady;
            return;
        }

        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene == null)
        {
            Debug.LogError("[CodexAStageBuild] ERROR scene_not_found=" + ScenePath);
            return;
        }

        SessionState.SetBool(SessionKey, true);
        string outputDirectory = Path.GetFullPath("Builds");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(
            outputDirectory,
            "stream-manager-pico-astage-complex.apk");

        try
        {
            Debug.Log("[CodexAStageBuild] START output=" + outputPath);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[CodexAStageBuild] ERROR result={report.summary.result};" +
                    $"errors={report.summary.totalErrors};" +
                    $"warnings={report.summary.totalWarnings}");
                return;
            }

            Debug.Log(
                $"[CodexAStageBuild] COMPLETE output={outputPath};" +
                $"bytes={report.summary.totalSize};" +
                $"warnings={report.summary.totalWarnings};" +
                $"duration={report.summary.totalTime}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("[CodexAStageBuild] ERROR exception=" + exception.Message);
        }
    }
}
