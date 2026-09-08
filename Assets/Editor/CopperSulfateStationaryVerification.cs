using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class CopperSulfateStationaryVerification
{
    [MenuItem("Tools/Stream Manager/Copper Sulfate/Verify Stationary Variant")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        string sourcePath = CopperSulfateTransferInstaller.ScenePath;
        string compactPath = CopperSulfateStationarySceneBuilder.ScenePath;
        byte[] originalBytes = File.ReadAllBytes(sourcePath);
        byte[] compactBytes = File.ReadAllBytes(compactPath);
        Scene active = SceneManager.GetActiveScene();
        string tempPath = "Assets/Editor/CopperStationaryVerification_" + Guid.NewGuid().ToString("N") + ".unity";
        var report = new List<string>();
        if (!AssetDatabase.CopyAsset(compactPath, tempPath)) throw new IOException("Could not copy verification scene.");
        Scene scene = EditorSceneManager.OpenScene(tempPath, OpenSceneMode.Additive);
        Scene original = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
        try
        {
            void Check(bool success, string label)
            {
                if (!success) throw new InvalidOperationException("FAIL: " + label);
                report.Add("PASS: " + label);
            }
            var roots = scene.GetRootGameObjects();
            bool seatedLiquids = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Any();
            var oldRoots = original.GetRootGameObjects();
            var ids = CopperSulfateStationarySceneBuilder.RequiredIds;
            var apparatus = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true))
                .Where(s => ids.Contains(s.StableId)).ToArray();
            var oldApparatus = oldRoots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true))
                .Where(s => ids.Contains(s.StableId)).ToDictionary(s => s.StableId);
            Check(apparatus.Length == 9 && apparatus.All(s => s.gameObject.activeInHierarchy), "All nine experiment objects remain active");
            Check(apparatus.All(s => Vector3.Distance(s.transform.lossyScale, oldApparatus[s.StableId].transform.lossyScale) < 0.0001f),
                "Furniture resizing does not scale apparatus");
            Check(apparatus.All(s => s.GetComponent<XRGrabInteractable>() != null && s.GetComponent<XRGrabInteractable>().enabled &&
                s.GetComponentsInChildren<Collider>().Any(c => c.enabled)), "All nine objects keep enabled grab components and colliders");
            var calibration = roots.SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>(true)).Single();
            Vector3 stand = calibration.PreparationStart.position;
            var tables = roots.Where(r => new[] { "PreparationStation", "MixingStation", "FilteringStation" }.Contains(r.name)).ToArray();
            Check(tables.Length == 3 && tables.All(t => t.activeSelf), "Three independent tables remain");
            var topPolygons = new List<Vector2[]>();
            var objectPolygons = new List<Vector2[]>();
            foreach (var table in tables)
            {
                Renderer top = CopperSulfateStationarySceneBuilder.FindTabletop(table.transform);
                Quaternion rotation = table.transform.rotation;
                Bounds bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(top.gameObject, Vector3.zero, rotation);
                var originalTop = CopperSulfateStationarySceneBuilder.FindTabletop(oldRoots.Single(r => r.name == table.name).transform);
                float expectedTop = seatedLiquids ? stand.y + 0.74f : originalTop.bounds.max.y;
                Check(Mathf.Abs(top.bounds.max.y - expectedTop) < 0.0001f &&
                    bounds.size.x <= 0.651f && bounds.size.z <= 0.501f,
                    table.name + " has compact dimensions and the configured tabletop height");
                Vector3 localStand = Quaternion.Inverse(rotation) * stand;
                Vector2 closest = new Vector2(Mathf.Clamp(localStand.x, bounds.min.x, bounds.max.x),
                    Mathf.Clamp(localStand.z, bounds.min.z, bounds.max.z));
                Check(Vector2.Distance(closest, new Vector2(localStand.x, localStand.z)) >= 0.25f,
                    table.name + " leaves at least 25 cm horizontal body clearance");
                topPolygons.Add(Polygon(bounds, rotation));
            }
            Check(AllSeparate(topPolygons), "The three tabletops do not intersect");
            foreach (var item in apparatus)
            {
                bool supported = false;
                foreach (var table in tables)
                {
                    Renderer top = CopperSulfateStationarySceneBuilder.FindTabletop(table.transform);
                    Quaternion rotation = table.transform.rotation;
                    Bounds surface = CopperSulfateStationarySceneBuilder.ProjectedBounds(top.gameObject, Vector3.zero, rotation);
                    Bounds bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item.gameObject, Vector3.zero, rotation);
                    if (bounds.min.x < surface.min.x - 0.001f || bounds.max.x > surface.max.x + 0.001f ||
                        bounds.min.z < surface.min.z - 0.001f || bounds.max.z > surface.max.z + 0.001f) continue;
                    supported = bounds.min.y >= surface.max.y && bounds.min.y - surface.max.y < 0.005f;
                    if (!supported) continue;
                    Vector3 center = rotation * bounds.center;
                    float radius = Vector3.ProjectOnPlane(center - stand, Vector3.up).magnitude;
                    Check(radius <= 0.605f && center.z >= stand.z,
                        item.StableId + " is in front of the stance within about 60 cm horizontally (" + radius.ToString("F3") + " m)");
                    objectPolygons.Add(Polygon(bounds, rotation));
                    break;
                }
                Check(supported, item.StableId + " rests fully on a tabletop without sinking or overhanging");
            }
            Check(AllSeparate(objectPolygons), "Apparatus footprints do not overlap at rest");
            Physics.SyncTransforms();
            for (int i = 0; i < apparatus.Length; i++)
                for (int j = i + 1; j < apparatus.Length; j++)
                    if (Penetrates(apparatus[i].gameObject, apparatus[j].gameObject))
                        throw new InvalidOperationException("Collider overlap: " + apparatus[i].StableId + " / " + apparatus[j].StableId);
            Check(true, "Apparatus physics colliders do not penetrate each other");
            foreach (var item in apparatus)
                foreach (var table in tables)
                    if (Penetrates(item.gameObject, table))
                        throw new InvalidOperationException("Apparatus penetrates furniture: " + item.StableId + " / " + table.name);
            Check(true, "Apparatus physics colliders do not penetrate the tables");
            var tool = apparatus.Single(a => a.StableId == "forceps");
            float length = tool.transform.TransformVector(Vector3.right * tool.GetComponent<MeshFilter>().sharedMesh.bounds.size.x).magnitude;
            Check(Mathf.Abs(length - 0.17f) < 0.0001f && tool.GetComponent<XRGrabInteractable>().attachTransform.name == "ForcepsGrip",
                "Forceps retain their 17 cm length and grip anchor");
            var detector = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateTransferDetector>()).Single();
            var sequence = detector.GetComponent<PreparationSequenceController>();
            Check(sequence.StepCount == (seatedLiquids ? 7 : 8) && sequence.Steps[0].completionAction == CopperSulfateTransferDetector.ActionId &&
                detector.Forceps.gameObject.scene == scene && detector.Beaker.gameObject.scene == scene,
                "Sequence and transfer references belong to the copied scene");
            Check(Vector3.ProjectOnPlane(detector.Pickup.position - stand, Vector3.up).magnitude <= 0.605f &&
                Vector3.ProjectOnPlane(detector.Mouth.position - stand, Vector3.up).magnitude <= 0.605f,
                "Crystal pickup and beaker mouth are reachable from the same stance");
            Check(PicoHandPinchGrabBootstrap.Configure(scene), "Hand-pinch configuration works in the new variant");
            var origin = calibration.Origin;
            Check(Vector3.Distance(origin.transform.lossyScale, Vector3.one) < 0.0001f,
                "XR Origin keeps real-world scale");
            var camera = origin.Camera.transform;
            camera.localPosition = new Vector3(0.45f, 1.65f, -0.3f);
            camera.localRotation = Quaternion.Euler(8f, -35f, 0f);
            calibration.RequestRecenter();
            calibration.EditorTick(0.3f, true, true);
            Check(calibration.Status == "ready" && Vector3.Distance(camera.position, stand + Vector3.up * 1.65f) < 0.0001f,
                "Calibration places the tracked head at the common stance while retaining real height");
            Matrix4x4 originPose = origin.transform.localToWorldMatrix;
            camera.localRotation = Quaternion.Euler(25f, 65f, 0f);
            calibration.EditorTick(1f, true, true);
            Check(origin.transform.localToWorldMatrix == originPose, "Turning the head does not move the user or tables");
            Check(EditorBuildSettings.scenes.Count(s => s.enabled) == 1 &&
                EditorBuildSettings.scenes.Single(s => s.enabled).path == compactPath,
                "Build Settings select only the new stationary scene");
            report.Add("Scope: saved scene geometry and simulated tracking/grabbing; comfortable reach still requires headset confirmation.");
        }
        catch (Exception exception)
        {
            report.Add(exception.ToString());
            throw;
        }
        finally
        {
            EditorSceneManager.CloseScene(original, true);
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(active);
            AssetDatabase.DeleteAsset(tempPath);
            report.Add(originalBytes.SequenceEqual(File.ReadAllBytes(sourcePath)) && compactBytes.SequenceEqual(File.ReadAllBytes(compactPath))
                ? "PASS: Both original and stationary scene assets remained unchanged during verification" : "FAIL: A scene asset changed");
            File.WriteAllLines("Temp/CopperSulfateStationary.verification.txt", report);
        }
        CopperSulfateTransferSmokeTest.RunForScene(compactPath);
        File.Copy("Temp/CopperSulfateTransfer.verification.txt", "Temp/CopperSulfateStationary.transfer-verification.txt", true);
        Debug.Log("[CopperStationary] Geometry, tracking, and transfer verification passed.");
    }

    private static Vector2[] Polygon(Bounds bounds, Quaternion rotation) => new[]
    {
        new Vector3(bounds.min.x, 0f, bounds.min.z), new Vector3(bounds.max.x, 0f, bounds.min.z),
        new Vector3(bounds.max.x, 0f, bounds.max.z), new Vector3(bounds.min.x, 0f, bounds.max.z)
    }.Select(point => rotation * point).Select(point => new Vector2(point.x, point.z)).ToArray();

    private static bool AllSeparate(List<Vector2[]> polygons)
    {
        for (int i = 0; i < polygons.Count; i++)
            for (int j = i + 1; j < polygons.Count; j++)
                if (Intersect(polygons[i], polygons[j])) return false;
        return true;
    }

    private static bool Penetrates(GameObject a, GameObject b)
    {
        foreach (var ca in a.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
            foreach (var cb in b.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
                if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation,
                    cb, cb.transform.position, cb.transform.rotation, out _, out float depth) && depth > 0.001f)
                    return true;
        return false;
    }

    private static bool Intersect(Vector2[] a, Vector2[] b)
    {
        // Separating-axis test for the oriented table and apparatus footprints.
        foreach (var polygon in new[] { a, b })
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                float[] pa = a.Select(p => Vector2.Dot(p, axis)).ToArray();
                float[] pb = b.Select(p => Vector2.Dot(p, axis)).ToArray();
                if (pa.Max() <= pb.Min() + 0.001f || pb.Max() <= pa.Min() + 0.001f) return false;
            }
        return true;
    }
}
