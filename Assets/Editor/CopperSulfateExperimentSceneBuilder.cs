using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Creates the controlled copper-sulfate experiment scene from the current
/// continuous-experiment scene. The source scene is never rewritten.
/// </summary>
[InitializeOnLoad]
public static class CopperSulfateExperimentSceneBuilder
{
    private const string SourceScenePath =
        "Assets/Scenes/ContinuousExperiment_AStage.unity";
    private const string ExperimentScenePath =
        "Assets/Scenes/ContinuousExperiment_CopperSulfate_AStage.unity";
    private const string GeneratedRootName =
        "CopperSulfateExperimentObjects";
    private const string MaterialFolder =
        "Assets/Guidance/AStage/ExperimentMaterials";
    private const string WaterPrefabPath =
        "Assets/3D Laboratory Environment with Appratus/Prefabs/Beaker water.prefab";
    private const string ReagentDishPrefabPath =
        "Assets/3D Laboratory Environment with Appratus/Prefabs/china dish.prefab";
    private const string RequestFile =
        "Temp/CopperSulfateExperimentSceneBuilder.request";
    private const string ForcepsSemanticRequestFile =
        "Temp/CopperSulfateForcepsSemantic.request";
    private static bool _requestQueued;

    static CopperSulfateExperimentSceneBuilder()
    {
        // The scene is intentionally created only from the menu so opening
        // the project cannot unexpectedly replace the active scene.
    }

    [InitializeOnLoadMethod]
    private static void ProcessRequestedBuild()
    {
        string buildRequest = Path.GetFullPath(RequestFile);
        string forcepsRequest = Path.GetFullPath(ForcepsSemanticRequestFile);
        if (Application.isBatchMode || _requestQueued ||
            !File.Exists(buildRequest) && !File.Exists(forcepsRequest))
        {
            return;
        }

        _requestQueued = true;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _requestQueued = false;
                ProcessRequestedBuild();
                return;
            }

            try
            {
                if (File.Exists(buildRequest))
                {
                    CreateScene();
                    File.Delete(buildRequest);
                    Debug.Log(
                        "[Copper Sulfate Builder] Requested scene creation completed.");
                }

                if (File.Exists(forcepsRequest))
                {
                    ConfigureForcepsSemantic();
                    File.Delete(forcepsRequest);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _requestQueued = false;
            }
        };
    }

    [MenuItem("Tools/Stream Manager/Create Copper Sulfate Experiment Scene", priority = 30)]
    public static void CreateFromMenu()
    {
        CreateScene();
        EditorUtility.DisplayDialog(
            "Copper Sulfate Experiment",
            "已创建独立硫酸铜实验场景:\n" + ExperimentScenePath,
            "确定");
    }

    [MenuItem("Tools/Stream Manager/Configure Copper Sulfate Forceps Semantic", priority = 31)]
    public static void ConfigureForcepsSemanticFromMenu()
    {
        ConfigureForcepsSemantic();
        EditorUtility.DisplayDialog(
            "Copper Sulfate Experiment",
            "已为镊子配置语义属性并刷新场景对象注册表。",
            "确定");
    }

    public static void CreateBatch()
    {
        try
        {
            CreateScene();
            Debug.Log(
                "[Copper Sulfate Builder] Scene created: " +
                ExperimentScenePath);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "请先退出 Play Mode，再创建实验场景。");
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            throw new InvalidOperationException(
                "Source scene not found: " + SourceScenePath);
        }

        EnsureFolder("Assets/Guidance/AStage");
        EnsureFolder(MaterialFolder);

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ExperimentScenePath) == null &&
            !AssetDatabase.CopyAsset(SourceScenePath, ExperimentScenePath))
        {
            throw new InvalidOperationException(
                "Could not copy source scene to " + ExperimentScenePath);
        }

        Scene scene = EditorSceneManager.OpenScene(
            ExperimentScenePath,
            OpenSceneMode.Single);

        HideUnusedEquipment(scene);
        GameObject generatedRoot = GetOrCreateGeneratedRoot();
        ClearGeneratedObjects(generatedRoot);

        SemanticObject beaker = FindSemantic("beaker");
        SemanticObject cylinder = FindSemantic("graduated_cylinder");
        SemanticObject funnel = FindSemantic("glass_funnel");
        SemanticObject dish = FindSemantic("china_dish");
        SemanticObject spiritLamp = FindSemantic("spirit_lamp");
        RequireSemantic(beaker, "beaker");
        RequireSemantic(cylinder, "graduated_cylinder");
        RequireSemantic(funnel, "glass_funnel");
        RequireSemantic(dish, "china_dish");
        RequireSemantic(spiritLamp, "spirit_lamp");

        HideContainerContents(beaker.gameObject);
        ConfigureSemantic(
            beaker.gameObject,
            "beaker",
            "空烧杯",
            "container",
            "mixing",
            "colorless",
            "transparent",
            "wide_cylinder",
            new[] { "glass" },
            new[]
            {
                "空烧杯", "烧杯", "玻璃烧杯", "空的烧杯",
                "empty beaker", "glass beaker"
            },
            true,
            true,
            new[] { "empty" });

        ConfigureSemantic(
            spiritLamp.gameObject,
            "spirit_lamp",
            "酒精灯",
            "device",
            "heating",
            "silver",
            "opaque",
            "lamp",
            new[] { "metal", "glass" },
            new[]
            {
                "酒精灯", "加热灯",
                "spirit lamp", "alcohol lamp", "heating lamp"
            },
            true,
            true,
            new[] { "contains_liquid", "contains_alcohol" },
            "alcohol",
            "colorless");

        GameObject tripodStand = GameObject.Find("Boiling_Liquid_Stand");
        if (tripodStand != null)
        {
            ConfigureSemantic(
                tripodStand,
                "tripod_stand",
                "三脚架",
                "support",
                "supporting",
                "silver",
                "opaque",
                "tripod",
                new[] { "metal" },
                new[]
                {
                    "三脚架", "加热三脚架", "三脚支架",
                    "tripod", "tripod stand", "heating stand"
                },
                true,
                true);

            // The tripod is a separate apparatus. The VR user must be able to
            // pick it up and position it manually during the heating step.
            EnsureGrabbable(tripodStand);
        }

        Material copperMaterial = GetOrCreateMaterial(
            "CopperSulfate", new Color(0.08f, 0.32f, 0.86f));
        Vector3 beakerPosition = beaker.transform.position;
        Vector3 cylinderPosition = cylinder.transform.position;

        GameObject reagent = CreateReagent(
            generatedRoot.transform,
            "CopperSulfateCrude",
            beakerPosition + new Vector3(-0.42f, 0f, 0.10f),
            ReagentDishPrefabPath,
            dish.gameObject,
            copperMaterial);
        ConfigureSemantic(
            reagent,
            "copper_sulfate_crude",
            "粗硫酸铜",
            "reagent",
            "soluble_solid",
            "blue",
            "opaque",
            "crystalline_powder",
            new[] { "crystal", "solid" },
            new[]
            {
                "粗硫酸铜", "硫酸铜", "硫酸铜晶体", "蓝色晶体",
                "copper sulfate", "copper sulfate crystals",
                "crude copper sulfate"
            },
            true,
            true);

        GameObject water = CreateWaterSource(
            generatedRoot.transform,
            "WaterSource",
            cylinderPosition + new Vector3(0.34f, 0f, 0.10f),
            WaterPrefabPath,
            beaker.gameObject);
        ConfigureSemantic(
            water,
            "water_source",
            "水源",
            "liquid_source",
            "supplying",
            "colorless",
            "transparent",
            "water_container",
            new[] { "glass", "liquid" },
            new[]
            {
                "水", "蒸馏水", "纯水", "水源", "装水容器",
                "water", "distilled water", "water source"
            },
            true,
            true);

        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        GuidanceManager guidanceManager =
            UnityEngine.Object.FindObjectOfType<GuidanceManager>(true);
        SceneObjectRegistry registry =
            UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        ExperimentStateController stateController =
            UnityEngine.Object.FindObjectOfType<ExperimentStateController>(true);
        ExperimentLogger logger =
            UnityEngine.Object.FindObjectOfType<ExperimentLogger>(true);
        GuidanceDataChannelReceiver receiver =
            UnityEngine.Object.FindObjectOfType<GuidanceDataChannelReceiver>(true);
        ExperimentInteractionTracker tracker =
            UnityEngine.Object.FindObjectOfType<ExperimentInteractionTracker>(true);

        RequireComponent(sequence, "PreparationSequenceController");
        RequireComponent(guidanceManager, "GuidanceManager");
        RequireComponent(registry, "SceneObjectRegistry");
        RequireComponent(stateController, "ExperimentStateController");
        RequireComponent(logger, "ExperimentLogger");
        RequireComponent(receiver, "GuidanceDataChannelReceiver");
        RequireComponent(tracker, "ExperimentInteractionTracker");

        List<PreparationSequenceController.Step> steps =
            new List<PreparationSequenceController.Step>
            {
                Step(
                    "copper_sulfate_crude",
                    "找到粗硫酸铜，准备进行溶解。",
                    reagent),
                Step(
                    "water_source",
                    "找到水源，准备量取所需的水。",
                    water),
                Step(
                    "graduated_cylinder",
                    "找到量筒，量取适量的水。",
                    cylinder.gameObject),
                Step(
                    "beaker",
                    "找到烧杯，将粗硫酸铜加入水中并充分溶解。",
                    beaker.gameObject),
                Step(
                    "glass_funnel",
                    "找到玻璃漏斗，使用滤纸过滤溶液。",
                    funnel.gameObject),
                Step(
                    "china_dish",
                    "找到蒸发皿，接收过滤后的硫酸铜溶液。",
                    dish.gameObject)
            };

        if (tripodStand != null)
        {
            steps.Add(
                Step(
                    "tripod_stand",
                    "找到三脚架，自行组装加热装置。",
                    tripodStand));
        }

        steps.Add(
            Step(
                "spirit_lamp",
                "找到酒精灯，配合三脚架加热蒸发皿中的溶液进行浓缩。",
                spiritLamp.gameObject));

        sequence.Configure(guidanceManager, steps.ToArray(), false);

        SerializedObject registrySerialized = new SerializedObject(registry);
        SetBoolean(registrySerialized, "includeInactiveObjects", false);
        SetBoolean(registrySerialized, "logCatalogOnStart", false);
        registrySerialized.ApplyModifiedPropertiesWithoutUndo();
        registry.Refresh();

        receiver.GuidanceManager = guidanceManager;
        receiver.PreparationSequence = sequence;
        receiver.ObjectRegistry = registry;
        receiver.StateController = stateController;
        receiver.ExperimentLogger = logger;

        logger.StateController = stateController;
        SetObjectReference(
            new SerializedObject(tracker), "stateController", stateController);
        SerializedObject trackerSerialized = new SerializedObject(tracker);
        SetObjectReference(trackerSerialized, "experimentLogger", logger);
        SetObjectReference(trackerSerialized, "preparationSequence", sequence);
        trackerSerialized.ApplyModifiedPropertiesWithoutUndo();

        ConfigureForcepsSemantic(false);
        CopperSulfateTransferInstaller.ConfigureScene(scene);
        CopperSulfateErgonomicsInstaller.ConfigureScene(scene);

        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(registry);
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(logger);
        EditorUtility.SetDirty(tracker);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ExperimentScenePath))
        {
            throw new InvalidOperationException(
                "Could not save " + ExperimentScenePath);
        }

        ActivateSceneInBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateScene();

        Debug.Log(
            "[Copper Sulfate Builder] Configured " + sequence.StepCount +
            " sequence steps; registry objects=" + registry.Count + ".");
    }

    private static void HideUnusedEquipment(Scene scene)
    {
        string[] semanticIdsToHide =
        {
            "florence_flask",
            "dropper",
            "test_tube_rack",
            "test_tube",
            "crucible",
            "crucible_tongs"
        };

        HashSet<string> hiddenIds = new HashSet<string>(
            semanticIdsToHide,
            StringComparer.OrdinalIgnoreCase);
        foreach (SemanticObject semantic in
                 UnityEngine.Object.FindObjectsOfType<SemanticObject>(true))
        {
            if (semantic != null && hiddenIds.Contains(semantic.StableId) &&
                !semantic.StableId.StartsWith("complex_", StringComparison.OrdinalIgnoreCase))
            {
                semantic.gameObject.SetActive(false);
            }
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null || root.name.StartsWith("complex_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = root.name.Replace("_", " ").ToLowerInvariant();
            bool unusedRack = name.Contains("test tube rack") ||
                              name.Contains("test tube support");
            bool unusedCrucible = name.Contains("crucible") ||
                                  name.Contains("tongs");
            if (unusedRack || unusedCrucible)
            {
                root.SetActive(false);
            }
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null &&
                    child.name.StartsWith("PlacementZones_", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }

    private static GameObject GetOrCreateGeneratedRoot()
    {
        GameObject root = GameObject.Find(GeneratedRootName);
        if (root != null)
        {
            return root;
        }

        root = new GameObject(GeneratedRootName);
        return root;
    }

    private static void ClearGeneratedObjects(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        for (int index = root.transform.childCount - 1; index >= 0; index--)
        {
            UnityEngine.Object.DestroyImmediate(root.transform.GetChild(index).gameObject);
        }
    }

    private static void HideContainerContents(GameObject container)
    {
        if (container == null)
        {
            return;
        }

        foreach (Transform child in
                 container.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == container.transform)
            {
                continue;
            }

            string name = child.name.ToLowerInvariant();
            if (name.Contains("liquid") || name.Contains("water"))
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static GameObject CreateReagent(
        Transform parent,
        string name,
        Vector3 position,
        string prefabPath,
        GameObject referenceObject,
        Material crystalMaterial)
    {
        GameObject root = CreatePackageObject(
            parent,
            name,
            prefabPath,
            position,
            referenceObject,
            0.8f);
        CreateCrystalContents(root.transform, crystalMaterial);
        return root;
    }

    private static GameObject CreateWaterSource(
        Transform parent,
        string name,
        Vector3 position,
        string prefabPath,
        GameObject referenceObject)
    {
        return CreatePackageObject(
            parent,
            name,
            prefabPath,
            position,
            referenceObject,
            0.9f);
    }

    private static void EnsureGrabbable(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        // The package prefab has a root MeshCollider without a mesh. Disable
        // that invalid collider and use a stable compound box instead.
        foreach (MeshCollider meshCollider in
                 root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (meshCollider.sharedMesh == null)
            {
                meshCollider.enabled = false;
            }
        }

        Rigidbody rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = root.AddComponent<Rigidbody>();
        }
        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;

        XRGrabInteractable grab = root.GetComponent<XRGrabInteractable>();
        if (grab == null)
        {
            grab = root.AddComponent<XRGrabInteractable>();
        }
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false;

        AddRootCollider(root);
    }

    private static GameObject CreatePackageObject(
        Transform parent,
        string name,
        string prefabPath,
        Vector3 position,
        GameObject referenceObject,
        float fitScale)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                "Required laboratory prefab was not found: " + prefabPath);
        }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        root.name = name;
        root.transform.SetParent(parent, false);
        root.transform.position = position;

        Bounds prefabBounds = GetRendererBounds(root);
        Bounds referenceBounds = GetRendererBounds(referenceObject);
        float scale = CalculateFitScale(prefabBounds, referenceBounds) * fitScale;
        root.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        foreach (MeshCollider meshCollider in
                 root.GetComponentsInChildren<MeshCollider>(true))
        {
            meshCollider.convex = true;
        }

        Rigidbody rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = root.AddComponent<Rigidbody>();
        }
        rigidbody.useGravity = false;
        rigidbody.isKinematic = true;

        XRGrabInteractable grab = root.GetComponent<XRGrabInteractable>();
        if (grab == null)
        {
            grab = root.AddComponent<XRGrabInteractable>();
        }
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false;

        AddRootCollider(root);
        return root;
    }

    private static void CreateCrystalContents(
        Transform parent,
        Material material)
    {
        GameObject contents = new GameObject("Copper Sulfate Crystals");
        contents.transform.SetParent(parent, false);
        contents.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        contents.transform.localScale = Vector3.one * 0.11f;

        Vector3[] offsets =
        {
            new Vector3(-0.22f, 0f, -0.10f),
            new Vector3(0.02f, 0.03f, 0.02f),
            new Vector3(0.20f, 0.01f, -0.04f),
            new Vector3(-0.05f, 0.01f, 0.18f)
        };
        for (int index = 0; index < offsets.Length; index++)
        {
            GameObject crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "Crystal " + (index + 1);
            crystal.transform.SetParent(contents.transform, false);
            crystal.transform.localPosition = offsets[index];
            crystal.transform.localRotation = Quaternion.Euler(
                0f, index * 31f, index * 17f);
            crystal.transform.localScale = new Vector3(0.25f, 0.12f, 0.22f);
            Renderer renderer = crystal.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }

    private static float CalculateFitScale(
        Bounds source,
        Bounds reference)
    {
        if (source.size.x <= 0f || source.size.y <= 0f || source.size.z <= 0f)
        {
            return 1f;
        }

        float x = reference.size.x / source.size.x;
        float y = reference.size.y / source.size.y;
        float z = reference.size.z / source.size.z;
        return Mathf.Min(x, Mathf.Min(y, z));
    }

    private static Bounds GetRendererBounds(GameObject target)
    {
        Renderer[] renderers = target == null
            ? Array.Empty<Renderer>()
            : target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(target != null ? target.transform.position : Vector3.zero, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static void AddRootCollider(GameObject root)
    {
        BoxCollider collider = root.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = root.AddComponent<BoxCollider>();
        }
        Bounds bounds = GetRendererBounds(root);
        collider.center = root.transform.InverseTransformPoint(bounds.center);
        Vector3 localSize = root.transform.InverseTransformVector(bounds.size);
        collider.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z));
    }

    private static void ConfigureSemantic(
        GameObject target,
        string id,
        string displayName,
        string category,
        string function,
        string color,
        string transparency,
        string shape,
        IEnumerable<string> materials,
        IEnumerable<string> aliases,
        bool selectable,
        bool grabbable,
        IEnumerable<string> stateTags = null,
        string contents = "none",
        string contentColor = "none")
    {
        SemanticObject semantic = target.GetComponent<SemanticObject>() ??
            target.AddComponent<SemanticObject>();
        semantic.ConfigureDetailed(
            id,
            displayName,
            id,
            category,
            function,
            color,
            transparency,
            shape,
            materials,
            contents,
            contentColor,
            stateTags ?? new[] { "ready" },
            aliases,
            selectable,
            grabbable);
        EditorUtility.SetDirty(semantic);
    }

    private static void ConfigureForcepsSemantic(bool saveScene = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "请先退出 Play Mode，再配置镊子语义属性。");
        }

        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ExperimentScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(
                ExperimentScenePath,
                OpenSceneMode.Single);

        SemanticObject forceps = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<SemanticObject>(true))
            .Where(item => item != null &&
                (item.StableId == "forceps" || string.Equals(
                    item.gameObject.name, "forceps", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.StableId == "forceps")
            .FirstOrDefault();
        if (forceps == null)
        {
            throw new InvalidOperationException(
                "当前硫酸铜实验场景中没有找到镊子对象。");
        }

        ConfigureSemantic(
            forceps.gameObject,
            "forceps",
            "镊子",
            "tool",
            "handling",
            "silver",
            "opaque",
            "slender_tool",
            new[] { "metal" },
            new[]
            {
                "镊子", "金属镊子", "晶体夹", "夹取工具",
                "forceps", "tweezers", "crystal forceps"
            },
            true,
            true,
            new[] { "clean", "available" });

        SceneObjectRegistry registry = UnityEngine.Object
            .FindObjectOfType<SceneObjectRegistry>(true);
        if (registry != null)
        {
            registry.Refresh();
            EditorUtility.SetDirty(registry);
        }

        forceps.gameObject.SetActive(true);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene && !EditorSceneManager.SaveScene(scene, ExperimentScenePath))
        {
            throw new InvalidOperationException(
                "无法保存镊子语义属性。");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            "[Copper Sulfate Builder] Forceps semantic configured and registered.");
    }

    private static PreparationSequenceController.Step Step(
        string id,
        string instruction,
        GameObject target)
    {
        return new PreparationSequenceController.Step
        {
            id = id,
            instruction = instruction,
            target = target
        };
    }

    private static SemanticObject FindSemantic(string stableId)
    {
        return UnityEngine.Object.FindObjectsOfType<SemanticObject>(true)
            .FirstOrDefault(item => item != null &&
                string.Equals(item.StableId, stableId, StringComparison.OrdinalIgnoreCase));
    }

    private static void RequireSemantic(SemanticObject semantic, string stableId)
    {
        if (semantic == null)
        {
            throw new InvalidOperationException(
                "Required semantic object not found: " + stableId);
        }

        semantic.gameObject.SetActive(true);
    }

    private static void RequireComponent<T>(T component, string componentName)
        where T : UnityEngine.Object
    {
        if (component == null)
        {
            throw new InvalidOperationException(
                "Required component not found: " + componentName);
        }
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No compatible Unity material shader was found.");
            }

            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[index]);
            }
            current = next;
        }
    }

    private static void SetBoolean(
        SerializedObject serialized,
        string propertyName,
        bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static void SetObjectReference(
        SerializedObject serialized,
        string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ActivateSceneInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();
        bool found = false;
        for (int index = 0; index < scenes.Count; index++)
        {
            EditorBuildSettingsScene scene = scenes[index];
            bool isExperiment = string.Equals(
                scene.path,
                ExperimentScenePath,
                StringComparison.OrdinalIgnoreCase);
            bool isSource = string.Equals(
                scene.path,
                SourceScenePath,
                StringComparison.OrdinalIgnoreCase);
            if (isExperiment)
            {
                scenes[index] = new EditorBuildSettingsScene(scene.path, true);
                found = true;
            }
            else if (isSource)
            {
                scenes[index] = new EditorBuildSettingsScene(scene.path, false);
            }
        }

        if (!found)
        {
            scenes.Add(new EditorBuildSettingsScene(ExperimentScenePath, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ValidateScene()
    {
        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        SceneObjectRegistry registry =
            UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        int expectedStepCount =
            GameObject.Find("Boiling_Liquid_Stand") != null ? 8 : 7;
        if (sequence == null || registry == null ||
            sequence.StepCount != expectedStepCount)
        {
            throw new InvalidOperationException(
                "Copper sulfate scene validation failed: sequence or registry is incomplete.");
        }

        foreach (PreparationSequenceController.Step step in sequence.Steps)
        {
            if (step == null || step.target == null ||
                step.target.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                throw new InvalidOperationException(
                    "A sequence target is missing a Collider: " +
                    (step?.id ?? "null"));
            }
        }

        string[] hiddenIds =
        {
            "florence_flask", "dropper", "test_tube_rack", "test_tube",
            "crucible", "crucible_tongs"
        };
        foreach (string hiddenId in hiddenIds)
        {
            SemanticObject hidden = FindSemantic(hiddenId);
            if (hidden != null && hidden.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "Unused apparatus is still active: " + hiddenId);
            }
        }
    }
}
