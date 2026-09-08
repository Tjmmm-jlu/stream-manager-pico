using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CopperSulfateTransferInstaller
{
    public const string ScenePath = "Assets/Scenes/ContinuousExperiment_CopperSulfate_AStage.unity";
    private const string RequestPath = "Temp/CopperSulfateTransfer.request";
    private static bool _busy;

    static CopperSulfateTransferInstaller()
    {
        EditorApplication.update += ProcessRequest;
    }

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath))
            return;
        _busy = true;
        try
        {
            string request = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (request == "install") Install();
            else if (request == "verify") CopperSulfateTransferSmokeTest.Run();
            else throw new InvalidOperationException("Unknown transfer request: " + request);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/CopperSulfateTransfer.result.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Install Transfer Step")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before installing.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open the copper sulfate scene first.");

        string backup = "MigrationBackups/BeforeCopperTransfer_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        if (scene.isDirty)
            EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);

        ConfigureScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save the transfer configuration.");
        File.WriteAllText("Temp/CopperSulfateTransfer.result.txt",
            "Installed transfer step. Backup: " + backup);
        Debug.Log("[CopperTransfer] Installed; existing scene and camera setup preserved.");
    }

    public static void ConfigureScene(Scene scene)
    {
        var semantics = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        SemanticObject Find(string id) => semantics.Single(item => item.StableId == id);
        SemanticObject tool = Find("forceps");
        SemanticObject source = Find("copper_sulfate_crude");
        SemanticObject cup = Find("beaker");
        var sequence = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PreparationSequenceController>(true)).Single();
        Transform visual = source.GetComponentsInChildren<Transform>(true)
            .Single(item => item.name == "Copper Sulfate Crystals");

        Bounds toolBounds = LocalMeshBounds(tool.transform);
        Bounds cupBounds = LocalMeshBounds(cup.transform);
        Transform tip = Anchor(tool.transform, "CopperTransferTip",
            new Vector3(toolBounds.min.x, toolBounds.center.y, toolBounds.center.z));
        // The supplied forceps mesh has its two open tips on the negative X end.
        if (Mathf.Abs(tip.localPosition.x - toolBounds.max.x) < 0.0001f)
            tip.localPosition = new Vector3(toolBounds.min.x, toolBounds.center.y, toolBounds.center.z);
        EditorUtility.SetDirty(tip);
        Transform pickup = Anchor(source.transform, "CopperTransferPickup",
            source.transform.InverseTransformPoint(visual.TransformPoint(LocalMeshBounds(visual).center)));
        Transform mouth = Anchor(cup.transform, "CopperTransferMouth",
            new Vector3(cupBounds.center.x, cupBounds.max.y, cupBounds.center.z));
        Transform deposit = Anchor(cup.transform, "CopperTransferDeposit",
            new Vector3(cupBounds.center.x, cupBounds.min.y + cupBounds.size.y * 0.2f,
                cupBounds.center.z));
        float openingRadius = Mathf.Clamp(Mathf.Min(
            cup.transform.TransformVector(Vector3.right * cupBounds.size.x).magnitude,
            cup.transform.TransformVector(Vector3.forward * cupBounds.size.z).magnitude) * 0.4f,
            0.02f, 0.065f);

        var steps = sequence.Steps.ToList();
        int first = steps.FindIndex(step => step.id == "copper_sulfate_crude" ||
            step.completionAction == CopperSulfateTransferDetector.ActionId);
        if (first != 0)
            throw new InvalidOperationException("Expected the reagent or transfer step first.");
        steps[0] = new PreparationSequenceController.Step
        {
            id = "forceps_transfer",
            instruction = "\u4f7f\u7528\u954a\u5b50\u5939\u53d6\u7c97\u786b\u9178\u94dc\uff0c\u5e76\u8f6c\u79fb\u5230\u7a7a\u70e7\u676f\u3002",
            target = tool.gameObject,
            completionAction = CopperSulfateTransferDetector.ActionId
        };
        int cylinderIndex = steps.FindIndex(step => step.id == "graduated_cylinder");
        if (cylinderIndex > 1)
        {
            var cylinder = steps[cylinderIndex];
            steps.RemoveAt(cylinderIndex);
            steps.Insert(1, cylinder);
        }
        sequence.Configure(sequence.GuidanceManager, steps, false);
        var detector = sequence.GetComponent<CopperSulfateTransferDetector>() ??
            sequence.gameObject.AddComponent<CopperSulfateTransferDetector>();
        detector.Configure(sequence, tool, source, cup, tip, pickup, mouth, deposit,
            visual, openingRadius);
        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(detector);
    }

    private static Transform Anchor(Transform parent, string name, Vector3 localPosition)
    {
        Transform anchor = parent.Find(name);
        if (anchor != null) return anchor;
        anchor = new GameObject(name).transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = localPosition;
        return anchor;
    }

    private static Bounds LocalMeshBounds(Transform root)
    {
        bool initialized = false;
        Bounds result = default;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                point = root.InverseTransformPoint(filter.transform.TransformPoint(point));
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        if (!initialized) throw new InvalidOperationException("No mesh bounds: " + root.name);
        return result;
    }
}
