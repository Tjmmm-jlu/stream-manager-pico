using System.Collections;
using UnityEngine;

public static class ExperimentFixedHighLayoutBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFixedHighLayout()
    {
        ExperimentDistractorLayoutGenerator generator =
            Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null)
        {
            return;
        }

        var runner = new GameObject("Fixed High Layout Startup");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<ExperimentFixedHighLayoutRunner>();
    }
}

[DisallowMultipleComponent]
public sealed class ExperimentFixedHighLayoutRunner : MonoBehaviour
{
    // The base scene warmup waits 45 frames and restores one renderer per
    // frame. This delay leaves additional headroom before creating High props.
    private const int StableFramesBeforeGeneration = 150;
    private bool _paused;

    private IEnumerator Start()
    {
        bool requiresPicoWarmup =
            Application.platform == RuntimePlatform.Android;
        int requiredStableFrames = requiresPicoWarmup
            ? StableFramesBeforeGeneration
            : 1;
        int stableFrames = 0;
        while (stableFrames < requiredStableFrames)
        {
            if (!requiresPicoWarmup || (!_paused && Application.isFocused))
            {
                stableFrames++;
            }
            else
            {
                stableFrames = 0;
            }
            yield return null;
        }

        ExperimentDistractorLayoutGenerator generator =
            Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null)
        {
            Debug.LogError("[FixedHighLayout] Generator disappeared before startup.");
            Destroy(gameObject);
            yield break;
        }

        generator.ApplySelection(
            ExperimentLayoutSelection.High,
            "fixed_high_startup");
        Debug.Log(
            $"[FixedHighLayout] ready;generated={generator.GeneratedCount}");
        Destroy(gameObject);
    }

    private void OnApplicationPause(bool paused)
    {
        _paused = paused;
    }
}
