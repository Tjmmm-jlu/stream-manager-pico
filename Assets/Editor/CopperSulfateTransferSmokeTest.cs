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

public static class CopperSulfateTransferSmokeTest
{
    private const string LogPath = "Temp/CopperSulfateTransfer.smoke.jsonl";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Run Transfer Verification")]
    public static void Run() => RunForScene(CopperSulfateTransferInstaller.ScenePath);

    public static void RunForScene(string scenePath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before running verification.");
        var report = new List<string>();
        byte[] sceneBefore = File.ReadAllBytes(scenePath);
        Scene originalScene = SceneManager.GetActiveScene();
        string testScenePath = "Assets/Editor/CopperTransferVerification_" + Guid.NewGuid().ToString("N") + ".unity";
        if (!AssetDatabase.CopyAsset(scenePath, testScenePath))
            throw new IOException("Could not create the temporary verification scene.");
        var preview = EditorSceneManager.OpenScene(testScenePath, OpenSceneMode.Additive);
        try
        {
            var detector = preview.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<CopperSulfateTransferDetector>(true)).Single();
            var sequence = detector.GetComponent<PreparationSequenceController>();
            var tracker = detector.GetComponent<ExperimentInteractionTracker>();
            var receiver = detector.GetComponent<GuidanceDataChannelReceiver>();
            var logger = detector.GetComponent<ExperimentLogger>();
            var state = detector.GetComponent<ExperimentStateController>();
            var reagent = preview.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<SemanticObject>(true))
                .Single(item => item.StableId == "copper_sulfate_crude");
            File.WriteAllText(LogPath, string.Empty);
            Set(logger, "_sequence", sequence);
            Set(logger, "_sessionId", "editor-transfer-verification");
            Set(logger, "_logFilePath", Path.GetFullPath(LogPath));
            Set(receiver, "_copperTransfer", detector);
            Set(receiver, "interactionTracker", tracker);
            Set(receiver, "objectRegistry", null);
            Call(logger, "OnEnable");
            Call(receiver, "OnEnable");
            detector.EditorTick(0f, false);
            Transform crystalParent = detector.CrystalVisual.parent;
            Vector3 crystalPosition = detector.CrystalVisual.localPosition;
            Vector3 crystalScale = detector.CrystalVisual.localScale;
            Vector3 toolPosition = detector.Forceps.transform.position;
            Vector3 cupPosition = detector.Beaker.transform.position;
            Quaternion cupRotation = detector.Beaker.transform.rotation;
            string initialBeaker = JsonUtility.ToJson(SemanticObjectSummary.From(detector.Beaker));
            RenderForcepsTip(detector);

            void Check(bool success, string label)
            {
                if (!success) throw new InvalidOperationException("FAIL: " + label);
                report.Add("PASS: " + label);
            }
            void Command(string type, int index = 0)
            {
                string json = "{\"type\":\"" + type +
                    "\",\"trialId\":\"transfer-smoke\",\"condition\":\"editor_debug\",\"stepIndex\":" + index + "}";
                Call(receiver, "OnMessage", Encoding.UTF8.GetBytes(json));
            }
            void Move(Vector3 point)
            {
                detector.Forceps.transform.position += point - detector.Tip.position;
            }
            void Tick(float duration, bool held)
            {
                for (int i = 0; i < Mathf.CeilToInt(duration / 0.05f); i++)
                    detector.EditorTick(0.05f, held);
            }
            void Pickup()
            {
                Move(detector.Pickup.position);
                Tick(0.6f, true);
            }
            int CompletedEvents() => File.ReadLines(LogPath).Count(line =>
                JsonUtility.FromJson<ExperimentLogRecord>(line).eventType == "copper_sulfate_transfer_completed");

            Command("start_trial"); Command("complete_trial");
            Check(state.CurrentState == ExperimentTrialState.Completed && !sequence.IsRunning,
                "Standalone target trials can still complete before a sequence starts");
            Command("start_sequence");
            Check(sequence.CurrentStep.id == "forceps_transfer" &&
                sequence.CurrentStep.target == detector.Forceps.gameObject &&
                sequence.GuidanceManager.CurrentTarget == null,
                "First step targets forceps without automatic VR guidance");
            Command("next_step"); Command("complete_step");
            Command("show_step", 1); Command("complete_sequence"); Command("complete_trial");
            Check(sequence.IsRunning && sequence.CurrentStepIndex == 0 &&
                state.CurrentState != ExperimentTrialState.Completed,
                "All web advance/completion commands respect the action gate");
            Call(tracker, "EvaluateReleasedObject", detector.Forceps);
            Check(sequence.CurrentStepIndex == 0,
                "Releasing the correct tool cannot complete the transfer step");

            Move(detector.Pickup.position + Vector3.up);
            Tick(1f, true);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup,
                "Holding forceps far from reagent does not pick up");
            Move(detector.Pickup.position);
            Tick(1f, false);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup,
                "Proximity without a grab does not pick up");
            Tick(0.2f, true);
            Move(detector.Pickup.position + Vector3.up);
            Tick(0.1f, true);
            Move(detector.Pickup.position);
            Tick(0.25f, true);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup,
                "Interrupted proximity resets pickup dwell");
            Tick(0.3f, true);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.Carrying &&
                detector.CrystalVisual.IsChildOf(detector.Tip) &&
                detector.CrystalVisual.GetComponentsInChildren<Collider>().All(item => !item.enabled),
                "Pickup moves existing crystal visuals to the tip and disables their colliders");
            Tick(0.05f, false);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup &&
                detector.CrystalVisual.parent == crystalParent &&
                Vector3.Distance(detector.CrystalVisual.localPosition, crystalPosition) < 0.0001f &&
                !reagent.StateTags.Contains("held_by_forceps"),
                "Releasing forceps returns crystals and clears carrying state");

            Pickup();
            Move(detector.Mouth.position - detector.Mouth.up * 0.15f);
            Tick(1f, true);
            Check(sequence.CurrentStepIndex == 0,
                "Approaching the beaker from below cannot transfer");
            Move(detector.Mouth.position + Vector3.right);
            Tick(1f, true);
            Check(sequence.CurrentStepIndex == 0,
                "Wrong destination does not complete or consume crystals");
            detector.Beaker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Move(detector.Mouth.position);
            Tick(1f, true);
            Check(sequence.CurrentStepIndex == 0, "A sideways beaker cannot receive crystals");
            detector.Beaker.transform.rotation = cupRotation;
            detector.Beaker.transform.position += Vector3.right * 0.2f;
            Move(detector.Mouth.position + detector.Mouth.up * 0.025f);
            Tick(0.8f, true);
            Check(detector.Stage == CopperSulfateTransferDetector.TransferStage.Completed &&
                sequence.CurrentStep.id == "graduated_cylinder" && CompletedEvents() == 1,
                "A moved beaker receives crystals and advances exactly once to measuring");
            Check(detector.Beaker.Contents == "copper_sulfate" &&
                detector.Beaker.StateTags.Contains("contains_copper_sulfate") &&
                !detector.Beaker.StateTags.Contains("empty") &&
                !detector.Beaker.Aliases.Contains("\u7a7a\u70e7\u676f") &&
                reagent.StateTags.Contains("transferred") &&
                detector.CrystalVisual.IsChildOf(detector.Beaker.transform),
                "Destination semantics, source semantics and crystal location agree");
            Tick(2f, true);
            Check(CompletedEvents() == 1 && sequence.CurrentStepIndex == 1,
                "Repeated frames cannot duplicate the completion event");
            var completion = File.ReadLines(LogPath).Select(JsonUtility.FromJson<ExperimentLogRecord>)
                .Single(item => item.eventType == "copper_sulfate_transfer_completed");
            Check(completion.stepId == "forceps_transfer" && completion.stepIndex == 0 &&
                completion.objectId == "beaker" && completion.trialId == "transfer-smoke",
                "Completion log includes the original step, trial and destination");
            Check(!sequence.PreviousStep(), "Replaying a consumed action requires a reset");
            Call(tracker, "EvaluateReleasedObject", sequence.CurrentStep.target.GetComponent<SemanticObject>());
            if (detector.GetComponent<CopperSulfateMixingDetector>() != null)
                Check(sequence.CurrentStep.id == "graduated_cylinder", "Releasing the cylinder cannot bypass measurement");
            else
                Check(sequence.CurrentStep.id == "water_source", "Later legacy selection steps still advance");

            Command("reset_sequence");
            Check(!sequence.IsRunning && sequence.CurrentStepIndex == -1 &&
                detector.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup &&
                Vector3.Distance(detector.Forceps.transform.position, toolPosition) < 0.0001f &&
                Vector3.Distance(detector.Beaker.transform.position, cupPosition) < 0.0001f &&
                Vector3.Distance(detector.CrystalVisual.localScale, crystalScale) < 0.0001f &&
                JsonUtility.ToJson(SemanticObjectSummary.From(detector.Beaker)) == initialBeaker,
                "Reset restores poses, scale, material state, aliases and sequence");
            Command("start_sequence"); Pickup();
            Command("abort_trial");
            Check(!sequence.IsRunning && detector.CrystalVisual.parent == crystalParent &&
                state.CurrentState == ExperimentTrialState.Aborted,
                "Aborting while carrying restores the stage");
            Command("start_sequence"); Pickup();
            Move(detector.Mouth.position + detector.Mouth.up * 0.025f);
            Tick(0.8f, true);
            Check(CompletedEvents() == 2 && sequence.CurrentStepIndex == 1,
                "A new run completes successfully after abort/reset");
            sequence.ResetSequence();
            Call(receiver, "OnDisable");
            Call(logger, "OnDisable");
            report.Add("Scope: temporary editor scene, real scene references and web-command dispatcher; simulated hand-held input; no connected browser or headset.");
        }
        catch (Exception exception)
        {
            report.Add(exception.ToString());
            throw;
        }
        finally
        {
            EditorSceneManager.CloseScene(preview, true);
            SceneManager.SetActiveScene(originalScene);
            AssetDatabase.DeleteAsset(testScenePath);
            report.Add(sceneBefore.SequenceEqual(File.ReadAllBytes(scenePath))
                ? "PASS: Verification did not rewrite the scene asset" : "FAIL: Scene asset changed");
            File.WriteAllLines("Temp/CopperSulfateTransfer.verification.txt", report);
        }
        Debug.Log("[CopperTransfer] Editor verification passed: " + report.Count + " checks/notes.");
    }

    private static void Call(object target, string method, params object[] args)
    {
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, args);
    }
    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    }

    private static void RenderForcepsTip(CopperSulfateTransferDetector detector)
    {
        var preview = new PreviewRenderUtility();
        Material marker = new Material(Shader.Find("Standard")) { color = Color.red };
        Texture2D image = null;
        try
        {
            Mesh mesh = detector.Forceps.GetComponent<MeshFilter>().sharedMesh;
            Material material = detector.Forceps.GetComponent<Renderer>().sharedMaterial;
            Bounds bounds = mesh.bounds;
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = bounds.size.x * 0.24f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 3f;
            preview.camera.transform.position = bounds.center + Vector3.up;
            preview.camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.8f, 0.87f, 0.88f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45f, 30f, 0f);
            preview.lights[1].intensity = 1f;
            preview.BeginStaticPreview(new Rect(0, 0, 1000, 360));
            preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
            preview.DrawMesh(Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
                Matrix4x4.TRS(detector.Tip.localPosition + Vector3.up * 0.01f,
                    Quaternion.identity, Vector3.one * 0.008f), marker, 0);
            preview.Render();
            image = preview.EndStaticPreview();
            File.WriteAllBytes("Temp/CopperSulfateTransfer.forceps-tip.png", image.EncodeToPNG());
        }
        finally
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(marker);
            preview.Cleanup();
        }
    }
}
