using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CodexPlaceComplexObjectsOnTable
{
    private const string SessionKey = "Codex.PlaceComplexObjectsOnTable.20260817";
    private const string TargetScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const float EdgePadding = 0.05f;
    private const float ObjectSpacing = 0.025f;

    static CodexPlaceComplexObjectsOnTable()
    {
        EditorApplication.delayCall += RunWhenReady;
    }

    private static void RunWhenReady()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            EditorApplication.delayCall += RunWhenReady;
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(TargetScenePath);
        bool closeAfterPlacement = !scene.IsValid() || !scene.isLoaded;
        if (closeAfterPlacement)
        {
            scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive);
        }

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[CodexPlacement] AStage could not be loaded: " + TargetScenePath);
            return;
        }

        GameObject root = scene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject)
            .FirstOrDefault(item =>
            {
                string normalized = Normalize(item.name);
                return normalized.StartsWith("complexsceneobject") ||
                       normalized.StartsWith("complexsceneobj");
            });
        if (root == null)
        {
            Debug.LogError("[CodexPlacement] Complex Scene Objects was not found in the active scene.");
            return;
        }

        List<Transform> objects = Enumerable.Range(0, root.transform.childCount)
            .Select(root.transform.GetChild)
            .Where(item => TryGetRendererBounds(item.gameObject, out _))
            .ToList();
        if (objects.Count == 0)
        {
            Debug.LogError("[CodexPlacement] The container has no direct children with renderers.");
            return;
        }

        ExperimentDistractorLayoutGenerator generator = scene.GetRootGameObjects()
            .SelectMany(item => item.GetComponentsInChildren<ExperimentDistractorLayoutGenerator>(true))
            .FirstOrDefault();
        Transform area = generator != null ? generator.PlacementArea : null;
        Vector2 areaSize = generator != null ? generator.AreaSize : Vector2.zero;
        if (area == null || areaSize.x <= 0f || areaSize.y <= 0f)
        {
            Debug.LogError("[CodexPlacement] The configured tabletop placement area was not found.");
            return;
        }

        string backupPath = BuildBackupPath(scene.path);
        if (!EditorSceneManager.SaveScene(scene, backupPath, true))
        {
            Debug.LogError("[CodexPlacement] Could not create the scene backup: " + backupPath);
            return;
        }

        List<Rect> occupied = CollectProtectedFootprints(scene, root.transform, area);
        List<Vector2> candidates = BuildCandidatePositions(areaSize);
        var placed = new List<string>();
        var fallback = new List<string>();

        objects = objects
            .OrderByDescending(item => FootprintArea(item.gameObject, area))
            .ToList();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Place complex scene objects on table");

        foreach (Transform item in objects)
        {
            Undo.RecordObject(item, "Place on table");
            bool found = false;
            for (int index = 0; index < candidates.Count; index++)
            {
                Vector2 candidate = candidates[index];
                MoveToTable(item, area, candidate);
                Rect footprint = GetLocalFootprint(item.gameObject, area);
                if (!IsInside(footprint, areaSize) || occupied.Any(other => Overlaps(footprint, other)))
                {
                    continue;
                }

                occupied.Add(Expand(footprint, ObjectSpacing));
                candidates.RemoveAt(index);
                placed.Add(item.name);
                found = true;
                break;
            }

            if (!found)
            {
                Vector2 candidate = candidates.Count > 0 ? candidates[0] : Vector2.zero;
                if (candidates.Count > 0)
                {
                    candidates.RemoveAt(0);
                }

                MoveToTable(item, area, candidate);
                occupied.Add(Expand(GetLocalFootprint(item.gameObject, area), ObjectSpacing));
                fallback.Add(item.name);
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool(SessionKey, true);
        Debug.Log(
            $"[CodexPlacement] COMPLETE scene={scene.path}; parent={root.name}; " +
            $"placed={placed.Count}; fallback={fallback.Count}; backup={backupPath}; " +
            $"objects={string.Join(", ", placed.Concat(fallback))}");
        if (fallback.Count > 0)
        {
            Debug.LogWarning(
                "[CodexPlacement] These objects were put on the tabletop but could not be " +
                "placed without overlap: " + string.Join(", ", fallback));
        }

        if (closeAfterPlacement)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        else
        {
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string BuildBackupPath(string scenePath)
    {
        string directory = Path.GetDirectoryName(scenePath)?.Replace('\\', '/') ?? "Assets/Scenes";
        string name = Path.GetFileNameWithoutExtension(scenePath);
        return $"{directory}/{name}_BeforeComplexPlacement_{DateTime.Now:yyyyMMdd_HHmmss}.unity";
    }

    private static List<Vector2> BuildCandidatePositions(Vector2 size)
    {
        int columns = 9;
        int rows = 5;
        float halfX = Mathf.Max(0.05f, size.x * 0.5f - EdgePadding);
        float halfZ = Mathf.Max(0.05f, size.y * 0.5f - EdgePadding);
        var result = new List<Vector2>();
        for (int row = 0; row < rows; row++)
        {
            float z = Mathf.Lerp(-halfZ, halfZ, rows == 1 ? 0.5f : row / (float)(rows - 1));
            for (int column = 0; column < columns; column++)
            {
                float x = Mathf.Lerp(-halfX, halfX, columns == 1 ? 0.5f : column / (float)(columns - 1));
                result.Add(new Vector2(x, z));
            }
        }

        return result
            .OrderByDescending(item => Mathf.Abs(item.y))
            .ThenByDescending(item => Mathf.Abs(item.x))
            .ToList();
    }

    private static List<Rect> CollectProtectedFootprints(
        Scene scene,
        Transform complexRoot,
        Transform area)
    {
        var footprints = new List<Rect>();
        foreach (SemanticObject semantic in UnityEngine.Object.FindObjectsOfType<SemanticObject>(true))
        {
            if (semantic == null || semantic.gameObject.scene != scene ||
                semantic.transform.IsChildOf(complexRoot))
            {
                continue;
            }

            if (TryGetRendererBounds(semantic.gameObject, out _))
            {
                footprints.Add(Expand(GetLocalFootprint(semantic.gameObject, area), ObjectSpacing));
            }
        }

        return footprints;
    }

    private static float FootprintArea(GameObject item, Transform area)
    {
        Rect footprint = GetLocalFootprint(item, area);
        return footprint.width * footprint.height;
    }

    private static void MoveToTable(Transform item, Transform area, Vector2 localPosition)
    {
        Vector3 target = area.TransformPoint(localPosition.x, 0f, localPosition.y);
        if (!TryGetRendererBounds(item.gameObject, out Bounds bounds))
        {
            return;
        }

        Vector3 delta = target - bounds.center;
        delta.y = area.position.y - bounds.min.y + 0.002f;
        item.position += delta;
    }

    private static Rect GetLocalFootprint(GameObject item, Transform area)
    {
        if (!TryGetRendererBounds(item, out Bounds bounds))
        {
            return new Rect();
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, bounds.center.y, min.z),
            new Vector3(min.x, bounds.center.y, max.z),
            new Vector3(max.x, bounds.center.y, min.z),
            new Vector3(max.x, bounds.center.y, max.z)
        };
        Vector3 first = area.InverseTransformPoint(corners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minZ = first.z;
        float maxZ = first.z;
        foreach (Vector3 corner in corners.Skip(1))
        {
            Vector3 local = area.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, local.x);
            maxX = Mathf.Max(maxX, local.x);
            minZ = Mathf.Min(minZ, local.z);
            maxZ = Mathf.Max(maxZ, local.z);
        }

        return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
    }

    private static bool TryGetRendererBounds(GameObject item, out Bounds bounds)
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

    private static bool IsInside(Rect footprint, Vector2 areaSize)
    {
        float halfX = areaSize.x * 0.5f - EdgePadding;
        float halfZ = areaSize.y * 0.5f - EdgePadding;
        return footprint.xMin >= -halfX && footprint.xMax <= halfX &&
               footprint.yMin >= -halfZ && footprint.yMax <= halfZ;
    }

    private static bool Overlaps(Rect left, Rect right)
    {
        return left.xMin < right.xMax && left.xMax > right.xMin &&
               left.yMin < right.yMax && left.yMax > right.yMin;
    }

    private static Rect Expand(Rect value, float amount)
    {
        return Rect.MinMaxRect(
            value.xMin - amount,
            value.yMin - amount,
            value.xMax + amount,
            value.yMax + amount);
    }
}
