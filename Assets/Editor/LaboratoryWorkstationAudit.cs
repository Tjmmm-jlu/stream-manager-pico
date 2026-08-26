using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LaboratoryWorkstationAudit
{
    private const string SourceScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    private const string OutputPath =
        "Library/PreparationWorkstationAudit.tsv";

    private static double _runAt;

    static LaboratoryWorkstationAudit()
    {
        if (!Application.isBatchMode)
        {
            _runAt = EditorApplication.timeSinceStartup + 1d;
            EditorApplication.update += RunWhenReady;
        }
    }

    [MenuItem("PICO/Audit Laboratory Workstation")]
    public static void Run()
    {
        if (UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().path != SourceScenePath)
        {
            return;
        }

        GameObject rig = GameObject.Find("PICO XR Origin");
        Vector3 origin =
            rig != null ? rig.transform.position : new Vector3(5.52f, 0f, 6.37f);
        var rows = new List<string>
        {
            "distance\tcenterX\tcenterY\tcenterZ\tsizeX\tsizeY\tsizeZ\tpath"
        };

        IEnumerable<Renderer> renderers =
            UnityEngine.Object.FindObjectsOfType<Renderer>(true)
                .Where(renderer =>
                    renderer != null &&
                    !renderer.transform.IsChildOf(rig.transform) &&
                    renderer.name != "streamcamera")
                .OrderBy(renderer => HorizontalDistance(
                    renderer.bounds.center, origin));

        foreach (Renderer renderer in renderers)
        {
            float distance =
                HorizontalDistance(renderer.bounds.center, origin);
            string lowerPath = GetPath(renderer.transform).ToLowerInvariant();
            bool relevantName =
                lowerPath.Contains("table") ||
                lowerPath.Contains("bench") ||
                lowerPath.Contains("desk") ||
                lowerPath.Contains("shelf") ||
                lowerPath.Contains("flask") ||
                lowerPath.Contains("beaker") ||
                lowerPath.Contains("bottle") ||
                lowerPath.Contains("cylinder") ||
                lowerPath.Contains("pipette") ||
                lowerPath.Contains("apparatus");
            if (distance > 6f && !relevantName)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            rows.Add(
                $"{distance:F3}\t{bounds.center.x:F3}\t{bounds.center.y:F3}\t" +
                $"{bounds.center.z:F3}\t{bounds.size.x:F3}\t" +
                $"{bounds.size.y:F3}\t{bounds.size.z:F3}\t" +
                GetPath(renderer.transform));
        }

        File.WriteAllLines(OutputPath, rows);
        Debug.Log(
            $"[Preparation Audit] origin={origin} rows={rows.Count - 1} " +
            $"output={Path.GetFullPath(OutputPath)}");
    }

    private static void RunWhenReady()
    {
        if (EditorApplication.timeSinceStartup < _runAt ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        EditorApplication.update -= RunWhenReady;
        Run();
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(
            new Vector2(a.x, a.z),
            new Vector2(b.x, b.z));
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
