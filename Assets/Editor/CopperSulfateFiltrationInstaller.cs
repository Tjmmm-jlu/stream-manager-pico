using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateFiltrationInstaller
{
    private const string RequestPath = "Temp/CopperSulfateFiltration.request";
    private static bool _busy;
    static CopperSulfateFiltrationInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
            if (command == "audit") Audit();
            else if (command == "install") Install();
            else if (command == "polish") Polish();
            else if (command == "verify")
            {
                CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/CopperSulfateFiltration.result.txt", "Filtration and liquid verification passed.");
            }
            else if (command == "build")
            {
                string path = "Builds/CopperSulfate-SeatedFiltration.apk";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { CopperSulfateLiquidInstaller.ScenePath },
                    locationPathName = path, target = BuildTarget.Android, options = BuildOptions.Development });
                File.WriteAllText("Temp/CopperSulfateFiltration.build.txt", report.summary.result + "\n" + path + "\nErrors: " + report.summary.totalErrors);
            }
            else throw new InvalidOperationException("Unknown filtration command: " + command);
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/CopperSulfateFiltration.result.txt", error.ToString()); Debug.LogException(error);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Install Seated Filtration")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the stationary scene outside Play Mode.");
        var roots = scene.GetRootGameObjects();
        var mix = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Single();
        if (mix.GetComponent<CopperSulfateFiltrationDetector>() != null)
            throw new InvalidOperationException("Filtration is already installed.");
        string backup = "MigrationBackups/BeforeFiltrationInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        var semantics = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        var funnel = semantics.Single(s => s.StableId == "glass_funnel");
        var dish = semantics.Single(s => s.StableId == "china_dish");
        Material glass = AssetDatabase.LoadAssetAtPath<Material>(CopperSulfateLiquidInstaller.AssetFolder + "/VesselGlass.mat");
        Material water = AssetDatabase.LoadAssetAtPath<Material>(CopperSulfateLiquidInstaller.AssetFolder + "/Water.mat");
        Material ink = AssetDatabase.LoadAssetAtPath<Material>(CopperSulfateLiquidInstaller.AssetFolder + "/GraduationInk.mat");
        Material blue = CopperSulfateLiquidInstaller.MaterialAsset("FiltrateBlue", new Color(0.05f, 0.3f, 0.82f, 0.85f), true);
        Material paper = CopperSulfateLiquidInstaller.MaterialAsset("FilterPaper", new Color(0.93f, 0.95f, 0.94f), false);
        var receiving = new GameObject("Filtrate Receiving Beaker");
        receiving.transform.localScale = Vector3.one * 1.15f;
        receiving.AddComponent<MeshFilter>().sharedMesh = mix.Beaker.GetComponent<MeshFilter>().sharedMesh;
        receiving.AddComponent<MeshRenderer>().sharedMaterial = glass;
        var body = receiving.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        receiving.AddComponent<XRGrabInteractable>().throwOnDetach = false;
        var semantic = receiving.AddComponent<SemanticObject>();
        semantic.ConfigureDetailed("filtrate_beaker", "\u63a5\u6536\u70e7\u676f", "beaker", "container", "collecting_filtrate", "colorless",
            "transparent", "cylindrical", new[] { "glass" }, "none", "none", new[] { "empty" },
            new[] { "\u63a5\u6536\u70e7\u676f", "\u6ee4\u6db2\u70e7\u676f", "filtrate beaker", "receiving beaker" }, true, true);
        var receiverLiquid = CopperSulfateLiquidInstaller.Container(semantic, 150f, 0f, false, glass, water, ink);
        Material ceramic = dish.GetComponent<Renderer>().sharedMaterial;
        var dishLiquid = CopperSulfateLiquidInstaller.Container(dish, 150f, 0f, false, ceramic, water, ink);
        funnel.GetComponent<Renderer>().sharedMaterial = glass;
        PrefabUtility.RecordPrefabInstancePropertyModifications(funnel.GetComponent<Renderer>());
        PositionApparatus(scene, receiving.transform, funnel.transform, dish.transform);
        Bounds funnelBounds = funnel.GetComponent<MeshFilter>().sharedMesh.bounds;
        Transform Point(string name, Vector3 position)
        {
            var point = new GameObject(name).transform; point.SetParent(funnel.transform, false); point.localPosition = position; return point;
        }
        float radius = Mathf.Min(funnelBounds.extents.x, funnelBounds.extents.z) * 0.88f;
        var seat = Point("Filter Seat", new Vector3(funnelBounds.center.x, funnelBounds.min.y + funnelBounds.size.y * 0.68f, funnelBounds.center.z));
        var inlet = Point("Filter Inlet", new Vector3(funnelBounds.center.x, funnelBounds.max.y - 0.002f, funnelBounds.center.z));
        var paperObject = Point("Filter Paper", Vector3.zero).gameObject;
        var mesh = CreatePaper(funnelBounds, radius);
        AssetDatabase.CreateAsset(mesh, CopperSulfateLiquidInstaller.AssetFolder + "/FilterPaper.asset");
        paperObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        paperObject.AddComponent<MeshRenderer>().sharedMaterial = paper;
        var captured = Point("Trapped Residue", new Vector3(funnelBounds.center.x, funnelBounds.min.y + funnelBounds.size.y * 0.63f, funnelBounds.center.z));
        Material residueMaterial = AssetDatabase.LoadAssetAtPath<Material>(CopperSulfateLiquidInstaller.AssetFolder + "/Residue.mat");
        for (int i = 0; i < 6; i++)
        {
            var grain = GameObject.CreatePrimitive(PrimitiveType.Sphere); grain.name = "Residue " + i;
            UnityEngine.Object.DestroyImmediate(grain.GetComponent<Collider>()); grain.transform.SetParent(captured, false);
            float angle = i * Mathf.PI / 3f;
            grain.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.009f, 0.002f, Mathf.Sin(angle) * 0.009f);
            grain.transform.localScale = new Vector3(0.005f, 0.004f, 0.006f);
            grain.GetComponent<Renderer>().sharedMaterial = residueMaterial;
        }
        captured.gameObject.SetActive(false);
        var stream = Stream(mix.transform, "Filtration Pour Stream", blue);
        var pouring = stream.gameObject.AddComponent<LiquidPourInteractor>(); pouring.Configure(stream, 18f);
        var outlet = Stream(mix.transform, "Filter Outlet Stream", blue);
        var filter = mix.gameObject.AddComponent<CopperSulfateFiltrationDetector>();
        var sequence = mix.GetComponent<PreparationSequenceController>();
        filter.Configure(sequence, mix, receiverLiquid, dishLiquid, funnel.GetComponent<XRGrabInteractable>(),
            seat, inlet, radius * funnel.transform.lossyScale.x, captured, outlet, pouring);
        var filterStep = sequence.Steps.Single(s => s.id == "glass_funnel");
        filterStep.completionAction = CopperSulfateFiltrationDetector.FilterAction;
        filterStep.instruction = "\u5c06\u5e26\u6ee4\u7eb8\u7684\u6f0f\u6597\u88c5\u5165\u63a5\u6536\u70e7\u676f\uff0c\u53cc\u624b\u6301\u539f\u70e7\u676f\u548c\u63a5\u6536\u7ec4\u4ef6\u8fc7\u6ee4\uff1b\u5b8c\u6210\u540e\u53d6\u4e0b\u6f0f\u6597\u3002";
        var transferStep = sequence.Steps.Single(s => s.id == "china_dish");
        transferStep.completionAction = CopperSulfateFiltrationDetector.TransferAction;
        transferStep.instruction = "\u53cc\u624b\u6301\u63a5\u6536\u70e7\u676f\u548c\u84b8\u53d1\u76bf\uff0c\u5c06\u84dd\u8272\u6ee4\u6db2\u5012\u5165\u84b8\u53d1\u76bf\u3002";
        mix.GetComponent<SceneObjectRegistry>().Refresh(); mix.GetComponent<ExperimentInteractionTracker>().RefreshTrackedObjects();
        EditorUtility.SetDirty(sequence); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save filtration scene.");
        Polish();
        File.WriteAllText("Temp/CopperSulfateFiltration.result.txt", "Installed seated filtration. Backup: " + backup);
    }

    private static LineRenderer Stream(Transform parent, string name, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
        line.positionCount = 2; line.useWorldSpace = true; line.startWidth = line.endWidth = 0.0025f;
        line.numCapVertices = 3; line.shadowCastingMode = ShadowCastingMode.Off; line.enabled = false; return line;
    }

    private static Mesh CreatePaper(Bounds bounds, float radius)
    {
        var vertices = new Vector3[128]; var normals = new Vector3[128]; var triangles = new int[32 * 12];
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI / 16f;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.6f, bounds.center.z) + radial * 0.008f;
            vertices[i * 2 + 1] = new Vector3(bounds.center.x, bounds.max.y - 0.003f, bounds.center.z) + radial * radius;
            vertices[i * 2 + 64] = vertices[i * 2]; vertices[i * 2 + 65] = vertices[i * 2 + 1];
            float height = vertices[i * 2 + 1].y - vertices[i * 2].y;
            Vector3 normal = new Vector3(-radial.x, (radius - 0.008f) / height, -radial.z).normalized;
            normals[i * 2] = normals[i * 2 + 1] = normal;
            normals[i * 2 + 64] = normals[i * 2 + 65] = -normal;
            int a = i * 2, b = (i * 2 + 2) % 64;
            int[] faces = { a, a + 1, b + 1, a, b + 1, b, a + 64, b + 65, a + 65, a + 64, b + 64, b + 65 };
            Array.Copy(faces, 0, triangles, i * 12, 12);
        }
        var mesh = new Mesh { name = "Conical Filter Paper", vertices = vertices, normals = normals, triangles = triangles };
        mesh.RecalculateBounds(); return mesh;
    }

    private static void Polish()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath) throw new InvalidOperationException("Open the stationary scene.");
        var filter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateFiltrationDetector>()).Single();
        PositionApparatus(scene, filter.Receiver.transform, filter.Funnel.transform, filter.Dish.transform);
        var dish = filter.Dish;
        Bounds bounds = dish.GetComponent<MeshFilter>().sharedMesh.bounds;
        float baseY = bounds.min.y + bounds.size.y * 0.23f;
        var visual = dish.transform.Find("Measured Liquid");
        dish.Configure(dish.CapacityMl, 0f, new Vector3(bounds.center.x, baseY, bounds.center.z),
            Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f, bounds.max.y - baseY - 0.002f,
            visual.GetComponent<MeshFilter>(), visual.GetComponent<MeshRenderer>(), 0.28f);
        Bounds funnelBounds = filter.Funnel.GetComponent<MeshFilter>().sharedMesh.bounds;
        float radius = Mathf.Min(funnelBounds.extents.x, funnelBounds.extents.z) * 0.88f;
        var mesh = CreatePaper(funnelBounds, radius);
        var asset = AssetDatabase.LoadAssetAtPath<Mesh>(CopperSulfateLiquidInstaller.AssetFolder + "/FilterPaper.asset");
        EditorUtility.CopySerialized(mesh, asset); EditorUtility.SetDirty(asset); UnityEngine.Object.DestroyImmediate(mesh);
        var paperRenderer = filter.Funnel.transform.Find("Filter Paper").GetComponent<MeshRenderer>();
        paperRenderer.shadowCastingMode = ShadowCastingMode.Off; paperRenderer.receiveShadows = false;
        float paperBottom = funnelBounds.min.y + funnelBounds.size.y * 0.6f;
        float residueY = Mathf.Lerp(paperBottom, funnelBounds.max.y - 0.003f, (0.018f - 0.008f) / (radius - 0.008f));
        filter.TrappedResidue.localPosition = new Vector3(funnelBounds.center.x, residueY + 0.006f, funnelBounds.center.z);
        int index = 0;
        foreach (Transform grain in filter.TrappedResidue)
        {
            float angle = index++ * Mathf.PI / 3f;
            grain.localPosition = new Vector3(Mathf.Cos(angle) * 0.018f, 0f, Mathf.Sin(angle) * 0.018f);
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(CopperSulfateLiquidInstaller.AssetFolder + "/FilterPaper.mat");
        material.shader = Shader.Find("Unlit/Color"); material.color = new Color(0.93f, 0.95f, 0.94f);
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(dish); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save calibrated filtration geometry.");
        File.WriteAllText("Temp/CopperSulfateFiltration.result.txt", "Calibrated shallow dish and filter-paper normals.");
    }

    private static void PositionApparatus(Scene scene, Transform receiving, Transform funnel, Transform dish)
    {
        var table = scene.GetRootGameObjects().Single(r => r.name == "FilteringStation").transform;
        var tabletop = CopperSulfateStationarySceneBuilder.FindTabletop(table);
        Vector3 center = new Vector3(tabletop.bounds.center.x, tabletop.bounds.max.y, tabletop.bounds.center.z);
        void Place(Transform item, float x, float z)
        {
            item.rotation = table.rotation;
            Bounds bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item.gameObject, Vector3.zero, table.rotation);
            Vector3 target = center + table.rotation * new Vector3(x, 0f, z);
            item.position += target + Vector3.up * 0.003f - table.rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
        }
        Place(funnel, -0.235f, -0.18f); Place(receiving, -0.095f, -0.18f); Place(dish, -0.207f, 0.06f);
        var tripod = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Single(s => s.StableId == "tripod_stand");
        Place(tripod.transform, 0.145f, 0f);
    }

    private static void Audit()
    {
        var scene = SceneManager.GetActiveScene();
        var items = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true));
        var lines = items.Where(s => new[] { "glass_funnel", "china_dish", "beaker", "spirit_lamp", "tripod_stand" }.Contains(s.StableId))
            .Select(s => s.StableId + " position=" + s.transform.position.ToString("F4") + " rotation=" + s.transform.eulerAngles +
                " scale=" + s.transform.lossyScale + " meshes=" + string.Join(";", s.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh != null).Select(m => m.sharedMesh.bounds.ToString())) +
                " world=" + string.Join(";", s.GetComponentsInChildren<Renderer>().Select(m => m.bounds.ToString()))).ToList();
        var table = scene.GetRootGameObjects().Single(r => r.name == "FilteringStation");
        lines.Add("table " + CopperSulfateStationarySceneBuilder.FindTabletop(table.transform).bounds);
        var start = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>()).Single();
        lines.Add("start " + start.PreparationStart.position + " " + start.PreparationStart.eulerAngles);
        lines.Add("dirty " + scene.isDirty);
        var dishMesh = items.Single(s => s.StableId == "china_dish").GetComponent<MeshFilter>().sharedMesh;
        for (int band = 0; band < 6; band++)
        {
            var vertices = dishMesh.vertices.Where(v => new Vector2(v.x, v.z).magnitude >= band * 0.01f &&
                new Vector2(v.x, v.z).magnitude < (band + 1) * 0.01f).ToArray();
            if (vertices.Length > 0) lines.Add("dish radius " + band + "cm y=" + vertices.Min(v => v.y).ToString("F5") + ".." + vertices.Max(v => v.y).ToString("F5"));
        }
        File.WriteAllLines("Temp/CopperSulfateFiltration.audit.txt", lines);
    }
}
