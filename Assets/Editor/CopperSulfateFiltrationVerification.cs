using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
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
        var items = new[] { filter.Receiver.gameObject, filter.Funnel.gameObject, filter.Dish.gameObject, tripod.gameObject };
        foreach (var item in items)
        {
            Bounds bounds = CopperSulfateStationarySceneBuilder.ProjectedBounds(item, Vector3.zero, table.rotation);
            Check(Vector3.ProjectOnPlane(table.rotation * bounds.center - stand, Vector3.up).magnitude <= 0.605f,
                item.name + " remains within the seated 60 cm horizontal reach");
            Check(bounds.min.x >= top.min.x - 0.001f && bounds.max.x <= top.max.x + 0.001f &&
                bounds.min.z >= top.min.z - 0.001f && bounds.max.z <= top.max.z + 0.001f &&
                bounds.min.y >= top.max.y && bounds.min.y - top.max.y < 0.005f,
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
        foreach (var grab in new[] { filter.Receiver.Grab, filter.Dish.Grab, filter.Funnel })
        {
            grab.interactionManager = manager;
            typeof(XRGrabInteractable).GetMethod("Awake", flags).Invoke(grab, null);
        }
        void Release(XRDirectInteractor hand)
        {
            foreach (var selected in hand.interactablesSelected.ToArray()) manager.SelectExit(hand, selected);
        }
        void Hold(XRDirectInteractor hand, XRGrabInteractable item)
        {
            Release(hand); hand.transform.position = item.attachTransform != null ? item.attachTransform.position : item.transform.position;
            manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)item);
        }
        void Tick(float time)
        {
            for (int i = 0; i < Mathf.CeilToInt(time / 0.02f); i++)
            { Physics.SyncTransforms(); mix.Tick(0.02f); filter.Tick(0.02f); }
        }
        void PositionAssembly()
        {
            filter.Receiver.transform.SetPositionAndRotation(new Vector3(-60f, 2f, -60f), Quaternion.identity);
            filter.Funnel.transform.rotation = Quaternion.identity;
            filter.Funnel.transform.position += filter.Receiver.Mouth - filter.Seat.position;
            Physics.SyncTransforms();
        }
        void Assemble()
        {
            Hold(left, filter.Funnel); Hold(right, filter.Receiver.Grab); PositionAssembly(); Tick(0.3f);
        }
        void AlignSource(float tilt = 95f)
        {
            mix.Beaker.transform.rotation = Quaternion.Euler(0f, 0f, tilt);
            mix.Beaker.transform.position += filter.Inlet.position + Vector3.up * 0.12f - mix.Beaker.LowestLip();
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
        Hold(left, mix.Beaker.Grab); Hold(right, filter.Receiver.Grab); PositionAssembly(); AlignSource(); Tick(0.5f);
        Check(filter.Receiver.VolumeMl == 0f && mix.Beaker.VolumeMl == 99.8f, "The beaker cannot fill the receiver before assembling the filter");
        Release(right); Hold(left, filter.Funnel); PositionAssembly(); Tick(0.4f);
        Check(!filter.Attached, "One-handed assembly is rejected");
        Hold(right, filter.Receiver.Grab); right.handedness = InteractorHandedness.Left; Tick(0.4f);
        Check(!filter.Attached, "Assembly requires different physical hands");
        right.handedness = InteractorHandedness.Right;
        filter.Funnel.transform.position += Vector3.right * 0.2f; Tick(0.4f);
        Check(!filter.Attached, "A misaligned funnel does not snap across space");
        PositionAssembly(); Tick(0.3f);
        Check(filter.Attached && filter.Funnel.transform.IsChildOf(filter.Receiver.transform) && !filter.Funnel.isSelected,
            "Aligned two-hand assembly mounts the funnel and releases its original hand");
        Check(!filter.Funnel.enabled && Vector3.Distance(filter.Seat.position, filter.Receiver.Mouth) < 0.001f,
            "Mounted funnel follows the receiving beaker without a competing grab");
        var lookAt = filter.Inlet.position;
        var view = lookAt + new Vector3(0.15f, 0.17f, -0.35f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.assembled.png", view, Quaternion.LookRotation(lookAt - view));
        Hold(left, mix.Beaker.Grab); Release(right); AlignSource(); Tick(0.4f);
        Check(filter.Receiver.VolumeMl == 0f, "Filtering cannot run with the receiving assembly on the table");
        Hold(right, filter.Receiver.Grab); AlignSource(0f); Tick(0.4f);
        Check(filter.Receiver.VolumeMl == 0f, "An upright solution beaker cannot filter");
        AlignSource(); mix.Beaker.transform.position += Vector3.right * 0.3f; Tick(0.8f);
        Check(filter.Receiver.VolumeMl == 0f && filter.AttemptSpilledMl > 10f && filter.Status == "filtrate_lost",
            "Missing the paper records real loss and reports insufficient remaining solution");
        Check(Mathf.Abs(mix.Beaker.VolumeMl + filter.Receiver.VolumeMl + filter.AttemptSpilledMl - 99.8f) < 0.02f,
            "Filtration misses conserve solution volume");
        Command("retry_liquid_stage");
        Check(sequence.CurrentStepIndex == 3 && !filter.Attached && mix.Beaker.VolumeMl == 99.8f && filter.Receiver.VolumeMl == 0f &&
            filter.AttemptSpilledMl == 0f && mix.Stage == CopperSulfateMixingDetector.MixingStage.Completed,
            "Web retry restores only filtration while keeping dissolution completed");
        Assemble(); Hold(left, mix.Beaker.Grab); AlignSource();
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(obstacle, scene);
        obstacle.transform.position = filter.Inlet.position + Vector3.up * 0.06f;
        obstacle.transform.localScale = new Vector3(0.06f, 0.01f, 0.06f); Tick(0.3f);
        Check(filter.Receiver.VolumeMl == 0f && filter.AttemptSpilledMl > 0f, "A physical obstruction intercepts the stream above the paper");
        UnityEngine.Object.DestroyImmediate(obstacle); Command("retry_liquid_stage"); Assemble(); Hold(left, mix.Beaker.Grab); AlignSource();
        for (int i = 0; i < 700 && mix.Beaker.VolumeMl > 0.001f; i++) Tick(0.02f);
        mix.Beaker.transform.rotation = Quaternion.identity; Tick(0.6f);
        Check(Mathf.Abs(filter.Receiver.VolumeMl - 99.8f) < 0.02f && filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.RemovingFunnel,
            "The complete measured solution passes through the filter into the held receiver");
        Check(filter.TrappedResidue.gameObject.activeSelf && filter.TrappedResidue.localScale.sqrMagnitude > 0f &&
            mix.Residue.transform.localScale.sqrMagnitude < 0.001f,
            "Gray residue leaves the source beaker and remains on the filter paper");
        Check(filter.Receiver.GetComponent<SemanticObject>().ContentColor == "blue" &&
            filter.Receiver.GetComponent<SemanticObject>().StateTags.Contains("filtered"),
            "Filtered solution remains blue and is registered as filtered");
        Check(mix.Beaker.GetComponent<SemanticObject>().Contents == "none" &&
            filter.Funnel.GetComponent<SemanticObject>().ContentColor == "gray",
            "Object catalog distinguishes the emptied source from residue retained in the funnel");
        Command("next_step"); Check(sequence.CurrentStepIndex == 3, "Collecting filtrate still requires removing the mounted funnel");
        Hold(left, filter.Funnel); Tick(0.02f);
        filter.Funnel.transform.position += Vector3.up * 0.15f + Vector3.left * 0.15f; Tick(0.3f);
        Check(!filter.Attached && sequence.CurrentStepIndex == 4 && filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Transferring,
            "Lifting the funnel away unlocks the real filtrate transfer action");
        lookAt = filter.Receiver.Mouth;
        view = lookAt + new Vector3(0.03f, 0.48f, -0.18f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.filtered.png", view, Quaternion.LookRotation(lookAt - view));
        Command("next_step"); Check(sequence.CurrentStepIndex == 4, "The evaporating-dish transfer cannot be skipped");
        Release(left); Release(right); Hold(left, filter.Receiver.Grab); AlignDish(); Tick(0.4f);
        Check(filter.Dish.VolumeMl == 0f, "Transfer requires the evaporating dish in the other hand");
        Hold(right, filter.Dish.Grab); filter.Receiver.transform.position += Vector3.right * 0.3f; Tick(0.8f);
        Check(filter.Status == "filtrate_lost" && filter.Dish.VolumeMl == 0f, "Missing the dish reports a failed transfer attempt");
        Command("retry_liquid_stage");
        Check(sequence.CurrentStepIndex == 4 && Mathf.Abs(filter.Receiver.VolumeMl - 99.8f) < 0.02f &&
            filter.Dish.VolumeMl == 0f && filter.TrappedResidue.gameObject.activeSelf && !filter.Attached,
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
        view = lookAt + new Vector3(0.03f, 0.13f, -0.2f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateFiltration.dish.png", view, Quaternion.LookRotation(lookAt - view));
    }
}
