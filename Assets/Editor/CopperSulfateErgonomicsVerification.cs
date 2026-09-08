using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class CopperSulfateErgonomicsVerification
{
    [MenuItem("Tools/Stream Manager/Copper Sulfate/Verify Ergonomics And Start Pose")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before verification.");
        byte[] original = File.ReadAllBytes(CopperSulfateTransferInstaller.ScenePath);
        Scene activeScene = SceneManager.GetActiveScene();
        string path = "Assets/Editor/CopperErgonomicsVerification_" + Guid.NewGuid().ToString("N") + ".unity";
        var report = new List<string>();
        if (!AssetDatabase.CopyAsset(CopperSulfateTransferInstaller.ScenePath, path))
            throw new IOException("Could not copy verification scene.");
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            void Check(bool passed, string label)
            {
                if (!passed) throw new InvalidOperationException("FAIL: " + label);
                report.Add("PASS: " + label);
            }
            bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.0001f;
            var transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var stations = transforms.Where(t => new[] { "PreparationStation", "MixingStation", "FilteringStation" }.Contains(t.name)).ToArray();
            Check(stations.Length == 3, "Three separate workstations remain");
            var stationPoses = stations.Select(t => t.localToWorldMatrix).ToArray();
            var semantics = transforms.Select(t => t.GetComponent<SemanticObject>()).Where(s => s != null).ToArray();
            var untouched = semantics.Where(s => s.StableId != "forceps").Select(s => s.transform).ToArray();
            var objectPoses = untouched.Select(t => t.localToWorldMatrix).ToArray();
            var tool = semantics.Single(s => s.StableId == "forceps");
            var bounds = tool.GetComponent<MeshFilter>().sharedMesh.bounds;
            float Length() => tool.transform.TransformVector(Vector3.right * bounds.size.x).magnitude;
            Check(Mathf.Abs(Length() - 0.17f) < 0.0001f, "Forceps are 17 cm in world space");
            float bottom = tool.GetComponent<Renderer>().bounds.min.y;
            var scale = tool.transform.localScale;
            CopperSulfateErgonomicsInstaller.ConfigureScene(scene);
            CopperSulfateErgonomicsInstaller.ConfigureScene(scene);
            Check(Near(scale, tool.transform.localScale) && Mathf.Abs(bottom - tool.GetComponent<Renderer>().bounds.min.y) < 0.0001f,
                "Repeated setup preserves scale and tabletop contact");
            Check(stations.Select((t, i) => t.localToWorldMatrix == stationPoses[i]).All(v => v) &&
                untouched.Select((t, i) => t.localToWorldMatrix == objectPoses[i]).All(v => v),
                "Setup does not move or scale workstations or other apparatus");
            var grab = tool.GetComponent<XRGrabInteractable>();
            var tip = tool.transform.Find("CopperTransferTip");
            Check(grab.attachTransform.localPosition.x > bounds.center.x && tip.localPosition.x < bounds.center.x &&
                grab.useDynamicAttach && !grab.matchAttachPosition && grab.matchAttachRotation,
                "Grip is behind the working tip and keeps the initial orientation for either hand");
            var calibration = transforms.Select(t => t.GetComponent<PicoExperimentStartPose>()).Single(c => c != null);
            var origin = calibration.Origin;
            Check(Vector3.ProjectOnPlane(calibration.PreparationStart.position - tool.transform.position, Vector3.up).magnitude < 0.6f,
                "The starting head position is within 60 cm horizontally of the first tool");
            var floor = transforms.Select(t => t.GetComponent<Renderer>()).First(r =>
                r != null && r.gameObject.activeInHierarchy && r.name == "floor (1)");
            Check(Mathf.Abs(calibration.PreparationStart.position.y - floor.bounds.max.y) < 0.0001f,
                "The virtual tracking floor matches the visible laboratory floor");
            var camera = origin.Camera.transform;
            var hand = transforms.Single(t => t.name == "Right Hand Pinch Grab");
            Vector3 handPosition = hand.localPosition;
            Quaternion handRotation = hand.localRotation;
            camera.localPosition = new Vector3(0.6f, 1.65f, -0.4f);
            camera.localRotation = Quaternion.Euler(-12f, 47f, 4f);
            Vector3 localCamera = camera.localPosition;
            Quaternion localRotation = camera.localRotation;
            var before = origin.Origin.transform.localToWorldMatrix;
            Call(calibration, "Start");
            calibration.EditorTick(2f, false, true);
            Check(calibration.Status == "waiting" && origin.Origin.transform.localToWorldMatrix == before,
                "Startup waits without moving the rig when head tracking is unavailable");
            calibration.EditorTick(1f, true, false);
            Check(calibration.Status == "waiting", "Device-relative tracking cannot be mistaken for floor tracking");
            calibration.EditorTick(0.1f, true, true);
            calibration.EditorTick(0.1f, false, true);
            calibration.EditorTick(0.2f, true, true);
            Check(calibration.Status == "waiting", "Tracking interruption restarts the valid-pose delay");
            calibration.EditorTick(0.1f, true, true);
            Vector3 destination = calibration.PreparationStart.position;
            Check(calibration.Status == "ready" && Near(camera.position, destination + Vector3.up * 1.65f),
                "Valid tracking places the head above the start using its real floor-relative height");
            Check(Vector3.Angle(Vector3.ProjectOnPlane(camera.forward, Vector3.up), calibration.PreparationStart.forward) < 0.01f,
                "Only horizontal heading aligns to the preparation area");
            Check(Near(camera.localPosition, localCamera) && Quaternion.Angle(camera.localRotation, localRotation) < 0.01f &&
                Near(hand.localPosition, handPosition) && Quaternion.Angle(hand.localRotation, handRotation) < 0.01f &&
                Near(origin.Origin.transform.lossyScale, Vector3.one),
                "Head and hand local tracking poses and real-world scale are preserved");
            camera.localPosition += new Vector3(0.8f, 0f, 0.25f);
            Vector3 walked = camera.position;
            calibration.EditorTick(2f, true, true);
            Check(Near(camera.position, walked), "After startup, walking is not pulled back to the start");
            var receiver = transforms.Select(t => t.GetComponent<GuidanceDataChannelReceiver>()).Single(r => r != null);
            var sequence = receiver.GetComponent<PreparationSequenceController>();
            var detector = receiver.GetComponent<CopperSulfateTransferDetector>();
            sequence.StartSequence();
            int step = sequence.CurrentStepIndex;
            var stage = detector.Stage;
            var materialState = JsonUtility.ToJson(SemanticObjectSummary.From(detector.Beaker));
            Call(receiver, "OnMessage", Encoding.UTF8.GetBytes("{\"type\":\"recenter_experiment\"}"));
            Check(calibration.Status == "waiting", "Web recenter command reaches the configured calibrator");
            calibration.EditorTick(0.3f, true, true);
            Check(Near(camera.position, destination + Vector3.up * 1.65f) && sequence.CurrentStepIndex == step &&
                detector.Stage == stage && JsonUtility.ToJson(SemanticObjectSummary.From(detector.Beaker)) == materialState,
                "Explicit recenter restores stance without resetting the step or materials");
            calibration.RequestRecenter();
            calibration.EditorTick(11f, false, true);
            Vector3 timeoutPosition = camera.position;
            calibration.EditorTick(1f, true, true);
            Check(calibration.Status == "tracking_unavailable" && Near(camera.position, timeoutPosition),
                "An expired manual request cannot trigger a delayed teleport");
            camera.localPosition = new Vector3(0.2f, 1.2f, 0.7f);
            calibration.RequestRecenter();
            calibration.EditorTick(0.3f, true, true);
            Check(Near(camera.position, destination + Vector3.up * 1.2f), "A shorter tracked height is retained on retry");
            report.Add("Scope: copied editor scene; simulated headset poses, no PICO hardware or connected browser.");
        }
        catch (Exception exception)
        {
            report.Add(exception.ToString());
            throw;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(activeScene);
            AssetDatabase.DeleteAsset(path);
            report.Add(original.SequenceEqual(File.ReadAllBytes(CopperSulfateTransferInstaller.ScenePath))
                ? "PASS: Verification did not modify the source scene" : "FAIL: Source scene changed");
            File.WriteAllLines("Temp/CopperSulfateErgonomics.verification.txt", report);
        }
        Debug.Log("[CopperErgonomics] Verification passed.");
    }

    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
