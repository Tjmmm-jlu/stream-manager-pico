using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateHeatingInstaller
{
    private const string RequestPath = "Temp/CopperSulfateHeating.request";
    private static bool _busy;
    static CopperSulfateHeatingInstaller() => EditorApplication.update += ProcessRequest;
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
            else if (command == "crystallization") InstallCrystallization();
            else if (command == "verify")
            {
                PicoPinchToggleVerification.Run(); CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/CopperSulfateHeating.result.txt", "PASS: pinch, liquid, filtration, heating, crystallization and reset workflows.");
            }
            else if (command == "build" || command == "build-crystallization")
            {
                string apk = command == "build-crystallization" ? "Builds/CopperSulfate-Crystallization.apk" : "Builds/CopperSulfate-Heating.apk";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { CopperSulfateLiquidInstaller.ScenePath },
                    locationPathName = apk, target = BuildTarget.Android, options = BuildOptions.Development });
                File.WriteAllText("Temp/CopperSulfateHeating.build.txt", report.summary.result + "\n" + apk + "\nErrors: " + report.summary.totalErrors);
            }
            else throw new InvalidOperationException("Unknown heating command: " + command);
        }
        catch (Exception error)
        { File.WriteAllText("Temp/CopperSulfateHeating.result.txt", error.ToString()); Debug.LogException(error); }
        finally { _busy = false; }
    }

    private static void Install()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath) throw new InvalidOperationException("Open the stationary scene.");
        var roots = scene.GetRootGameObjects();
        var filter = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateFiltrationDetector>()).Single();
        if (filter.GetComponent<CopperSulfateHeatingDetector>() != null) throw new InvalidOperationException("Heating is already installed.");
        string backup = "MigrationBackups/BeforeHeatingInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        var sequence = filter.GetComponent<PreparationSequenceController>();
        var lamp = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Single(s => s.StableId == "spirit_lamp").GetComponent<XRGrabInteractable>();
        var ring = filter.Stand.GetComponentsInChildren<MeshRenderer>().Single(m => m.name.StartsWith("pTorus"));
        var baseMesh = filter.Stand.GetComponentsInChildren<MeshRenderer>().Single(m => m.name == "pCube89");
        var wickMesh = lamp.GetComponentsInChildren<MeshRenderer>().Single(m => m.name == "pCylinder14");
        var lampBody = lamp.GetComponentsInChildren<MeshRenderer>().Single(m => m.name == "pSphere8");
        Transform Point(string name, Transform parent, Vector3 world)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.SetPositionAndRotation(world, Quaternion.identity); return t; }
        var dishSeat = Point("Heating Dish Seat", filter.Stand.transform, ring.bounds.center + Vector3.down * 0.014f);
        var lampSeat = Point("Heating Lamp Seat", filter.Stand.transform, new Vector3(ring.bounds.center.x,
            baseMesh.bounds.max.y + lamp.transform.position.y - lampBody.bounds.min.y + 0.002f, ring.bounds.center.z));
        lampSeat.rotation = lamp.transform.rotation; dishSeat.rotation = filter.Stand.transform.rotation;
        var wick = Point("Ignition Wick", lamp.transform, new Vector3(wickMesh.bounds.center.x, wickMesh.bounds.max.y, wickMesh.bounds.center.z));
        var lidSeat = Point("Lamp Cap Seat", lamp.transform, wick.position - Vector3.up * 0.022f);
        var table = roots.Single(r => r.name == "MixingStation").transform;
        var tabletop = CopperSulfateStationarySceneBuilder.FindTabletop(table);
        var parking = Point("Lamp Cap Parking", null, new Vector3(4.49f, tabletop.bounds.max.y, 7.27f));
        var capMat = CopperSulfateLiquidInstaller.MaterialAsset("LampCap", new Color(0.16f, 0.19f, 0.2f), false);
        var gripMat = CopperSulfateLiquidInstaller.MaterialAsset("LighterHandle", new Color(0.7f, 0.08f, 0.065f), false);
        var metal = CopperSulfateLiquidInstaller.MaterialAsset("LighterMetal", new Color(0.38f, 0.41f, 0.43f), false);
        var cap = NewGrab("Spirit Lamp Cap", "lamp_cap", "\u9152\u7cbe\u706f\u5e3d", "lamp_cap", "extinguishing", new[] { "\u706f\u5e3d", "\u706d\u706b\u76d6", "lamp cap" });
        cap.transform.SetPositionAndRotation(lidSeat.position, lidSeat.rotation);
        var capMesh = Revolve("Hollow Lamp Cap", new[] { new Vector2(0.025f, 0f), new Vector2(0.025f, 0.04f), new Vector2(0f, 0.04f),
            new Vector2(0f, 0.037f), new Vector2(0.022f, 0.037f), new Vector2(0.022f, 0f) });
        var capVisual = new GameObject("Cap Shell"); capVisual.transform.SetParent(cap.transform, false);
        capVisual.AddComponent<MeshFilter>().sharedMesh = capMesh; capVisual.AddComponent<MeshRenderer>().sharedMaterial = capMat;
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            var wall = new GameObject("Cap Wall " + i); wall.transform.SetParent(cap.transform, false);
            wall.transform.localPosition = new Vector3(Mathf.Sin(a) * 0.0235f, 0.02f, Mathf.Cos(a) * 0.0235f);
            wall.transform.localRotation = Quaternion.Euler(0, i * 30f, 0);
            wall.AddComponent<BoxCollider>().size = new Vector3(0.014f, 0.04f, 0.003f);
        }
        var lidCollider = cap.gameObject.AddComponent<BoxCollider>(); lidCollider.center = Vector3.up * 0.0385f; lidCollider.size = new Vector3(0.044f, 0.003f, 0.044f);
        cap.colliders.AddRange(cap.GetComponentsInChildren<Collider>());
        cap.GetComponent<ExperimentGrabReturn>().ConfigureReturnAnchor(parking);
        var lighter = NewGrab("Long Handle Lighter", "long_lighter", "\u957f\u67c4\u70b9\u706b\u5668", "lighter", "igniting", new[] { "\u70b9\u706b\u5668", "\u6253\u706b\u5668", "long lighter" });
        lighter.transform.SetPositionAndRotation(new Vector3(4.29f, tabletop.bounds.max.y + 0.012f, 7.25f), Quaternion.Euler(90f, 0f, 90f));
        Primitive(lighter.transform, "Red Grip", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.026f, 0.047f, 0.026f), gripMat, true);
        Primitive(lighter.transform, "Metal Neck", PrimitiveType.Cylinder, Vector3.up * 0.094f, new Vector3(0.009f, 0.048f, 0.009f), metal, true);
        Primitive(lighter.transform, "Ignition Head", PrimitiveType.Cylinder, Vector3.up * 0.146f, new Vector3(0.014f, 0.007f, 0.014f), capMat, true);
        lighter.colliders.AddRange(lighter.GetComponentsInChildren<Collider>());
        var tip = Point("Lighter Tip", lighter.transform, lighter.transform.TransformPoint(Vector3.up * 0.153f));
        var flameMat = CopperSulfateLiquidInstaller.MaterialAsset("LampFlame", new Color(0.3f, 0.65f, 1f, 0.5f), true);
        flameMat.EnableKeyword("_EMISSION"); flameMat.SetColor("_EmissionColor", new Color(0.15f, 0.5f, 1.5f));
        var coreMat = CopperSulfateLiquidInstaller.MaterialAsset("FlameCore", new Color(1f, 0.75f, 0.2f), false);
        coreMat.EnableKeyword("_EMISSION"); coreMat.SetColor("_EmissionColor", new Color(1.5f, 0.7f, 0.1f));
        var flame = new GameObject("Alcohol Flame"); flame.transform.SetParent(lamp.transform, false);
        flame.transform.position = wick.position;
        flame.transform.localScale = new Vector3(1f / lamp.transform.lossyScale.x, 1f / lamp.transform.lossyScale.y, 1f / lamp.transform.lossyScale.z);
        var flameMesh = Revolve("Flame Teardrop", new[] { new Vector2(0f, 0f), new Vector2(0.008f, 0.008f), new Vector2(0.01f, 0.025f), new Vector2(0.006f, 0.065f), new Vector2(0f, 0.105f) });
        flame.AddComponent<MeshFilter>().sharedMesh = flameMesh; flame.AddComponent<MeshRenderer>().sharedMaterial = flameMat;
        var core = new GameObject("Warm Flame Core"); core.transform.SetParent(flame.transform, false); core.transform.localScale = new Vector3(0.65f, 0.55f, 0.65f);
        core.AddComponent<MeshFilter>().sharedMesh = flameMesh; core.AddComponent<MeshRenderer>().sharedMaterial = coreMat;
        flame.SetActive(false);
        var steamObject = new GameObject("Heating Steam"); steamObject.transform.SetParent(filter.Dish.transform, false);
        steamObject.transform.localPosition = filter.Dish.Bottom + Vector3.up * filter.Dish.Height;
        var steam = steamObject.AddComponent<ParticleSystem>(); var main = steam.main;
        main.playOnAwake = false; main.startLifetime = 0.9f; main.startSpeed = 0.04f; main.startSize = 0.013f;
        main.startColor = new Color(0.87f, 0.91f, 0.94f, 0.6f); main.maxParticles = 18;
        var emission = steam.emission; emission.rateOverTime = 8f;
        var shape = steam.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.026f; shape.rotation = new Vector3(-90f, 0f, 0f);
        var velocity = steam.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World; velocity.y = 0.06f;
        var color = steam.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.2f, 0.2f), new GradientAlphaKey(0f, 1f) }); color.color = gradient;
        steam.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        steam.GetComponent<Renderer>().enabled = false;
        var heating = filter.gameObject.AddComponent<CopperSulfateHeatingDetector>();
        heating.Configure(sequence, filter, lamp, cap, lighter, dishSeat, lampSeat, lidSeat, parking, wick, tip, flame, steam);
        PlaceTools(scene, heating);
        sequence.Steps[5].id = "heating_assembly"; sequence.Steps[5].completionAction = CopperSulfateHeatingDetector.AssembleAction;
        sequence.Steps[5].instruction = "\u53d6\u4e0b\u6f0f\u6597\uff0c\u5c06\u84b8\u53d1\u76bf\u548c\u9152\u7cbe\u706f\u5b89\u88c5\u5230\u94c1\u67b6\u53f0\u3002";
        sequence.Steps[6].completionAction = CopperSulfateHeatingDetector.HeatAction;
        sequence.Steps[6].instruction = "\u53d6\u4e0b\u706f\u5e3d\uff0c\u7528\u957f\u67c4\u70b9\u706b\u5668\u70b9\u71c3\u9152\u7cbe\u706f\uff1b\u6d53\u7f29\u5b8c\u6210\u540e\u76d6\u5e3d\u706d\u706b\u3002";
        filter.GetComponent<SceneObjectRegistry>().Refresh(); filter.GetComponent<ExperimentInteractionTracker>().RefreshTrackedObjects();
        EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(heating); EditorUtility.SetDirty(flameMat); EditorUtility.SetDirty(coreMat);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save heating apparatus.");
        File.WriteAllText("Temp/CopperSulfateHeating.result.txt", "Installed heating. Backup: " + backup);
        var focus = lamp.transform.position; var eye = focus + new Vector3(0.22f, 0.22f, -0.28f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateHeating.tools.png", eye, Quaternion.LookRotation(focus - eye));
    }

    private static void Polish()
    {
        var scene = SceneManager.GetActiveScene();
        var heat = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateHeatingDetector>()).Single();
        var steam = heat.GetComponent<CopperSulfateFiltrationDetector>().Dish.GetComponentInChildren<ParticleSystem>();
        steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); steam.GetComponent<Renderer>().enabled = false;
        var main = steam.main; main.startColor = new Color(0.87f, 0.91f, 0.94f, 0.6f);
        var flameMat = CopperSulfateLiquidInstaller.MaterialAsset("LampFlame", new Color(0.3f, 0.65f, 1f, 0.5f), true);
        flameMat.EnableKeyword("_EMISSION"); flameMat.SetColor("_EmissionColor", new Color(0.15f, 0.5f, 1.5f)); AssetDatabase.SaveAssets();
        PlaceTools(scene, heat); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/CopperSulfateHeating.result.txt", "Polished tool placement and disabled inactive steam renderer.");
    }

    private static void InstallCrystallization()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath) throw new InvalidOperationException("Open the stationary scene.");
        var heat = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateHeatingDetector>()).Single();
        var dish = heat.GetComponent<CopperSulfateFiltrationDetector>().Dish;
        string backup = "MigrationBackups/BeforeCrystallizationInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        var crystals = dish.transform.Find("Crystallized Copper Sulfate");
        if (crystals == null)
        {
            crystals = new GameObject("Crystallized Copper Sulfate").transform;
            crystals.SetParent(dish.transform, false); crystals.localPosition = dish.Bottom;
            var material = CopperSulfateLiquidInstaller.MaterialAsset("CrystallizedCopperSulfate", new Color(0.025f, 0.32f, 0.9f), false);
            material.SetFloat("_Glossiness", 0.45f);
            for (int i = 0; i < 9; i++)
            {
                float angle = (i - 1) * Mathf.PI / 4f;
                float radius = i == 0 ? 0f : 0.015f;
                float height = 0.012f + i % 4 * 0.002f;
                var grain = GameObject.CreatePrimitive(PrimitiveType.Cube); grain.name = "Blue Crystal " + (i + 1);
                grain.transform.SetParent(crystals, false);
                grain.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, height * 0.5f + 0.001f, Mathf.Sin(angle) * radius);
                grain.transform.localRotation = Quaternion.Euler(0f, i * 37f, (i % 3 - 1) * 8f);
                grain.transform.localScale = new Vector3(0.007f + i % 3 * 0.002f, height, 0.009f + i % 2 * 0.004f);
                grain.GetComponent<Renderer>().sharedMaterial = material;
                UnityEngine.Object.DestroyImmediate(grain.GetComponent<Collider>());
            }
        }
        crystals.gameObject.SetActive(false);
        var serialized = new SerializedObject(heat);
        serialized.FindProperty("crystallizedContents").objectReferenceValue = crystals.gameObject;
        serialized.ApplyModifiedProperties();
        var sequence = heat.GetComponent<PreparationSequenceController>();
        sequence.Steps.Single(s => s.completionAction == CopperSulfateHeatingDetector.HeatAction).instruction =
            "\u53d6\u4e0b\u706f\u5e3d\uff0c\u7528\u957f\u67c4\u70b9\u706b\u5668\u70b9\u71c3\u9152\u7cbe\u706f\uff1b\u6d53\u7f29\u5b8c\u6210\u540e\u76d6\u5e3d\u706d\u706b\uff0c\u7b49\u5f85 5 \u79d2\u7ed3\u6676\u3002";
        EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(heat);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save crystallization setup.");
        File.WriteAllText("Temp/CopperSulfateHeating.result.txt", "Installed automatic five-second crystallization. Backup: " + backup);
    }

    private static void PlaceTools(Scene scene, CopperSulfateHeatingDetector heat)
    {
        var roots = scene.GetRootGameObjects();
        var stand = roots.SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>()).Single().PreparationStart.position;
        var semantics = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Where(s => s.gameObject.activeInHierarchy).ToArray();
        Vector3 capPosition = heat.Cap.transform.position; Quaternion capRotation = heat.Cap.transform.rotation;
        Place(heat.Lighter, true);
        var support = heat.GetComponent<CopperSulfateFiltrationDetector>().Stand.transform;
        var baseMesh = support.GetComponentsInChildren<MeshRenderer>().Single(m => m.name == "pCube89");
        Vector3 besideLamp = heat.LampMount.position + support.right * 0.125f;
        besideLamp.y = baseMesh.bounds.max.y + 0.001f;
        heat.CapParking.SetParent(support, true); heat.CapParking.SetPositionAndRotation(besideLamp, support.rotation);
        heat.Cap.transform.SetPositionAndRotation(capPosition, capRotation);
        void Place(XRGrabInteractable item, bool longTool)
        {
            float best = float.PositiveInfinity; Vector3 chosen = default; Quaternion chosenRotation = default;
            foreach (string name in new[] { "MixingStation", "PreparationStation", "FilteringStation" })
            {
                var table = roots.Single(r => r.name == name).transform;
                var tabletop = CopperSulfateStationarySceneBuilder.FindTabletop(table);
                var center = tabletop.bounds.center;
                var top = CopperSulfateStationarySceneBuilder.ProjectedBounds(tabletop.gameObject, center, table.rotation);
                item.transform.rotation = table.rotation * (longTool ? Quaternion.Euler(90, 0, 0) : Quaternion.identity);
                for (float x = top.min.x + 0.02f; x < top.max.x; x += 0.02f)
                for (float z = top.min.z + 0.02f; z < top.max.z; z += 0.02f)
                {
                    var bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item.gameObject, center, table.rotation);
                    item.transform.position += table.rotation * (new Vector3(x, top.max.y + 0.001f, z) - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                    bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item.gameObject, center, table.rotation);
                    if (bounds.min.x < top.min.x || bounds.max.x > top.max.x || bounds.min.z < top.min.z || bounds.max.z > top.max.z) continue;
                    float reach = Vector3.ProjectOnPlane(item.transform.position - stand, Vector3.up).magnitude;
                    if (reach > 0.6f) continue;
                    bool blocked = false;
                    foreach (var other in semantics)
                    {
                        if (other.transform == item.transform || !other.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled)) continue;
                        var otherBounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(other.gameObject, center, table.rotation);
                        if (bounds.min.x - 0.006f < otherBounds.max.x && bounds.max.x + 0.006f > otherBounds.min.x &&
                            bounds.min.z - 0.006f < otherBounds.max.z && bounds.max.z + 0.006f > otherBounds.min.z) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    float score = Vector3.Distance(item.transform.position, heat.Lamp.transform.position);
                    if (score >= best) continue;
                    best = score; chosen = item.transform.position; chosenRotation = item.transform.rotation;
                }
            }
            if (float.IsInfinity(best)) throw new InvalidOperationException("No reachable clear tabletop position for " + item.name);
            item.transform.SetPositionAndRotation(chosen, chosenRotation);
        }
    }

    private static XRGrabInteractable NewGrab(string name, string id, string label, string type, string function, string[] aliases)
    {
        var go = new GameObject(name); var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.mass = 0.07f;
        var grab = go.AddComponent<XRGrabInteractable>(); grab.useDynamicAttach = true; grab.matchAttachPosition = true; grab.matchAttachRotation = true; grab.throwOnDetach = false;
        var grip = new GameObject("Grip").transform; grip.SetParent(go.transform, false); grab.attachTransform = grip;
        go.AddComponent<SemanticObject>().ConfigureDetailed(id, label, type, "tool", function, type == "lighter" ? "red" : "black", "opaque",
            type == "lighter" ? "long_handle" : "hollow_cap", new[] { "metal" }, "none", "none", new[] { "ready" }, aliases, true, true);
        go.AddComponent<ExperimentGrabReturn>(); return grab;
    }
    private static void Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collider)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    private static Mesh Revolve(string name, Vector2[] profile)
    {
        const int sides = 48; var vertices = new Vector3[profile.Length * sides]; var triangles = new List<int>();
        for (int row = 0; row < profile.Length; row++) for (int i = 0; i < sides; i++)
        {
            float angle = i * 2f * Mathf.PI / sides; vertices[row * sides + i] = new Vector3(Mathf.Sin(angle) * profile[row].x, profile[row].y, Mathf.Cos(angle) * profile[row].x);
            if (row == profile.Length - 1) continue;
            int a = row * sides + i, b = row * sides + (i + 1) % sides, c = a + sides, d = b + sides;
            triangles.AddRange(new[] { a, b, c, b, d, c });
        }
        var mesh = new Mesh { name = name }; mesh.vertices = vertices; mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, CopperSulfateLiquidInstaller.AssetFolder + "/" + name.Replace(" ", "") + ".asset"); return mesh;
    }

    private static void Audit()
    {
        var scene = SceneManager.GetActiveScene();
        var semantics = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        var lines = new List<string>();
        foreach (var id in new[] { "spirit_lamp", "tripod_stand", "china_dish" })
        {
            var item = semantics.Single(s => s.StableId == id);
            lines.Add(id + " position=" + item.transform.position.ToString("F5") + " scale=" + item.transform.lossyScale);
            foreach (var mesh in item.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.GetComponent<Renderer>() != null))
                lines.Add(mesh.name + " center=" + mesh.GetComponent<Renderer>().bounds.center.ToString("F5") + " size=" + mesh.GetComponent<Renderer>().bounds.size.ToString("F5"));
        }
        File.WriteAllLines("Temp/CopperSulfateHeating.audit.txt", lines);
    }
}
