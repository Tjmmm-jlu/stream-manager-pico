using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class ExperimentInstanceAudit
{
    // Revision 2: post-cleanup verification.
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath =
        "Temp/ExperimentInstanceAudit.request";
    private const string ReportPath =
        "Temp/ExperimentInstanceAudit.txt";

    private static readonly Regex ApparatusName = new Regex(
        "beaker|flask|dish|dropper|funnel|lamp|cylinder|rack|test[ _-]?tube|" +
        "bottle|pipette|scale|tray|rod|烧杯|烧瓶|蒸发皿|滴管|漏斗|酒精灯|量筒|试管",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [InitializeOnLoadMethod]
    private static void RunRequestedAudit()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath)))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedAudit();
                return;
            }

            Audit();
            File.Delete(Path.GetFullPath(RequestPath));
            Debug.Log($"[InstanceAudit] Report written to {ReportPath}.");
        };
    }

    [MenuItem("Tools/Stream Manager/Audit Experiment Instances")]
    public static void Audit()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        var targetIds = new Dictionary<GameObject, string>();
        if (sequence != null)
        {
            foreach (PreparationSequenceController.Step step in sequence.Steps)
            {
                if (step?.target != null)
                {
                    targetIds[step.target] = step.id;
                }
            }
        }

        var lines = new List<string>
        {
            $"Scene: {ScenePath}",
            $"Sequence targets: {targetIds.Count}",
            string.Empty,
            "ROOTS"
        };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            lines.Add(
                $"ROOT\t{root.name}\tactive={root.activeInHierarchy}\t" +
                $"children={root.transform.childCount}\tprefab={GetPrefabPath(root)}");
        }

        lines.Add(string.Empty);
        lines.Add("APPARATUS_AND_INTERACTABLES");
        IEnumerable<GameObject> allObjects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject);
        foreach (GameObject item in allObjects
                     .Where(item => IsRelevant(item, targetIds))
                     .OrderBy(GetHierarchyPath, StringComparer.OrdinalIgnoreCase))
        {
            SemanticObject semantic = item.GetComponent<SemanticObject>();
            XRGrabInteractable grab = item.GetComponent<XRGrabInteractable>();
            bool isTarget = targetIds.TryGetValue(item, out string targetId);
            bool containsTarget = targetIds.Keys.Any(target =>
                target != item && target.transform.IsChildOf(item.transform));
            bool insideTarget = targetIds.Keys.Any(target =>
                item != target && item.transform.IsChildOf(target.transform));
            GameObject outerRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(item);
            lines.Add(
                $"ITEM\t{GetHierarchyPath(item)}\tactive={item.activeInHierarchy}\t" +
                $"target={isTarget}\ttargetId={targetId ?? string.Empty}\t" +
                $"containsTarget={containsTarget}\tinsideTarget={insideTarget}\t" +
                $"semanticId={semantic?.StableId ?? string.Empty}\t" +
                $"xrGrab={(grab != null)}\tchildren={item.transform.childCount}\t" +
                $"outerPrefabRoot={(outerRoot == item)}\t" +
                $"prefab={GetPrefabPath(item)}");
        }

        File.WriteAllLines(Path.GetFullPath(ReportPath), lines);
    }

    private static bool IsRelevant(
        GameObject item,
        IReadOnlyDictionary<GameObject, string> targets)
    {
        return targets.ContainsKey(item) ||
               item.GetComponent<SemanticObject>() != null ||
               item.GetComponent<XRGrabInteractable>() != null ||
               ApparatusName.IsMatch(item.name);
    }

    private static string GetHierarchyPath(GameObject item)
    {
        var names = new Stack<string>();
        Transform current = item.transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", names);
    }

    private static string GetPrefabPath(GameObject item)
    {
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item);
        return string.IsNullOrWhiteSpace(path) ? "-" : path;
    }
}
