using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CodexAuditAStage
{
    private const string TargetScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string SessionKey = "Codex.AuditAStage.20260817.1";

    static CodexAuditAStage()
    {
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(TargetScenePath);
        bool closeAfter = !scene.IsValid() || !scene.isLoaded;
        if (closeAfter)
        {
            scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive);
        }

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[CodexAStageAudit] ERROR scene_not_loaded");
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        GameObject complexRoot = roots
            .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject)
            .FirstOrDefault(item => Normalize(item.name).StartsWith("complexsceneobj"));
        ExperimentDistractorLayoutGenerator generator = roots
            .SelectMany(item => item.GetComponentsInChildren<ExperimentDistractorLayoutGenerator>(true))
            .FirstOrDefault();

        if (complexRoot == null || generator == null || generator.PlacementArea == null)
        {
            Debug.LogError(
                $"[CodexAStageAudit] ERROR complexRoot={complexRoot != null};" +
                $"generator={generator != null};area={generator?.PlacementArea != null}");
            if (closeAfter)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            return;
        }

        Transform area = generator.PlacementArea;
        Vector2 areaSize = generator.AreaSize;
        List<Transform> objects = Enumerable.Range(0, complexRoot.transform.childCount)
            .Select(complexRoot.transform.GetChild)
            .Where(item => TryGetBounds(item.gameObject, out _))
            .ToList();

        var footprints = new Dictionary<Transform, Rect>();
        var offTable = new List<string>();
        var notGrounded = new List<string>();
        foreach (Transform item in objects)
        {
            Bounds bounds;
            TryGetBounds(item.gameObject, out bounds);
            Rect footprint = GetFootprint(item.gameObject, area);
            footprints[item] = footprint;
            if (!IsInside(footprint, areaSize, 0.05f))
            {
                offTable.Add(item.name);
            }
            if (Mathf.Abs(bounds.min.y - area.position.y) > 0.01f)
            {
                notGrounded.Add($"{item.name}({bounds.min.y - area.position.y:+0.000;-0.000}m)");
            }
        }

        var overlaps = new List<string>();
        for (int left = 0; left < objects.Count; left++)
        {
            for (int right = left + 1; right < objects.Count; right++)
            {
                if (Overlaps(footprints[objects[left]], footprints[objects[right]], 0.003f))
                {
                    overlaps.Add(objects[left].name + " <-> " + objects[right].name);
                }
            }
        }

        int missingScripts = roots
            .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
            .Sum(item => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject));
        int activeRenderers = objects
            .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
            .Count(item => item.enabled && item.gameObject.activeInHierarchy);
        int activeCameras = roots
            .SelectMany(item => item.GetComponentsInChildren<Camera>(true))
            .Count(item => item.enabled && item.gameObject.activeInHierarchy);
        int activeAudioListeners = roots
            .SelectMany(item => item.GetComponentsInChildren<AudioListener>(true))
            .Count(item => item.enabled && item.gameObject.activeInHierarchy);

        Debug.Log(
            $"[CodexAStageAudit] COMPLETE objects={objects.Count};renderers={activeRenderers};" +
            $"offTable={offTable.Count};notGrounded={notGrounded.Count};" +
            $"overlaps={overlaps.Count};missingScripts={missingScripts};" +
            $"activeCameras={activeCameras};activeAudioListeners={activeAudioListeners};" +
            $"sceneDirty={scene.isDirty}");
        Debug.Log("[CodexAStageAudit] OBJECTS " + string.Join(", ", objects.Select(item => item.name)));
        if (offTable.Count > 0)
        {
            Debug.LogWarning("[CodexAStageAudit] OFF_TABLE " + string.Join(", ", offTable));
        }
        if (notGrounded.Count > 0)
        {
            Debug.LogWarning("[CodexAStageAudit] NOT_GROUNDED " + string.Join(", ", notGrounded));
        }
        if (overlaps.Count > 0)
        {
            Debug.LogWarning("[CodexAStageAudit] OVERLAPS " + string.Join(", ", overlaps));
        }

        SessionState.SetBool(SessionKey, true);
        if (closeAfter)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool TryGetBounds(GameObject item, out Bounds bounds)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled)
            .ToArray();
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1))
        {
            bounds.Encapsulate(renderer.bounds);
        }
        return true;
    }

    private static Rect GetFootprint(GameObject item, Transform area)
    {
        TryGetBounds(item, out Bounds bounds);
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, bounds.center.y, min.z),
            new Vector3(min.x, bounds.center.y, max.z),
            new Vector3(max.x, bounds.center.y, min.z),
            new Vector3(max.x, bounds.center.y, max.z)
        };
        Vector3[] local = corners.Select(area.InverseTransformPoint).ToArray();
        return Rect.MinMaxRect(
            local.Min(item => item.x), local.Min(item => item.z),
            local.Max(item => item.x), local.Max(item => item.z));
    }

    private static bool IsInside(Rect footprint, Vector2 size, float padding)
    {
        float halfX = size.x * 0.5f - padding;
        float halfZ = size.y * 0.5f - padding;
        return footprint.xMin >= -halfX && footprint.xMax <= halfX &&
               footprint.yMin >= -halfZ && footprint.yMax <= halfZ;
    }

    private static bool Overlaps(Rect left, Rect right, float tolerance)
    {
        return left.xMin < right.xMax - tolerance && left.xMax > right.xMin + tolerance &&
               left.yMin < right.yMax - tolerance && left.yMax > right.yMin + tolerance;
    }
}
