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

public static class CopperSulfateHeatingVerification
{
    public static void Run(Scene scene, CopperSulfateFiltrationDetector filter, XRInteractionManager manager,
        XRDirectInteractor left, XRDirectInteractor right, List<string> report)
    {
        var heat = filter.GetComponent<CopperSulfateHeatingDetector>();
        if (heat == null) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sequence = filter.GetComponent<PreparationSequenceController>();
        var channel = filter.GetComponent<GuidanceDataChannelReceiver>();
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("FAIL: " + name); report.Add("PASS: " + name); }
        void Command(string name) => typeof(GuidanceDataChannelReceiver).GetMethod("OnMessage", flags).Invoke(channel,
            new object[] { Encoding.UTF8.GetBytes("{\"type\":\"" + name + "\"}") });
        foreach (var item in new[] { heat.Lamp, heat.Cap, heat.Lighter })
        { item.interactionManager = manager; typeof(XRGrabInteractable).GetMethod("Awake", flags).Invoke(item, null); }
        void Tick(float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / 0.02f); i++)
            { Physics.SyncTransforms(); filter.Tick(0.02f); heat.Tick(0.02f); }
        }
        void Release(XRDirectInteractor hand)
        {
            foreach (var selected in hand.interactablesSelected.ToArray()) manager.SelectExit((IXRSelectInteractor)hand, selected);
            PicoPinchToggleVerification.FlushReturns(scene);
        }
        void Hold(XRDirectInteractor hand, XRGrabInteractable grab)
        {
            Release(hand); hand.transform.position = grab.attachTransform != null ? grab.attachTransform.position : grab.transform.position;
            manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
        }
        void Align(XRGrabInteractable item, Transform target) => item.transform.SetPositionAndRotation(target.position, target.rotation);
        void TakeOffCap()
        {
            Hold(right, heat.Cap); Tick(0.1f);
            heat.Cap.transform.position += Vector3.right * 0.15f; Tick(0.1f); Release(right); Tick(0.1f);
        }
        void Cover()
        { Hold(right, heat.Cap); Align(heat.Cap, heat.CapMount); Tick(0.4f); }
        void Ignite()
        {
            Hold(left, heat.Lighter); heat.Lighter.transform.position += heat.Wick.position - heat.IgnitionTip.position;
            Tick(0.4f); Release(left);
        }
        Release(left); Release(right); Tick(0.1f);
        Check(sequence.CurrentStepIndex == 5 && sequence.CurrentStep.completionAction == CopperSulfateHeatingDetector.AssembleAction,
            "Filtrate transfer now leads to a gated heating assembly action");
        Check(heat.Covered && !heat.Lit && !heat.Flame.activeSelf, "Alcohol lamp starts capped and unlit");
        Check(heat.Crystals != null && !heat.Crystals.activeSelf && heat.Crystals.transform.IsChildOf(filter.Dish.transform) &&
            heat.Crystals.GetComponentsInChildren<Collider>(true).Length == 0,
            "Crystallization visual starts hidden in the dish without blocking apparatus colliders");
        Check(Mathf.Abs(heat.InitialMl - 99.8f) < 0.02f, "Heating snapshots the actual filtered solution volume");
        Command("next_step"); Command("complete_trial");
        Check(sequence.CurrentStepIndex == 5 && sequence.IsRunning, "Web shortcuts cannot skip heating assembly");
        Hold(left, filter.Dish.Grab); Align(filter.Dish.Grab, heat.DishMount); Tick(0.4f);
        Check(!heat.DishMounted, "The dish cannot mount while the filtration funnel occupies the ring");
        Hold(left, filter.Funnel); Tick(0.1f); filter.Funnel.transform.position += Vector3.left * 0.2f; Release(left); Tick(0.1f);
        Check(!filter.Attached, "The completed filter funnel can be taken off before heating");
        Align(filter.Dish.Grab, heat.DishMount); Tick(0.4f);
        Check(!heat.DishMounted, "Touching the heating seat without holding the dish does not mount it");
        Hold(left, filter.Dish.Grab); Align(filter.Dish.Grab, heat.DishMount); Tick(0.1f);
        Check(!heat.DishMounted, "Dish installation requires stable alignment"); Tick(0.2f);
        Check(heat.DishMounted && !filter.Dish.Grab.isSelected, "Dish mounts directly on the existing ring and releases the hand");
        Hold(left, heat.Lamp); Align(heat.Lamp, heat.LampMount); Tick(0.3f);
        Check(heat.LampMounted && sequence.CurrentStepIndex == 6 && !heat.Lamp.isSelected, "Installing lamp below dish unlocks heating");
        Check(heat.Wick.position.y < heat.DishMount.position.y &&
            Vector3.ProjectOnPlane(heat.Wick.position - heat.DishMount.position, Vector3.up).magnitude < 0.02f,
            "Lamp wick is centered below the dish on the original stand");
        Ignite(); Check(!heat.Lit, "The lighter cannot ignite a capped lamp");
        TakeOffCap();
        Check(!heat.Covered && Vector3.Distance(heat.Cap.transform.position, heat.CapParking.position) < 0.001f,
            "Released cap returns to its side parking point instead of covering the wick");
        Check(heat.Lamp.GetComponent<SemanticObject>().StateTags.Contains("uncapped") &&
            heat.Cap.GetComponent<SemanticObject>().StateTags.Contains("removed"), "Object catalog reflects the removed cap");
        Check(Vector3.ProjectOnPlane(heat.CapParking.position - heat.LampMount.position, Vector3.up).magnitude < 0.15f,
            "Cap parks within 15 cm of the mounted lamp");
        Hold(left, heat.Lighter); heat.Lighter.transform.position += heat.Wick.position + Vector3.right * 0.2f - heat.IgnitionTip.position;
        Tick(0.5f); Check(!heat.Lit, "The lighter cannot ignite from far away");
        heat.Lighter.transform.position += heat.Wick.position - heat.IgnitionTip.position;
        Tick(0.1f); Check(!heat.Lit, "A brief contact does not ignite the lamp"); Tick(0.25f);
        Check(heat.Lit && heat.Flame.activeSelf && heat.EvaporatedMl > 0f && !filter.Dish.Grab.enabled && !heat.Lamp.enabled,
            "Held lighter ignites after a stable dwell; mounted hot apparatus stays fixed");
        Release(left); Tick(2f);
        float retained = filter.Dish.VolumeMl;
        Check(retained < heat.InitialMl && retained > heat.TargetMl, "Lit mounted lamp progressively lowers the solution level");
        var focus = (heat.Wick.position + filter.Dish.Mouth) * 0.5f;
        var eye = focus + new Vector3(0.22f, 0.13f, -0.38f);
        filter.Dish.RefreshVisual();
        var steam = filter.Dish.GetComponentInChildren<ParticleSystem>(); steam.Simulate(0.8f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateHeating.burning.png", eye, Quaternion.LookRotation(focus - eye));
        Cover();
        float paused = filter.Dish.VolumeMl; Tick(1f);
        Check(!heat.Lit && heat.Covered && !heat.Cap.isSelected && filter.Dish.VolumeMl == paused && sequence.CurrentStepIndex == 6,
            "Covering early extinguishes, keeps the cap installed, and pauses without completing");
        TakeOffCap(); Ignite(); Tick(0.5f);
        Check(heat.Lit && filter.Dish.VolumeMl < paused && heat.InitialMl > filter.Dish.VolumeMl,
            "Uncapping and reigniting resumes the partially concentrated solution");
        Command("retry_liquid_stage"); Tick(0.1f);
        Check(Mathf.Abs(filter.Dish.VolumeMl - 99.8f) < 0.02f && heat.EvaporatedMl == 0f && heat.DishMounted &&
            heat.LampMounted && filter.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Completed && sequence.CurrentStepIndex == 6,
            "Heating retry restores filtrate and an unlit capped lamp without repeating filtration");
        Check(heat.Lamp.GetComponent<SemanticObject>().StateTags.Contains("capped") &&
            heat.Cap.GetComponent<SemanticObject>().StateTags.Contains("installed"), "Heating retry restores the capped object catalog state");
        TakeOffCap(); Ignite(); Tick(heat.HeatingSeconds + 1f);
        Check(heat.Stage == CopperSulfateHeatingDetector.HeatingStage.AwaitingExtinguish && heat.Lit &&
            Mathf.Abs(filter.Dish.VolumeMl - heat.TargetMl) < 0.01f && heat.Progress > 0.999f,
            "Accelerated heating reaches half the initial volume and waits for manual extinguishing");
        Tick(3f); Check(Mathf.Abs(filter.Dish.VolumeMl - heat.TargetMl) < 0.01f, "The training simulation does not boil the dish dry while awaiting the cap");
        Command("next_step"); Command("complete_trial");
        Check(sequence.CurrentStepIndex == 6 && sequence.IsRunning, "Web commands cannot skip extinguishing the lamp");
        Cover();
        Check(heat.Stage == CopperSulfateHeatingDetector.HeatingStage.Cooling && sequence.IsRunning && !heat.Lit && !heat.Flame.activeSelf &&
            !heat.Crystals.activeSelf && heat.CoolingRemainingSeconds > 4.8f,
            "Capping after concentration starts a five-second wait instead of completing immediately");
        Command("next_step"); Command("complete_trial");
        Check(sequence.CurrentStepIndex == 6 && sequence.IsRunning, "Web commands cannot skip the crystallization wait");
        Tick(2f); Command("retry_liquid_stage");
        Check(heat.Stage == CopperSulfateHeatingDetector.HeatingStage.Ready && heat.CoolingRemainingSeconds == 0f &&
            !heat.Crystals.activeSelf && heat.Cap.enabled && filter.Dish.Grab.enabled && heat.EvaporatedMl == 0f,
            "Retry during cooling cancels the pending crystal appearance and restores the heating apparatus");
        Tick(6f);
        Check(!heat.Crystals.activeSelf && sequence.IsRunning, "A cancelled cooling countdown cannot complete the experiment later");
        TakeOffCap(); Ignite(); Tick(heat.HeatingSeconds + 1f); Cover(); Release(left); Release(right);
        Check(!left.hasSelection && !right.hasSelection && heat.Stage == CopperSulfateHeatingDetector.HeatingStage.Cooling,
            "Cooling proceeds with both hands released");
        heat.Tick(heat.CoolingRemainingSeconds - 0.1f);
        Check(!heat.Crystals.activeSelf && !sequence.IsComplete && heat.CoolingRemainingSeconds > 0f,
            "Crystals remain hidden until the full five-second delay has elapsed");
        heat.Tick(heat.CoolingRemainingSeconds);
        Check(heat.Stage == CopperSulfateHeatingDetector.HeatingStage.Completed && sequence.IsComplete && !heat.Lit && !heat.Flame.activeSelf &&
            heat.Crystals.activeSelf && heat.CoolingRemainingSeconds == 0f && heat.Cap.enabled && filter.Dish.Grab.enabled &&
            heat.GetComponent<ExperimentStateController>().CurrentState == ExperimentTrialState.Completed,
            "At five seconds crystals appear and the trial completes automatically without another hand action");
        Check(Mathf.Abs(filter.Dish.VolumeMl + heat.EvaporatedMl - heat.InitialMl) < 0.01f &&
            filter.Dish.GetComponent<SemanticObject>().StateTags.Contains("crystallized") &&
            filter.Dish.GetComponent<SemanticObject>().Contents == "copper_sulfate_crystals_and_mother_liquor",
            "Crystallization preserves the remaining mother liquor and updates the material catalog");
        Tick(6f);
        Check(sequence.IsComplete && heat.Crystals.activeSelf, "Completed crystals remain visible without repeating the final step");
        focus = filter.Dish.Mouth; eye = focus + new Vector3(0.07f, 0.12f, -0.18f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateHeating.crystals.png", eye, Quaternion.LookRotation(focus - eye));
        Check(!heat.RetryCurrentStage(), "Completed heating cannot retry a finished experiment");
    }
}
