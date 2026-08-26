using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CodexPlayAuditAStage
{
    private const string TargetScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string ScreenshotPath =
        "D:/unity project/stream manager pico/Temp/AStage_PC_Audit.png";
    private const string StageKey = "Codex.PlayAuditAStage.Stage.20260817.1";
    private const string PreviousSceneKey = "Codex.PlayAuditAStage.PreviousScene.20260817.1";
    private const string StartedAtKey = "Codex.PlayAuditAStage.StartedAt.20260817.1";
    private const string CaptureAtKey = "Codex.PlayAuditAStage.CaptureAt.20260817.1";

    static CodexPlayAuditAStage()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling)
        {
            return;
        }

        int stage = SessionState.GetInt(StageKey, 0);
        switch (stage)
        {
            case 0:
                StartAudit();
                break;
            case 1:
                WaitForPlayMode();
                break;
            case 2:
                WaitForScreenshot();
                break;
            case 3:
                RestoreEditorScene();
                break;
            default:
                EditorApplication.update -= Tick;
                break;
        }
    }

    private static void StartAudit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene current = SceneManager.GetActiveScene();
        if (current.IsValid() && current.isDirty)
        {
            Debug.LogError("[CodexAStagePlayAudit] ERROR current_scene_has_unsaved_changes");
            SessionState.SetInt(StageKey, 4);
            return;
        }

        SessionState.SetString(PreviousSceneKey, current.IsValid() ? current.path : string.Empty);
        if (!string.Equals(current.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
        {
            EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
        }

        if (File.Exists(ScreenshotPath))
        {
            File.Delete(ScreenshotPath);
        }

        SessionState.SetFloat(StartedAtKey, (float)EditorApplication.timeSinceStartup);
        SessionState.SetInt(StageKey, 1);
        EditorApplication.isPlaying = true;
    }

    private static void WaitForPlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        float startedAt = SessionState.GetFloat(StartedAtKey, 0f);
        if (EditorApplication.timeSinceStartup - startedAt < 5d)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject complexRoot = scene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject)
            .FirstOrDefault(item => Normalize(item.name).StartsWith("complexsceneobj"));
        int activeComplexChildren = complexRoot == null ? 0 :
            Enumerable.Range(0, complexRoot.transform.childCount)
                .Select(complexRoot.transform.GetChild)
                .Count(item => item.gameObject.activeInHierarchy);
        int activeComplexRenderers = complexRoot == null ? 0 :
            complexRoot.GetComponentsInChildren<Renderer>(true)
                .Count(item => item.enabled && item.gameObject.activeInHierarchy);
        int activeSceneRenderers = scene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
            .Count(item => item.enabled && item.gameObject.activeInHierarchy);
        int activeCameras = scene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<Camera>(true))
            .Count(item => item.enabled && item.gameObject.activeInHierarchy);

        Debug.Log(
            $"[CodexAStagePlayAudit] RUNTIME scene={scene.path};frames={Time.frameCount};" +
            $"complexChildren={activeComplexChildren};complexRenderers={activeComplexRenderers};" +
            $"sceneRenderers={activeSceneRenderers};activeCameras={activeCameras};" +
            $"mainCamera={Camera.main != null}");

        ScreenCapture.CaptureScreenshot(ScreenshotPath, 1);
        SessionState.SetFloat(CaptureAtKey, (float)EditorApplication.timeSinceStartup);
        SessionState.SetInt(StageKey, 2);
    }

    private static void WaitForScreenshot()
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetInt(StageKey, 3);
            return;
        }

        float captureAt = SessionState.GetFloat(CaptureAtKey, 0f);
        bool screenshotReady = File.Exists(ScreenshotPath) &&
                               new FileInfo(ScreenshotPath).Length > 0;
        if (!screenshotReady && EditorApplication.timeSinceStartup - captureAt < 5d)
        {
            return;
        }

        SessionState.SetInt(StageKey, 3);
        EditorApplication.isPlaying = false;
    }

    private static void RestoreEditorScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        string previousScene = SessionState.GetString(PreviousSceneKey, string.Empty);
        if (!string.IsNullOrEmpty(previousScene) &&
            !string.Equals(previousScene, TargetScenePath, StringComparison.OrdinalIgnoreCase))
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        long screenshotBytes = File.Exists(ScreenshotPath)
            ? new FileInfo(ScreenshotPath).Length
            : 0L;
        Debug.Log(
            $"[CodexAStagePlayAudit] COMPLETE screenshot={ScreenshotPath};" +
            $"bytes={screenshotBytes};restoredScene={previousScene}");
        SessionState.SetInt(StageKey, 4);
        EditorApplication.update -= Tick;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
