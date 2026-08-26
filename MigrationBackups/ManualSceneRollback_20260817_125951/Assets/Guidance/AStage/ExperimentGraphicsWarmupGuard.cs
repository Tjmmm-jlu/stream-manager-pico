using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentGraphicsWarmupBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartGuard()
    {
        // The GLES crash workaround is specific to the Android PICO player.
        // Running it in the Editor blanks the Game view whenever it loses focus.
        if (Application.platform != RuntimePlatform.Android)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() ||
            Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true) == null)
        {
            return;
        }

        var runner = new GameObject("Experiment Graphics Warmup Guard");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<ExperimentGraphicsWarmupGuard>();
    }
}

[DisallowMultipleComponent]
public sealed class ExperimentGraphicsWarmupGuard : MonoBehaviour
{
    // Re-arm after every XR lifecycle pause/resume, not only after scene load.
    private const int StableFramesBeforeRendering = 45;
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private Coroutine _warmup;
    private bool _applicationPaused;
    private string _warmupReason = "scene-load";

    private void Awake()
    {
        SuspendEnabledRenderers("scene-load");
    }

    private void Start()
    {
        TryStartWarmup("scene-load");
    }

    private void LateUpdate()
    {
        // A scene script may toggle a renderer while OpenXR is paused or its
        // GLES swapchain is settling. Keep captured renderers off until this
        // guard restores them deliberately.
        foreach (Renderer renderer in _renderers)
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
            }
        }
    }

    private void OnApplicationPause(bool paused)
    {
        _applicationPaused = paused;
        if (paused)
        {
            SuspendEnabledRenderers("pause");
            StopWarmup();
            return;
        }

        TryStartWarmup("resume");
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            SuspendEnabledRenderers("focus-lost");
            StopWarmup();
            return;
        }

        TryStartWarmup("focus-gained");
    }

    private void SuspendEnabledRenderers(string reason)
    {
        int added = 0;
        Renderer[] candidates = Object.FindObjectsOfType<Renderer>(true);
        foreach (Renderer renderer in candidates)
        {
            if (renderer == null || !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                _renderers.Contains(renderer))
            {
                continue;
            }

            renderer.enabled = false;
            _renderers.Add(renderer);
            added++;
        }

        _renderers.RemoveAll(renderer => renderer == null);
        _renderers.Sort((left, right) =>
            string.CompareOrdinal(PathOf(left.transform), PathOf(right.transform)));
        Debug.Log(
            $"[GraphicsWarmup] suspended={_renderers.Count};" +
            $"added={added};reason={reason}");
    }

    private void TryStartWarmup(string reason)
    {
        if (_applicationPaused || !Application.isFocused || _renderers.Count == 0)
        {
            return;
        }

        StopWarmup();
        _warmupReason = reason;
        _warmup = StartCoroutine(RestoreRenderers());
    }

    private void StopWarmup()
    {
        if (_warmup == null)
        {
            return;
        }

        StopCoroutine(_warmup);
        _warmup = null;
    }

    private IEnumerator RestoreRenderers()
    {
        Debug.Log(
            $"[GraphicsWarmup] waiting={StableFramesBeforeRendering};" +
            $"reason={_warmupReason}");
        for (int frame = 0; frame < StableFramesBeforeRendering; frame++)
        {
            if (_applicationPaused || !Application.isFocused)
            {
                _warmup = null;
                yield break;
            }
            yield return null;
        }

        int total = _renderers.Count;
        int restored = 0;
        while (_renderers.Count > 0)
        {
            if (_applicationPaused || !Application.isFocused)
            {
                _warmup = null;
                yield break;
            }

            Renderer renderer = _renderers[0];
            _renderers.RemoveAt(0);
            if (renderer != null)
            {
                restored++;
                Debug.Log(
                    $"[GraphicsWarmup] enable={restored}/{total};" +
                    $"path={PathOf(renderer.transform)};type={renderer.GetType().Name}");
                renderer.enabled = true;
            }
            yield return null;
        }

        _warmup = null;
        Debug.Log($"[GraphicsWarmup] ready={restored};reason={_warmupReason}");
    }

    private static string PathOf(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null)
        {
            names.Push(transform.name);
            transform = transform.parent;
        }
        return string.Join("/", names);
    }
}
