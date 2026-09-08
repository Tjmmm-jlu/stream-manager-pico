using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateLiquidInstaller
{
    public const string ScenePath = CopperSulfateStationarySceneBuilder.ScenePath;
    public const string AssetFolder = "Assets/Guidance/AStage/LiquidExperiment";
    private const string RequestPath = "Temp/CopperSulfateLiquids.request";
    private static bool _busy;
    static CopperSulfateLiquidInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command == "install") Install();
            else if (command == "verify")
            {
                CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/CopperSulfateLiquids.result.txt", "Liquid verification passed. See CopperSulfateLiquids.verification.txt.");
            }
            else if (command == "polish-rod") PolishRod();
            else if (command == "tolerance10") ApplyTolerance10();
            else if (command == "build") Build();
            else throw new InvalidOperationException("Unknown liquid command: " + command);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/CopperSulfateLiquids.result.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Install Seated Liquid Steps")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the stationary scene outside Play Mode first.");
        string backup = "MigrationBackups/BeforeCopperLiquidInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Any())
            throw new InvalidOperationException("Liquid steps are already installed; preserve the existing configuration.");
        Directory.CreateDirectory(AssetFolder);
        AssetDatabase.Refresh();
        Configure(scene);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save the seated liquid scene.");
        File.WriteAllText("Temp/CopperSulfateLiquids.result.txt", "Installed. Backup: " + backup);
        Debug.Log("[CopperLiquids] Installed seated, two-handed measurement and dissolution.");
    }

    private static void Configure(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var semantics = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        SemanticObject Find(string id) => semantics.Single(s => s.StableId == id);
        var sequence = roots.SelectMany(r => r.GetComponentsInChildren<PreparationSequenceController>(true)).Single();
        var transfer = sequence.GetComponent<CopperSulfateTransferDetector>();
        var stations = new[] { "PreparationStation", "MixingStation", "FilteringStation" }
            .Select(name => roots.Single(r => r.name == name).transform).ToArray();
        float floor = roots.Single(r => r.name == "floor (1)").GetComponent<Renderer>().bounds.max.y;
        float oldTop = CopperSulfateStationarySceneBuilder.FindTabletop(stations[0]).bounds.max.y;
        float newTop = floor + 0.74f;
        foreach (var station in stations)
        {
            float top = CopperSulfateStationarySceneBuilder.FindTabletop(station).bounds.max.y;
            float ratio = 0.74f / (top - floor);
            Vector3 scale = station.localScale; scale.y *= ratio; station.localScale = scale;
            Vector3 position = station.position; position.y = floor + (position.y - floor) * ratio; station.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(station);
        }
        foreach (var semantic in semantics.Where(s => CopperSulfateStationarySceneBuilder.RequiredIds.Contains(s.StableId)))
        {
            semantic.transform.position += Vector3.up * (newTop - oldTop);
            PrefabUtility.RecordPrefabInstancePropertyModifications(semantic.transform);
        }

        Material glass = MaterialAsset("VesselGlass", new Color(0.72f, 0.87f, 0.9f, 0.18f), true);
        Material liquid = MaterialAsset("Water", new Color(0.3f, 0.73f, 0.83f, 0.65f), true);
        Material ink = MaterialAsset("GraduationInk", new Color(0.035f, 0.085f, 0.1f), false);
        LiquidContainer cylinder = Container(Find("graduated_cylinder"), 150f, 0f, true, glass, liquid, ink);
        LiquidContainer source = Container(Find("water_source"), 500f, 400f, false, glass, liquid, ink);
        LiquidContainer beaker = Container(Find("beaker"), 250f, 0f, false, glass, liquid, ink);
        var rod = CreateRod(new Vector3(4.25f, newTop + 0.006f, 7.286f));
        var residue = CreateResidue(beaker);
        var streamObject = new GameObject("Water Stream");
        streamObject.transform.SetParent(sequence.transform, false);
        var stream = streamObject.AddComponent<LineRenderer>();
        stream.sharedMaterial = liquid; stream.positionCount = 2; stream.useWorldSpace = true;
        stream.startWidth = stream.endWidth = 0.003f;
        stream.numCapVertices = 3; stream.enabled = false;
        stream.shadowCastingMode = ShadowCastingMode.Off;
        var pour = sequence.gameObject.AddComponent<LiquidPourInteractor>();
        pour.Configure(stream);
        var mixing = sequence.gameObject.AddComponent<CopperSulfateMixingDetector>();
        mixing.Configure(sequence, source, cylinder, beaker, pour, rod,
            rod.transform.Find("StirringTip"), transfer.CrystalVisual, residue);
        var steps = sequence.Steps.Where(s => s.id != "graduated_cylinder" && s.id != "water_source" && s.id != "beaker").ToList();
        steps.Insert(1, new PreparationSequenceController.Step { id = "graduated_cylinder", target = cylinder.gameObject,
            instruction = "\u53cc\u624b\u5206\u522b\u6301\u91cf\u7b52\u548c\u6c34\u6e90\u5bb9\u5668\uff0c\u91cf\u53d6 100 mL \u6e05\u6c34\uff08\u5141\u8bb8\u8bef\u5dee 10 mL\uff09\u3002",
            completionAction = CopperSulfateMixingDetector.MeasureAction });
        steps.Insert(2, new PreparationSequenceController.Step { id = "beaker", target = beaker.gameObject,
            instruction = "\u53cc\u624b\u6301\u91cf\u7b52\u548c\u70e7\u676f\uff0c\u52a0\u5165 100 mL \u6c34\uff1b\u968f\u540e\u6301\u70e7\u676f\u548c\u73bb\u7483\u68d2\u6405\u62cc\u6eb6\u89e3\u3002",
            completionAction = CopperSulfateMixingDetector.MixAction });
        sequence.Configure(sequence.GuidanceManager, steps, false);
        sequence.GetComponent<SceneObjectRegistry>().Refresh();
        sequence.GetComponent<ExperimentInteractionTracker>().RefreshTrackedObjects();
        EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(mixing);
    }

    internal static LiquidContainer Container(SemanticObject semantic, float capacity, float initial,
        bool graduated, Material glass, Material water, Material ink)
    {
        Transform root = semantic.transform;
        var mesh = root.GetComponent<MeshFilter>();
        if (mesh == null) throw new InvalidOperationException("Missing vessel mesh: " + root.name);
        Bounds bounds = mesh.sharedMesh.bounds;
        // The measuring cylinder's broad foot is excluded from its internal radius.
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * (graduated ? 0.56f : 0.82f);
        float bottomY = bounds.min.y + bounds.size.y * (graduated ? 0.1f : 0.025f);
        float height = bounds.max.y - bottomY - 0.002f / root.lossyScale.y;
        Vector3 bottom = new Vector3(bounds.center.x, bottomY, bounds.center.z);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.transform == root) renderer.sharedMaterial = glass;
            else if (renderer.name.IndexOf("liquid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     graduated && renderer.name.StartsWith("Glass_Lab_test_tube", StringComparison.Ordinal))
                renderer.gameObject.SetActive(false);
        }
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        var colliderRoot = new GameObject("Liquid Vessel Colliders").transform;
        colliderRoot.SetParent(root, false);
        float thickness = 0.003f / root.lossyScale.x;
        var baseCollider = colliderRoot.gameObject.AddComponent<BoxCollider>();
        baseCollider.center = bottom - Vector3.up * thickness * 0.5f;
        baseCollider.size = new Vector3(radius * 2f, thickness, radius * 2f);
        for (int i = 0; i < 16; i++)
        {
            var wall = new GameObject("Wall " + i).transform;
            wall.SetParent(colliderRoot, false);
            float angle = i * 360f / 16f;
            wall.localRotation = Quaternion.Euler(0f, angle, 0f);
            wall.localPosition = bottom + Vector3.up * height * 0.5f + wall.localRotation * Vector3.forward * (radius + thickness * 0.5f);
            wall.gameObject.AddComponent<BoxCollider>().size = new Vector3(radius * 2f * Mathf.Tan(Mathf.PI / 16f) + thickness,
                height, thickness);
        }
        var grab = root.GetComponent<XRGrabInteractable>();
        grab.colliders.Clear(); grab.colliders.AddRange(colliderRoot.GetComponentsInChildren<Collider>());
        var grip = new GameObject("LiquidGrip").transform; grip.SetParent(root, false);
        grip.localPosition = bottom + Vector3.up * height * 0.4f + Vector3.back * radius;
        grab.attachTransform = grip; grab.useDynamicAttach = true; grab.matchAttachPosition = true;
        grab.matchAttachRotation = true; grab.snapToColliderVolume = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(grab);
        var body = root.GetComponent<Rigidbody>();
        body.mass = 0.15f; body.useGravity = false; body.isKinematic = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        var visual = new GameObject("Measured Liquid"); visual.transform.SetParent(root, false);
        var filter = visual.AddComponent<MeshFilter>();
        var render = visual.AddComponent<MeshRenderer>(); render.sharedMaterial = water;
        render.shadowCastingMode = ShadowCastingMode.Off;
        var container = root.gameObject.AddComponent<LiquidContainer>();
        container.Configure(capacity, initial, bottom, radius, height, filter, render);
        if (filter.sharedMesh != null)
        {
            Mesh initialMesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            AssetDatabase.CreateAsset(initialMesh, AssetFolder + "/" + semantic.StableId + "InitialLiquid.asset");
            filter.sharedMesh = initialMesh;
        }
        if (graduated)
        {
            var markings = new GameObject("Volume Graduations").transform; markings.SetParent(root, false);
            for (int ml = 10; ml <= 150; ml += 10)
            {
                float y = bottom.y + height * ml / capacity;
                var tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tick.name = "Mark " + ml; tick.transform.SetParent(markings, false);
                UnityEngine.Object.DestroyImmediate(tick.GetComponent<Collider>());
                tick.transform.localPosition = new Vector3(bottom.x, y, bottom.z - radius - thickness * 1.2f);
                tick.transform.localScale = new Vector3(ml % 50 == 0 ? 0.021f : 0.011f, 0.0008f, 0.0007f) / root.lossyScale.x;
                tick.GetComponent<Renderer>().sharedMaterial = ink;
                if (ml % 50 != 0) continue;
                var label = new GameObject(ml + " mL").AddComponent<TextMesh>();
                label.transform.SetParent(markings, false);
                label.transform.localPosition = tick.transform.localPosition + Vector3.up * (0.004f / root.lossyScale.y);
                label.text = ml + " mL"; label.fontSize = 48; label.characterSize = 0.0023f / root.lossyScale.x;
                label.anchor = TextAnchor.LowerCenter; label.color = new Color(0.015f, 0.035f, 0.045f);
            }
        }
        EditorUtility.SetDirty(root.GetComponent<Renderer>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.GetComponent<Renderer>());
        return container;
    }

    private static XRGrabInteractable CreateRod(Vector3 position)
    {
        var root = new GameObject("Glass Stirring Rod");
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Glass"; capsule.transform.SetParent(root.transform, false);
        capsule.transform.localScale = new Vector3(0.008f, 0.125f, 0.008f);
        capsule.GetComponent<Renderer>().sharedMaterial = MaterialAsset("StirringGlass", new Color(0.45f, 0.78f, 0.85f, 0.75f), true);
        var body = root.AddComponent<Rigidbody>(); body.mass = 0.035f; body.useGravity = false; body.isKinematic = true;
        var tip = new GameObject("StirringTip").transform; tip.SetParent(root.transform, false); tip.localPosition = Vector3.down * 0.12f;
        var grip = new GameObject("Grip").transform; grip.SetParent(root.transform, false); grip.localPosition = Vector3.up * 0.08f;
        var grab = root.AddComponent<XRGrabInteractable>(); grab.attachTransform = grip;
        grab.useDynamicAttach = true; grab.matchAttachPosition = false; grab.matchAttachRotation = true;
        grab.throwOnDetach = false;
        root.AddComponent<SemanticObject>().ConfigureDetailed("glass_rod", "\u73bb\u7483\u68d2", "stirring_rod", "tool", "stirring", "colorless",
            "transparent", "thin_rod", new[] { "glass" }, "none", "none", new[] { "ready" },
            new[] { "\u73bb\u7483\u68d2", "\u6405\u62cc\u68d2", "glass rod", "stirring rod" }, true, true);
        PrefabUtility.SaveAsPrefabAsset(root, AssetFolder + "/GlassStirringRod.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        PolishRod();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(AssetFolder + "/GlassStirringRod.prefab"));
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, 90f));
        return instance.GetComponent<XRGrabInteractable>();
    }

    private static GameObject CreateResidue(LiquidContainer beaker)
    {
        var root = new GameObject("Insoluble Residue"); root.transform.SetParent(beaker.transform, false);
        root.transform.localPosition = beaker.Bottom + Vector3.up * 0.003f;
        Material material = MaterialAsset("Residue", new Color(0.35f, 0.37f, 0.39f), false);
        for (int i = 0; i < 3; i++)
        {
            var grain = GameObject.CreatePrimitive(PrimitiveType.Sphere); grain.transform.SetParent(root.transform, false);
            grain.transform.localPosition = new Vector3((i - 1) * 0.006f, 0f, i % 2 * 0.005f);
            grain.transform.localScale = Vector3.one * 0.004f;
            UnityEngine.Object.DestroyImmediate(grain.GetComponent<Collider>());
            grain.GetComponent<Renderer>().sharedMaterial = material;
        }
        root.SetActive(false);
        return root;
    }

    internal static Material MaterialAsset(string name, Color color, bool transparent)
    {
        string path = AssetFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.color = color; material.SetFloat("_Glossiness", 0.7f);
        if (transparent)
        {
            material.SetFloat("_Mode", 3f); material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Build()
    {
        Directory.CreateDirectory("Builds");
        string path = "Builds/CopperSulfate-SeatedLiquids-PourFix.apk";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
            locationPathName = path, target = BuildTarget.Android, options = BuildOptions.Development });
        File.WriteAllText("Temp/CopperSulfateLiquids.build.txt", result.summary.result + "\n" + path +
            "\nErrors: " + result.summary.totalErrors + "\nSize: " + result.summary.totalSize);
    }

    private static void PolishRod()
    {
        string path = AssetFolder + "/GlassStirringRod.prefab";
        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var glass = contents.transform.Find("Glass");
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            glass.GetComponent<MeshFilter>().sharedMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(primitive);
            glass.localScale = new Vector3(0.008f, 0.121f, 0.008f);
            var collider = glass.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            var capsule = contents.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = contents.AddComponent<CapsuleCollider>();
            capsule.radius = 0.004f; capsule.height = 0.25f; capsule.direction = 1;
            foreach (int sign in new[] { -1, 1 })
            {
                string name = sign < 0 ? "Lower End" : "Upper End";
                if (contents.transform.Find(name) != null) continue;
                var end = GameObject.CreatePrimitive(PrimitiveType.Sphere); end.name = name;
                end.transform.SetParent(contents.transform, false);
                end.transform.localPosition = Vector3.up * (sign * 0.121f); end.transform.localScale = Vector3.one * 0.008f;
                UnityEngine.Object.DestroyImmediate(end.GetComponent<Collider>());
                end.GetComponent<Renderer>().sharedMaterial = glass.GetComponent<Renderer>().sharedMaterial;
            }
            var grab = contents.GetComponent<XRGrabInteractable>(); grab.colliders.Clear(); grab.colliders.Add(capsule);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/CopperSulfateLiquids.rod.txt", "Rod geometry: 25 cm by 8 mm, rounded ends, single capsule collider.");
    }

    private static void ApplyTolerance10()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the stationary scene outside Play Mode first.");
        string backup = "MigrationBackups/BeforeLiquidTolerance10_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        var mixing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Single();
        var serialized = new SerializedObject(mixing);
        serialized.FindProperty("toleranceMl").floatValue = 10f;
        serialized.ApplyModifiedProperties();
        var sequence = mixing.GetComponent<PreparationSequenceController>();
        foreach (var step in sequence.Steps)
            if (step.completionAction == CopperSulfateMixingDetector.MeasureAction)
                step.instruction = step.instruction.Replace("5 mL", "10 mL");
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save tolerance change.");
        File.WriteAllText("Temp/CopperSulfateLiquids.result.txt", "Tolerance is 10 mL. Backup: " + backup);
    }
}
