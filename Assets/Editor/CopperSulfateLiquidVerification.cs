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
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class CopperSulfateLiquidVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        string path = CopperSulfateLiquidInstaller.ScenePath;
        byte[] before = File.ReadAllBytes(path);
        string copy = "Assets/Editor/LiquidVerification_" + Guid.NewGuid().ToString("N") + ".unity";
        var active = SceneManager.GetActiveScene();
        var report = new List<string>();
        if (!AssetDatabase.CopyAsset(path, copy)) throw new IOException("Could not copy scene.");
        Scene scene = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
        try
        {
            void Check(bool value, string name)
            {
                if (!value) throw new InvalidOperationException("FAIL: " + name);
                report.Add("PASS: " + name);
            }
            var roots = scene.GetRootGameObjects();
            var mix = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Single();
            var sequence = mix.GetComponent<PreparationSequenceController>();
            var transfer = mix.GetComponent<CopperSulfateTransferDetector>();
            var receiver = mix.GetComponent<GuidanceDataChannelReceiver>();
            var state = mix.GetComponent<ExperimentStateController>();
            var pour = mix.GetComponent<LiquidPourInteractor>();
            var filtration = mix.GetComponent<CopperSulfateFiltrationDetector>();
            var calibration = roots.SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>()).Single();
            var floor = roots.Single(r => r.name == "floor (1)").GetComponent<Renderer>().bounds.max.y;
            foreach (string name in new[] { "PreparationStation", "MixingStation", "FilteringStation" })
                Check(Mathf.Abs(CopperSulfateStationarySceneBuilder.FindTabletop(roots.Single(r => r.name == name).transform).bounds.max.y - floor - 0.74f) < 0.001f,
                    name + " tabletop is 74 cm above floor");
            Check(calibration.Origin.transform.lossyScale == Vector3.one && calibration.Origin.CameraYOffset == 0f,
                "Seated layout preserves tracked eye height and real-world rig scale");
            Check(mix.Cylinder.VolumeMl == 0f && mix.Beaker.VolumeMl == 0f && mix.Source.VolumeMl == 400f,
                "Empty cylinder and beaker; source starts with 400 mL");
            Check(sequence.StepCount == 7 && sequence.Steps[1].completionAction == CopperSulfateMixingDetector.MeasureAction &&
                sequence.Steps[2].completionAction == CopperSulfateMixingDetector.MixAction, "Measurement and mixing replace the three legacy selection steps");
            Check(mix.Rod.GetComponent<SemanticObject>().Matches("\u6405\u62cc\u68d2"), "Rod is registered with Chinese semantic aliases");
            Check(mix.Cylinder.Grab.colliders.All(c => c.enabled) && mix.Beaker.Grab.colliders.Count == 17,
                "Grab volumes use enabled hollow vessel walls and base");
            Physics.SyncTransforms();
            Render(scene, "Temp/CopperSulfateLiquids.seated.png", new Vector3(4.25f, floor + 1.13f, 6.8f), Quaternion.Euler(38f, 0f, 0f));
            Render(scene, "Temp/CopperSulfateLiquids.measurement.png", mix.Cylinder.Mouth + new Vector3(0f, 0.03f, -0.35f), Quaternion.Euler(15f, 0f, 0f));
            Set(receiver, "_copperTransfer", transfer); Set(receiver, "_copperMixing", mix);
            Set(receiver, "_copperFiltration", filtration);
            Set(receiver, "objectRegistry", null);
            Invoke(receiver, "OnEnable");
            mix.Tick(0f); transfer.EditorTick(0f, false);
            if (filtration != null) filtration.Tick(0f);
            if (filtration != null) CopperSulfateFiltrationVerification.CheckLayout(scene, filtration, report);
            Check(mix.Source.GetComponent<SemanticObject>().Contents == "water" &&
                mix.Cylinder.GetComponent<SemanticObject>().StateTags.Contains("empty"),
                "Initial semantic catalog agrees with filled source and empty cylinder");
            void Command(string type) => Invoke(receiver, "OnMessage", Encoding.UTF8.GetBytes(
                "{\"type\":\"" + type + "\",\"trialId\":\"liquid-verification\",\"condition\":\"editor_debug\"}"));
            void Tick(float time)
            {
                for (int i = 0; i < Mathf.CeilToInt(time / 0.02f); i++) { Physics.SyncTransforms(); mix.Tick(0.02f); }
            }
            var managerObject = new GameObject("Liquid Verification Interaction Manager");
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            var manager = managerObject.AddComponent<XRInteractionManager>(); Invoke(manager, "Awake");
            XRDirectInteractor Hand(string name, InteractorHandedness handedness)
            {
                var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<SphereCollider>().isTrigger = true;
                var hand = go.AddComponent<XRDirectInteractor>(); hand.interactionManager = manager; hand.handedness = handedness;
                hand.attachTransform = go.transform; Invoke(hand, "Awake");
                return hand;
            }
            var left = Hand("Verification Left", InteractorHandedness.Left);
            var right = Hand("Verification Right", InteractorHandedness.Right);
            foreach (var grab in new[] { mix.Source.Grab, mix.Cylinder.Grab, mix.Beaker.Grab, mix.Rod })
            {
                grab.interactionManager = manager; Invoke(grab, "Awake");
            }
            void Release(XRDirectInteractor hand)
            {
                foreach (var selected in hand.interactablesSelected.ToArray()) manager.SelectExit(hand, selected);
            }
            void Hold(XRDirectInteractor hand, XRGrabInteractable grab)
            {
                Release(hand); hand.transform.position = grab.attachTransform.position;
                manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
            }
            void Align(LiquidContainer source, LiquidContainer destination, float tilt = 65f)
            {
                destination.transform.SetPositionAndRotation(new Vector3(-40f, 2f, -40f), Quaternion.identity);
                source.transform.rotation = Quaternion.Euler(0f, 0f, tilt);
                source.transform.position += destination.Mouth + Vector3.up * 0.12f - source.LowestLip();
                Physics.SyncTransforms();
            }
            Hold(left, mix.Cylinder.Grab); Hold(right, mix.Source.Grab);
            Align(mix.Source, mix.Cylinder); Tick(1f);
            Check(mix.Cylinder.VolumeMl == 0f, "Idle experiment cannot pour or advance");
            Release(left); Release(right);
            Command("start_sequence");
            transfer.Forceps.transform.position += transfer.Pickup.position - transfer.Tip.position;
            transfer.EditorTick(0.6f, true);
            transfer.Forceps.transform.position += transfer.Mouth.position + Vector3.up * 0.025f - transfer.Tip.position;
            transfer.EditorTick(0.8f, true);
            Check(sequence.CurrentStepIndex == 1 && transfer.Stage == CopperSulfateTransferDetector.TransferStage.Completed,
                "Forceps transfer still advances to measurement");
            Command("next_step"); Command("complete_trial");
            Check(sequence.CurrentStepIndex == 1 && state.CurrentState != ExperimentTrialState.Completed,
                "Web shortcuts cannot bypass measurement");
            Hold(right, mix.Source.Grab); Align(mix.Source, mix.Cylinder); Tick(1f);
            Check(mix.Cylinder.VolumeMl == 0f, "One-handed pouring is rejected");
            Hold(left, mix.Cylinder.Grab); left.handedness = InteractorHandedness.Right; Tick(1f);
            Check(mix.Cylinder.VolumeMl == 0f, "Two interactors reporting the same hand are rejected");
            left.handedness = InteractorHandedness.Left;
            Check(LiquidPourInteractor.OppositeHands(mix.Source.Grab, mix.Cylinder.Grab), "Real XRI selections identify opposite hands");
            Align(mix.Source, mix.Cylinder, 0f); Tick(1f);
            Check(mix.Cylinder.VolumeMl == 0f, "Upright source does not pour");
            Align(mix.Source, mix.Cylinder); mix.Source.transform.position += Vector3.right * 0.4f; Tick(0.5f);
            Check(mix.SpilledMl > 1f && mix.Cylinder.VolumeMl == 0f && Mathf.Abs(mix.Source.VolumeMl + mix.SpilledMl - 400f) < 0.01f,
                "Missed stream is recorded as spill and conserves volume");
            Command("retry_liquid_stage");
            Check(sequence.CurrentStepIndex == 1 && transfer.CrystalVisual.IsChildOf(mix.Beaker.transform),
                "Retry retains the successful crystal transfer");
            Hold(left, mix.Cylinder.Grab); Hold(right, mix.Source.Grab);
            mix.Cylinder.SetVolume(120f); mix.Source.transform.rotation = Quaternion.identity; Tick(1f);
            Check(sequence.CurrentStepIndex == 1 && mix.Status == "overfilled", "Excess measurement cannot complete");
            Command("retry_liquid_stage"); Hold(left, mix.Cylinder.Grab); Hold(right, mix.Source.Grab);
            Align(mix.Source, mix.Cylinder);
            for (int i = 0; i < 600 && mix.Cylinder.VolumeMl < 100f; i++) mix.Tick(0.02f);
            Check(mix.Cylinder.VolumeMl >= 100f && mix.Cylinder.VolumeMl < 101f, "Tilted pouring reaches 100 mL continuously");
            mix.Cylinder.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            mix.Cylinder.RefreshVisual();
            var liquidMesh = mix.Cylinder.transform.Find("Measured Liquid").GetComponent<MeshFilter>().sharedMesh;
            Check(liquidMesh.vertexCount > 20 && liquidMesh.vertices.All(v =>
                mix.Cylinder.transform.TransformPoint(v).y <= mix.Cylinder.SurfaceWorldY + 0.0001f),
                "Tilted liquid mesh stays inside a horizontal surface plane");
            mix.Cylinder.transform.rotation = Quaternion.identity;
            Check(sequence.CurrentStepIndex == 1, "Passing target during active pouring does not advance");
            mix.Source.SetVolume(mix.Source.VolumeMl - (110f - mix.Cylinder.VolumeMl));
            mix.Cylinder.SetVolume(110f);
            mix.Source.transform.rotation = Quaternion.identity; Tick(0.6f);
            Check(sequence.CurrentStepIndex == 2 && mix.ToleranceMl == 10f, "110 mL upper boundary is accepted after upright two-hand settling");
            Check(Mathf.Abs(mix.Source.VolumeMl + mix.Cylinder.VolumeMl - 400f) < 0.01f,
                "Source and measured volume remain conserved");
            Command("next_step");
            Check(sequence.CurrentStepIndex == 2, "Mixing action cannot be skipped");
            Release(right); Hold(right, mix.Beaker.Grab); Align(mix.Cylinder, mix.Beaker, 95f);
            mix.Beaker.transform.position += Vector3.right * 0.3f; Tick(0.3f);
            Check(mix.Beaker.VolumeMl == 0f, "Moving the receiver away makes the stream miss its new mouth position");
            mix.Beaker.SetVolume(89f); mix.Cylinder.SetVolume(0f); Tick(0.6f);
            Check(mix.Stage == CopperSulfateMixingDetector.MixingStage.AddingWater, "Empty cylinder with only 89 mL received cannot enable stirring");
            mix.Beaker.SetVolume(90f); Tick(0.6f);
            Check(mix.Stage == CopperSulfateMixingDetector.MixingStage.Stirring, "90 mL lower boundary enables stirring");
            Command("retry_liquid_stage");
            Check(mix.Cylinder.VolumeMl == 100f && mix.Source.VolumeMl == 300f && mix.Beaker.VolumeMl == 0f && sequence.CurrentStepIndex == 2,
                "Mixing retry restores a conserved 400 mL baseline without repeating measurement");
            Hold(left, mix.Cylinder.Grab); Hold(right, mix.Beaker.Grab); Align(mix.Cylinder, mix.Beaker, 95f);
            for (int i = 0; i < 600 && mix.Cylinder.VolumeMl > 0.001f; i++) mix.Tick(0.02f);
            mix.Cylinder.transform.rotation = Quaternion.identity; Tick(0.6f);
            Check(mix.Stage == CopperSulfateMixingDetector.MixingStage.Stirring && Mathf.Abs(mix.Beaker.VolumeMl - 100f) <= mix.ToleranceMl,
                "Measured water reaches the held beaker and enables stirring");
            Hold(left, mix.Rod);
            Vector3 StirPoint(float angle) => mix.Beaker.transform.TransformPoint(mix.Beaker.Bottom +
                new Vector3(Mathf.Cos(angle) * mix.Beaker.Radius * 0.4f, mix.Beaker.Height * 0.2f, Mathf.Sin(angle) * mix.Beaker.Radius * 0.4f));
            void MoveTip(Vector3 position) { mix.Rod.transform.position += position - mix.RodTip.position; }
            MoveTip(mix.Beaker.Mouth + Vector3.up * 0.1f); Tick(1f);
            Check(mix.Progress == 0f, "Stirring outside liquid cannot dissolve crystals");
            MoveTip(StirPoint(0f)); Tick(1f);
            Check(mix.Progress == 0f, "Stationary immersed rod does not count as stirring");
            for (int i = 0; i < 50; i++)
            {
                mix.Beaker.transform.position += Vector3.right * 0.001f;
                mix.Rod.transform.position += Vector3.right * 0.001f;
                mix.Tick(0.02f);
            }
            Check(mix.Progress == 0f, "Moving both hands together does not count as relative stirring");
            Vector3 startPoint = StirPoint(0f);
            for (int i = 0; i < 50; i++) { MoveTip(startPoint + Vector3.right * (i % 2 * 0.001f)); mix.Tick(0.02f); }
            Check(mix.Progress == 0f, "Sub-millimeter tracking jitter does not dissolve crystals");
            Release(right);
            for (int i = 0; i < 50; i++) { MoveTip(StirPoint(i * 0.12f)); mix.Tick(0.02f); }
            Check(mix.Progress == 0f, "Rod-only stirring fails the two-hand requirement");
            Hold(right, mix.Beaker.Grab);
            for (int i = 0; i < 350 && mix.Progress < 1f; i++) { MoveTip(StirPoint(i * 0.12f)); mix.Tick(0.02f); }
            Check(mix.Progress == 1f && sequence.CurrentStepIndex == 3, "Relative circular stirring dissolves and advances exactly once");
            Check(mix.Beaker.GetComponent<SemanticObject>().Contents == "copper_sulfate_solution" &&
                transfer.CrystalVisual.Cast<Transform>().All(t => t.localScale == Vector3.zero),
                "Dissolution updates solution semantics and consumes blue crystals");
            Tick(1f); Check(sequence.CurrentStepIndex == 3, "Completed mixing cannot repeatedly advance");
            mix.Beaker.RefreshVisual();
            Render(scene, "Temp/CopperSulfateLiquids.solution.png", mix.Beaker.Mouth + new Vector3(0f, 0.14f, -0.34f), Quaternion.Euler(32f, 0f, 0f));
            if (filtration != null) CopperSulfateFiltrationVerification.Run(scene, mix, manager, left, right, report);
            Command("reset_sequence");
            if (filtration != null)
                Check(filtration.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Waiting && !filtration.Attached &&
                    filtration.Funnel.enabled && filtration.Receiver.VolumeMl == 0f && filtration.Dish.VolumeMl == 0f &&
                    !filtration.TrappedResidue.gameObject.activeSelf && filtration.SpilledMl == 0f,
                    "Full reset restores the unassembled filter, empty receiving vessels and residue");
            Check(mix.Cylinder.VolumeMl == 0f && mix.Beaker.VolumeMl == 0f && mix.Source.VolumeMl == 400f &&
                mix.Progress == 0f && mix.SpilledMl == 0f && transfer.Stage == CopperSulfateTransferDetector.TransferStage.WaitingForPickup,
                "Full reset restores liquid, crystal, spill and sequence states");
            Check(!left.hasSelection && !right.hasSelection, "Reset cancels actual XRI hand selections");
            Check(transfer.CrystalVisual.Cast<Transform>().All(t => t.localScale.sqrMagnitude > 0f), "Reset restores dissolved crystal scales");
            Command("start_sequence");
            transfer.Forceps.transform.position += transfer.Pickup.position - transfer.Tip.position;
            transfer.EditorTick(0.6f, true);
            transfer.Forceps.transform.position += transfer.Mouth.position + Vector3.up * 0.025f - transfer.Tip.position;
            transfer.EditorTick(0.8f, true);
            Hold(left, mix.Cylinder.Grab); Hold(right, mix.Source.Grab); Align(mix.Source, mix.Cylinder);
            for (int i = 0; i < 600 && mix.Cylinder.VolumeMl < 99.8f; i++) mix.Tick(0.02f);
            mix.Source.SetVolume(mix.Source.VolumeMl + mix.Cylinder.VolumeMl - 99.8f);
            mix.Cylinder.SetVolume(99.8f);
            Release(right); Tick(0.2f);
            Check(sequence.CurrentStepIndex == 1, "99.8 mL still needs a stable reading after releasing the source");
            Release(left); Tick(0.6f);
            Check(sequence.CurrentStepIndex == 1, "An unattended cylinder cannot confirm the measurement");
            Hold(left, mix.Cylinder.Grab); mix.Cylinder.transform.rotation = Quaternion.Euler(0f, 0f, 45f); Tick(0.6f);
            Check(sequence.CurrentStepIndex == 1, "A tilted cylinder cannot confirm the 99.8 mL reading");
            Hold(right, mix.Beaker.Grab); mix.Cylinder.transform.rotation = Quaternion.identity; Tick(0.6f);
            Check(sequence.CurrentStepIndex == 2 && Mathf.Abs(mix.Cylinder.VolumeMl - 99.8f) < 0.01f,
                "Swapping the source for the beaker accepts an upright 99.8 mL reading without regrabbing the source");
            Release(right); Align(mix.Cylinder, mix.Beaker, 95f); Tick(0.6f);
            Check(Mathf.Abs(mix.Cylinder.VolumeMl - 99.8f) < 0.01f && mix.Beaker.VolumeMl == 0f,
                "Confirming measurement still cannot enable one-handed pouring");
            Hold(right, mix.Source.Grab); Tick(0.6f);
            Check(Mathf.Abs(mix.Cylinder.VolumeMl - 99.8f) < 0.01f && mix.Beaker.VolumeMl == 0f,
                "Holding the water source instead of the receiving beaker cannot pour the cylinder");
            Hold(right, mix.Beaker.Grab); Align(mix.Cylinder, mix.Beaker, 95f);
            for (int i = 0; i < 600 && mix.Cylinder.VolumeMl > 0.001f; i++) mix.Tick(0.02f);
            mix.Cylinder.transform.rotation = Quaternion.identity; Tick(0.6f);
            Check(mix.Stage == CopperSulfateMixingDetector.MixingStage.Stirring && Mathf.Abs(mix.Beaker.VolumeMl - 99.8f) < 0.02f,
                "The measured 99.8 mL pours into the opposite-hand beaker and enables stirring");
            Check(Mathf.Abs(mix.Source.VolumeMl + mix.Cylinder.VolumeMl + mix.Beaker.VolumeMl + mix.SpilledMl - 400f) < 0.02f,
                "Swapping vessels conserves the 400 mL water supply");
            Command("reset_sequence"); Command("start_sequence");
            transfer.Forceps.transform.position += transfer.Pickup.position - transfer.Tip.position;
            transfer.EditorTick(0.6f, true);
            transfer.Forceps.transform.position += transfer.Mouth.position + Vector3.up * 0.025f - transfer.Tip.position;
            transfer.EditorTick(0.8f, true);
            Hold(left, mix.Cylinder.Grab); Hold(right, mix.Source.Grab); Align(mix.Source, mix.Cylinder); Tick(1f);
            Command("abort_trial");
            Check(state.CurrentState == ExperimentTrialState.Aborted && !sequence.IsRunning && mix.Cylinder.VolumeMl == 0f,
                "Aborting an active pour restores liquid state and stops the sequence");
            Invoke(receiver, "OnDisable");
            report.Add("Scope: temporary scene, real XRI SelectEnter/SelectExit, real pouring and stirring geometry; simulated poses/time, not PICO hand tracking.");
        }
        catch (Exception exception) { report.Add(exception.ToString()); throw; }
        finally
        {
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); AssetDatabase.DeleteAsset(copy);
            report.Add(before.SequenceEqual(File.ReadAllBytes(path)) ? "PASS: Saved scene unchanged by verification" : "FAIL: Saved scene changed");
            File.WriteAllLines("Temp/CopperSulfateLiquids.verification.txt", report);
        }
        Debug.Log("[CopperLiquids] Verification passed: " + report.Count);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    internal static void Render(Scene scene, string path, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject("Liquid Inspection Camera"); SceneManager.MoveGameObjectToScene(go, scene);
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = 75f;
        camera.nearClipPlane = 0.02f; camera.farClipPlane = 10f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.75f, 0.8f, 0.83f);
        var rt = new RenderTexture(1400, 1000, 24); var previous = RenderTexture.active;
        var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 0.8f;
        var image = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
