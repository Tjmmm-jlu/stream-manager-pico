using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class CopperSulfateFiltrationVerification
{
    public static void CheckLayout(Scene scene, CopperSulfateFiltrationDetector filter, List<string> report)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            report.Add("PASS: " + message);
        }
        var roots = scene.GetRootGameObjects();
        Vector3 stand = roots.SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>()).Single().PreparationStart.position;
        var table = roots.Single(r => r.name == "FilteringStation").transform;
        var tabletop = CopperSulfateStationarySceneBuilder.FindTabletop(table);
        Bounds top = CopperSulfateStationarySceneBuilder.ProjectedBounds(tabletop.gameObject, Vector3.zero, table.rotation);
        var tripod = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Single(s => s.StableId == "tripod_stand");
        Check(filter.StandConfigured && filter.Stand == tripod.GetComponent<XRGrabInteractable>(), "Existing Boiling_Liquid_Stand owns the filtration mounts");
        foreach (var mount in new[] { filter.FunnelMount, filter.ReceiverMount })
            Check(Vector3.ProjectOnPlane(mount.position - stand, Vector3.up).magnitude <= 0.605f,
                mount.name + " is within seated reach");
        var items = new[] { filter.Receiver.gameObject, filter.Funnel.gameObject, filter.Dish.gameObject, tripod.gameObject };
        foreach (var item in items)
        {
            Bounds bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item, Vector3.zero, table.rotation);
            Check(Vector3.ProjectOnPlane(table.rotation * bounds.center - stand, Vector3.up).magnitude <= 0.605f,
                item.name + " remains within the seated 60 cm horizontal reach");
            bool support = item == tripod.gameObject;
            if (support)
                bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(
                    tripod.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "pCube89").gameObject, Vector3.zero, table.rotation);
            Check(bounds.min.x >= top.min.x - 0.001f && bounds.max.x <= top.max.x + 0.001f &&
                bounds.min.z >= top.min.z - 0.001f && bounds.max.z <= top.max.z + 0.001f &&
                bounds.min.y >= top.max.y - 0.001f && bounds.min.y - top.max.y < 0.005f,
                item.name + " rests fully on the filtering tabletop");
        }
        Physics.SyncTransforms();
        for (int i = 0; i < items.Length; i++)
        for (int j = i + 1; j < items.Length; j++)
        foreach (var a in items[i].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
        foreach (var b in items[j].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position,
                b.transform.rotation, out _, out float depth) && depth > 0.001f)
                throw new InvalidOperationException("FAIL: Filtering apparatus overlap: " + items[i].name + " / " + items[j].name);
        report.Add("PASS: Filtering apparatus colliders do not overlap at rest");
        if (filter.Dish.transform.Find("Evaporating Dish Colliders") != null)
        {
            var bowl = filter.Dish.GetComponent<MeshFilter>().sharedMesh;
            var crucibleMeshes = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true))
                .Where(s => s.ObjectType == "crucible").SelectMany(s => s.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            Check(crucibleMeshes.Length > 0 && crucibleMeshes.All(m => m.sharedMesh != bowl) && bowl.bounds.size.y < bowl.bounds.size.x * 0.3f &&
                bowl.bounds.max.z > -bowl.bounds.min.z + 0.01f,
                "Evaporating dish has a distinct shallow profile and pouring spout; crucible keeps its mesh");
            Check(filter.Dish.Grab.colliders.Count > 0 && filter.Dish.Grab.colliders.All(c => c.enabled),
                "The replacement dish has enabled grab colliders");
            var eye = filter.Dish.Mouth + new Vector3(0.08f, 0.12f, -0.2f);
            CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateApparatusFix.dish.png", eye,
                Quaternion.LookRotation(filter.Dish.Mouth - eye));
        }
    }

    public static void Run(Scene scene, CopperSulfateMixingDetector mix, XRInteractionManager manager,
        XRDirectInteractor left, XRDirectInteractor right, List<string> report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var filter = mix.GetComponent<CopperSulfateFiltrationDetector>();
        var sequence = mix.GetComponent<PreparationSequenceController>();
        var channel = mix.GetComponent<GuidanceDataChannelReceiver>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            report.Add("PASS: " + description);
        }
        void Command(string command) => typeof(GuidanceDataChannelReceiver).GetMethod("OnMessage", flags).Invoke(channel,
            new object[] { Encoding.UTF8.GetBytes("{\"type\":\"" + command + "\"}") });
        foreach (var grab in new[] { filter.Receiver.Grab, filter.Dish.Grab, filter.Funnel, filter.Stand })
        {
            grab.interactionManager = manager;
            typeof(XRGrabInteractable).GetMethod("Awake", flags).Invoke(grab, null);
        }
        void Release(XRDirectInteractor hand)
        {
            foreach (var selected in hand.interactablesSelected.ToArray()) manager.SelectExit(hand, selected);
            PicoPinchToggleVerification.FlushReturns(scene);
        }
        void Hold(XRDirectInteractor hand, XRGrabInteractable item)
        {
            Release(hand); hand.transform.position = item.attachTransform != null ? item.attachTransform.position : item.transform.position;
            manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)item);
        }
        void MoveReceiverWithHand(XRDirectInteractor hand, Vector3 movement)
        {
            var grab = filter.Receiver.Grab;
            float ease = grab.attachEaseInTime;
            try
            {
                grab.attachEaseInTime = 0f;
                grab.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                Vector3 before = grab.transform.position;
                hand.transform.position += movement;
                grab.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                Check(Vector3.Distance(grab.transform.position, before + movement) < 0.002f,
                    "The removed receiver follows actual XRI interactor movement");
            }
            finally { grab.attachEaseInTime = ease; }
        }
        void Tick(float time)
        {
            for (int i = 0; i < Mathf.CeilToInt(time / 0.02f); i++)
            { Physics.SyncTransforms(); mix.Tick(0.02f); filter.Tick(0.02f); }
        }
        void AlignFunnel()
        {
            filter.Funnel.transform.rotation = filter.FunnelMount.rotation;
            filter.Funnel.transform.position += filter.FunnelMount.position - filter.Seat.position;
        }
        void AlignReceiver()
        {
            filter.Receiver.transform.rotation = filter.ReceiverMount.rotation;
            filter.Receiver.transform.position += filter.ReceiverMount.position - filter.Receiver.transform.TransformPoint(filter.Receiver.Bottom);
        }
        void Assemble()
        {
            Hold(left, filter.Funnel); AlignFunnel(); Tick(0.3f);
            Hold(left, filter.Receiver.Grab); AlignReceiver(); Tick(0.3f);
            PicoPinchToggleVerification.FlushReturns(scene);
        }
        void AlignSource(float tilt = 95f)
        {
            Vector3 direction = Vector3.ProjectOnPlane(new Vector3(4.25f, 0f, 6.8f) - filter.Inlet.position, Vector3.up).normalized;
            mix.Rod.transform.rotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up + direction * 0.6f).normalized);
            mix.Rod.transform.position += filter.GuideTarget.position - mix.RodTip.position;
            mix.Beaker.transform.rotation = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, -direction));
            mix.Beaker.transform.position += mix.RodTip.position + mix.Rod.transform.up * 0.13f - mix.Beaker.LowestLip();
        }
        void AlignDish()
        {
            filter.Dish.transform.SetPositionAndRotation(new Vector3(-60f, 2f, -60f), Quaternion.identity);
            filter.Receiver.transform.rotation = Quaternion.Euler(0f, 0f, 95f);
            filter.Receiver.transform.position += filter.Dish.Mouth + Vector3.up * 0.12f - filter.Receiver.LowestLip();
        }
        Check(sequence.CurrentStepIndex == 3 && sequence.CurrentStep.completionAction == CopperSulfateFiltrationDetector.FilterAction,
            "Real dissolution leads to a gated filtration action");
        Release(left); Release(right); mix.Beaker.SetVolume(99.8f); Tick(0.1f);
        Check(Mathf.Abs(filter.InitialMl - 99.8f) < 0.01f && Mathf.Abs(filter.MinimumMl - 89.82f) < 0.01f,
            "Filtration threshold uses 90 percent of the actual 99.8 mL solution");
        Command("next_step"); Command("complete_trial");
        Check(sequence.CurrentStepIndex == 3 && sequence.HasPendingActions, "Web commands cannot skip filtration");
        Hold(left, mix.Beaker.Grab); Hold(right, mix.Rod); AlignSource(); Tick(0.5f);
        Check(filter.Receiver.VolumeMl == 0f && mix.Beaker.VolumeMl == 99.8f, "The beaker cannot fill the receiver before assembling the filter");
        Release(left); Release(right); AlignFunnel(); Tick(0.4f);
        Check(!filter.Attached, "Contact alone cannot mount an unheld funnel");
        Hold(left, filter.Funnel); AlignFunnel();
        filter.Funnel.transform.position += Vector3.right * 0.2f; Tick(0.4f);
        Check(!filter.Attached, "A misaligned funnel does not snap across space");
        AlignFunnel(); filter.Funnel.transform.rotation *= Quaternion.Euler(40f, 0f, 0f); Tick(0.4f);
        Check(!filter.Attached, "A tilted funnel cannot mount");
        AlignFunnel(); Tick(0.1f); Check(!filter.Attached, "Assembly requires a stable alignment dwell"); Tick(0.2f);
        Check(filter.Attached && filter.Funnel.transform.IsChildOf(filter.Stand.transform) && !filter.Funnel.isSelected,
            "A held funnel mounts on the stand independently of the receiving beaker");
        Check(!filter.Funnel.enabled && !filter.Stand.enabled && Vector3.Distance(filter.Seat.position, filter.FunnelMount.position) < 0.001f,
            "The mounted funnel and support stay fixed during filtering");
        Hold(left, filter.Receiver.Grab); AlignReceiver(); filter.Receiver.transform.position += Vector3.right * 0.2f; Tick(0.4f);
        Check(!filter.ReceiverMounted, "The receiver cannot install outside its base position");
        AlignReceiver(); filter.Receiver.transform.rotation *= Quaternion.Euler(40f, 0f, 0f); Tick(0.4f);
        Check(!filter.ReceiverMounted, "A tilted receiving beaker cannot install");
        AlignReceiver(); Tick(0.3f);
        PicoPinchToggleVerification.FlushReturns(scene);
        Check(filter.ReceiverMounted && !filter.Receiver.Grab.isSelected && filter.Receiver.Grab.enabled &&
            filter.Funnel.transform.IsChildOf(filter.Stand.transform) && filter.Receiver.transform.IsChildOf(filter.Stand.transform),
            "Both separately installed apparatus stay on the stand after automatic hand release");
        Hold(left, filter.Receiver.Grab); Tick(0.4f);
        Check(filter.Receiver.Grab.isSelected && !filter.ReceiverMounted,
            "The mounted receiver can be picked up before filtering and does not immediately snap back");
        MoveReceiverWithHand(left, Vector3.left * 0.15f); Tick(0.1f);
        AlignReceiver(); Tick(0.3f);
        Check(filter.ReceiverMounted && !filter.Receiver.Grab.isSelected, "The receiver can be returned to its mount after adjustment");
        var nozzle = filter.Receiver.transform.InverseTransformPoint(filter.FilterOutlet.position) - filter.Receiver.Bottom;
        Check(nozzle.y > 0f && nozzle.y < filter.Receiver.Height && new Vector2(nozzle.x, nozzle.z).magnitude < filter.Receiver.Radius,
            "The funnel outlet extends inside the receiving beaker near its inner wall");
        Check(Physics.OverlapSphere(filter.Inlet.position + Vector3.up * 0.03f, 0.015f).All(c => !c.transform.IsChildOf(filter.Stand.transform)),
            "The stand leaves the filter opening unobstructed");
        var lookAt = filter.Inlet.position;
        var view = lookAt + new Vector3(0.15f, 0.17f, -0.35f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.assembled.png", view, Quaternion.LookRotation(lookAt - view));
        Hold(left, mix.Beaker.Grab); Release(right); AlignSource(); Tick(0.4f);
        Check(filter.Receiver.VolumeMl == 0f && filter.Status == "hold_beaker_and_rod", "Filtering requires a glass rod held in the other hand");
        Hold(right, mix.Rod); right.handedness = InteractorHandedness.Left; AlignSource(); Tick(0.4f);
        Check(filter.Receiver.VolumeMl == 0f && mix.Beaker.VolumeMl == 99.8f, "Beaker and rod must be held by different physical hands");
        right.handedness = InteractorHandedness.Right; AlignSource(0f); Tick(0.4f);
        Check(filter.Receiver.VolumeMl == 0f, "An upright solution beaker cannot filter");
        AlignSource();
        Vector3 adjustment = filter.Inlet.position + new Vector3(0.055f, 0.06f, 0f) - mix.RodTip.position;
        mix.Rod.transform.position += adjustment; mix.Beaker.transform.position += adjustment;
        mix.Beaker.transform.position += Vector3.Cross(mix.Rod.transform.up, Vector3.up).normalized * 0.08f;
        Tick(0.3f);
        Check(filter.Receiver.VolumeMl > 0f && filter.AttemptSpilledMl == 0f,
            "A rod end 5.5 cm off center and 6 cm above the inlet guides a cup lip 8 cm from the shaft");
        float beforeReversedRod = filter.Receiver.VolumeMl;
        mix.Rod.transform.rotation = Quaternion.AngleAxis(180f, mix.Rod.transform.right) * mix.Rod.transform.rotation; Tick(0.3f);
        Check(filter.Receiver.VolumeMl > beforeReversedRod && filter.AttemptSpilledMl == 0f,
            "Either physical end of the glass rod can guide liquid");
        Command("retry_liquid_stage"); Assemble(); Hold(left, mix.Beaker.Grab); Hold(right, mix.Rod);
        AlignSource(); mix.Beaker.transform.position += Vector3.right * 0.3f; Tick(0.8f);
        Check(filter.Receiver.VolumeMl == 0f && filter.AttemptSpilledMl == 0f && filter.Status == "align_glass_rod" &&
            mix.Beaker.VolumeMl == 99.8f,
            "A cup missing the glass rod pauses filtration without consuming solution");
        Check(Mathf.Abs(mix.Beaker.VolumeMl + filter.Receiver.VolumeMl + filter.AttemptSpilledMl - 99.8f) < 0.02f,
            "Filtration misses conserve solution volume");
        Command("retry_liquid_stage");
        Check(sequence.CurrentStepIndex == 3 && !filter.Attached && mix.Beaker.VolumeMl == 99.8f && filter.Receiver.VolumeMl == 0f &&
            filter.AttemptSpilledMl == 0f && mix.Stage == CopperSulfateMixingDetector.MixingStage.Completed,
            "Web retry restores only filtration while keeping dissolution completed");
        // Either assembly order is accepted, while the original supply pose is restored by retry.
        Hold(right, filter.Receiver.Grab); AlignReceiver(); Tick(0.3f);
        Check(filter.ReceiverMounted && !filter.Attached, "The receiving beaker may be installed before the funnel");
        Hold(left, filter.Funnel); AlignFunnel(); Tick(0.3f);
        Hold(left, mix.Beaker.Grab); Hold(right, mix.Rod); AlignSource();
        mix.Rod.transform.position += Vector3.right * 0.25f; Tick(0.2f);
        Check(filter.Receiver.VolumeMl == 0f && filter.AttemptSpilledMl == 0f && mix.Beaker.VolumeMl == 99.8f,
            "A rod far from the funnel pauses without filling the receiver or losing solution");
        Command("retry_liquid_stage"); Assemble(); Hold(left, mix.Beaker.Grab); Hold(right, mix.Rod); AlignSource();
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(obstacle, scene);
        obstacle.transform.position = mix.RodTip.position + mix.Rod.transform.up * 0.06f;
        obstacle.transform.localScale = new Vector3(0.03f, 0.015f, 0.03f); Tick(0.3f);
        Check(filter.Receiver.VolumeMl == 0f && filter.AttemptSpilledMl == 0f && mix.Beaker.VolumeMl == 99.8f,
            "A solid obstruction pauses the guided stream without consuming solution");
        var rig = new GameObject("Verification Hand Rig"); SceneManager.MoveGameObjectToScene(rig, scene);
        rig.AddComponent<XROrigin>(); right.transform.SetParent(rig.transform, true); obstacle.transform.SetParent(rig.transform, true);
        Tick(0.3f);
        Check(filter.Receiver.VolumeMl > 0f && filter.AttemptSpilledMl == 0f,
            "Colliders belonging to the holding hand rig do not block assisted filtration");
        right.transform.SetParent(null, true); UnityEngine.Object.DestroyImmediate(rig);
        Command("retry_liquid_stage"); Assemble(); Hold(left, mix.Beaker.Grab); Hold(right, mix.Rod); AlignSource();
        Tick(0.5f);
        Check(filter.Receiver.VolumeMl > 0f && !filter.Receiver.Grab.isSelected && !filter.Funnel.isSelected,
            "Both hands are free for the source beaker and guiding rod during mounted filtering");
        float retainedFiltrate = filter.Receiver.VolumeMl;
        Hold(left, filter.Receiver.Grab); Tick(0.4f);
        Check(!filter.ReceiverMounted && filter.Receiver.Grab.isSelected && Mathf.Abs(filter.Receiver.VolumeMl - retainedFiltrate) < 0.001f,
            "A partially filled receiver can be removed without losing liquid or continuing filtration");
        MoveReceiverWithHand(left, Vector3.left * 0.15f); Tick(0.1f);
        AlignReceiver(); Tick(0.3f);
        Check(filter.ReceiverMounted && Mathf.Abs(filter.Receiver.VolumeMl - retainedFiltrate) < 0.001f &&
            filter.Receiver.GetComponent<SemanticObject>().StateTags.Contains("filtered"),
            "Remounting preserves collected filtrate and its semantic state");
        Hold(left, mix.Beaker.Grab); AlignSource();
        lookAt = filter.GuideTarget.position;
        view = lookAt + new Vector3(-0.26f, 0.23f, -0.38f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateStandFiltration.pouring.png", view, Quaternion.LookRotation(lookAt - view));
        for (int i = 0; i < 700 && filter.Receiver.VolumeMl < filter.InitialMl * 0.91f; i++) Tick(0.02f);
        float beforeAdjustment = mix.Beaker.VolumeMl;
        mix.Beaker.transform.position += Vector3.right * 0.3f; Tick(0.6f);
        Check(beforeAdjustment > 0f && mix.Beaker.VolumeMl == beforeAdjustment &&
            filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Filtering,
            "A tilted beaker moving out of alignment pauses without prematurely completing filtration");
        AlignSource();
        for (int i = 0; i < 700 && mix.Beaker.VolumeMl > 0.001f; i++) Tick(0.02f);
        mix.Beaker.transform.rotation = Quaternion.identity; Tick(0.6f);
        Check(Mathf.Abs(filter.Receiver.VolumeMl - 99.8f) < 0.02f && filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.CollectingFiltrate,
            "The measured solution passes along the glass rod into the stationary receiver");
        Check(filter.TrappedResidue.gameObject.activeSelf && filter.TrappedResidue.localScale.sqrMagnitude > 0f &&
            mix.Residue.transform.localScale.sqrMagnitude < 0.001f,
            "Gray residue leaves the source beaker and remains on the filter paper");
        Check(filter.Receiver.GetComponent<SemanticObject>().ContentColor == "blue" &&
            filter.Receiver.GetComponent<SemanticObject>().StateTags.Contains("filtered"),
            "Filtered solution remains blue and is registered as filtered");
        Check(mix.Beaker.GetComponent<SemanticObject>().Contents == "none" &&
            filter.Funnel.GetComponent<SemanticObject>().ContentColor == "gray",
            "Object catalog distinguishes the emptied source from residue retained in the funnel");
        Command("next_step"); Check(sequence.CurrentStepIndex == 3, "Collecting filtrate requires taking the receiving beaker from the stand");
        lookAt = filter.Receiver.Mouth;
        view = lookAt + new Vector3(0.03f, 0.48f, -0.18f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.filtered.png", view, Quaternion.LookRotation(lookAt - view));
        Release(left); Release(right); Hold(left, filter.Receiver.Grab); Tick(0.02f);
        Check(sequence.CurrentStepIndex == 3, "Picking up without moving the receiver does not complete collection");
        MoveReceiverWithHand(left, Vector3.left * 0.15f); Tick(0.3f);
        Check(filter.Attached && !filter.ReceiverMounted && sequence.CurrentStepIndex == 4 &&
            filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Transferring && filter.Stand.enabled,
            "Taking the receiver away completes filtration while leaving the funnel on the stand");
        Command("next_step"); Check(sequence.CurrentStepIndex == 4, "The evaporating-dish transfer cannot be skipped");
        Release(left); Release(right); Hold(left, filter.Receiver.Grab); AlignDish(); Tick(0.4f);
        Check(filter.Dish.VolumeMl == 0f, "Transfer requires the evaporating dish in the other hand");
        Hold(right, filter.Dish.Grab); filter.Receiver.transform.position += Vector3.right * 0.3f; Tick(0.8f);
        Check(filter.Status == "filtrate_lost" && filter.Dish.VolumeMl == 0f, "Missing the dish reports a failed transfer attempt");
        Command("retry_liquid_stage");
        Check(sequence.CurrentStepIndex == 4 && Mathf.Abs(filter.Receiver.VolumeMl - 99.8f) < 0.02f &&
            filter.Dish.VolumeMl == 0f && filter.TrappedResidue.gameObject.activeSelf && filter.Attached && !filter.ReceiverMounted,
            "Transfer retry preserves completed filtration and its trapped residue");
        Hold(left, filter.Receiver.Grab); Hold(right, filter.Dish.Grab); AlignDish();
        for (int i = 0; i < 700 && filter.Receiver.VolumeMl > 0.001f; i++) Tick(0.02f);
        filter.Receiver.transform.rotation = Quaternion.identity; Tick(0.6f);
        Check(filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Completed && sequence.CurrentStepIndex == 5 &&
            Mathf.Abs(filter.Dish.VolumeMl - 99.8f) < 0.02f, "Real filtrate transfer fills the dish and advances to heating assembly");
        Check(filter.Dish.GetComponent<SemanticObject>().ContentColor == "blue" && filter.Dish.GetComponent<SemanticObject>().StateTags.Contains("filtered"),
            "Evaporating dish contains filtered blue solution");
        Check(Mathf.Abs(filter.Receiver.VolumeMl + filter.Dish.VolumeMl + filter.AttemptSpilledMl - filter.InitialMl) < 0.02f,
            "Filtrate transfer conserves the amount collected by filtration");
        Tick(1f); Check(sequence.CurrentStepIndex == 5 && !filter.CanRetry, "Completed filtration cannot advance repeatedly or retry a later stage");
        filter.Dish.RefreshVisual(); lookAt = filter.Dish.Mouth;
        float fullLevel = filter.Dish.SurfaceWorldY;
        filter.Dish.SetVolume(49.9f); filter.Dish.RefreshVisual();
        Check(filter.Dish.SurfaceWorldY < fullLevel && filter.Dish.SurfaceWorldY > filter.Dish.transform.TransformPoint(filter.Dish.Bottom).y,
            "A partially filled shallow dish has a lower interior liquid surface");
        filter.Dish.transform.rotation = Quaternion.Euler(0f, 0f, 35f); filter.Dish.RefreshVisual();
        var dishMesh = filter.Dish.transform.Find("Measured Liquid").GetComponent<MeshFilter>().sharedMesh;
        Check(dishMesh.vertices.All(v => filter.Dish.transform.TransformPoint(v).y <= filter.Dish.SurfaceWorldY + 0.0001f),
            "Liquid in the tilted shallow dish remains below a horizontal surface");
        filter.Dish.transform.rotation = Quaternion.identity; filter.Dish.SetVolume(99.8f); filter.Dish.RefreshVisual();
        if (filter.Dish.transform.Find("Evaporating Dish Colliders") != null)
        {
            Check(dishMesh.vertices.All(v => v.y >= 0.006f - 0.0001f && v.y <= 0.031f + 0.0001f &&
                new Vector2(v.x, v.z).magnitude <= Mathf.Lerp(0.026f, 0.063f, (v.y - 0.006f) / 0.029f) + 0.0001f),
                "Filtered liquid fits inside the replacement dish's actual interior walls and below its spout");
        }
        view = lookAt + new Vector3(0.03f, 0.13f, -0.2f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.dish.png", view, Quaternion.LookRotation(lookAt - view));
        CopperSulfateHeatingVerification.Run(scene, filter, manager, left, right, report);
    }
}
