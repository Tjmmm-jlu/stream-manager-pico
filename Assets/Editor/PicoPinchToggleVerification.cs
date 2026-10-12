using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[InitializeOnLoad]
public static class PicoPinchToggleVerification
{
    private const string RequestPath = "Temp/PicoPinchHold.request";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _busy;
    static PicoPinchToggleVerification() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
            if (command == "install")
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.path != CopperSulfateLiquidInstaller.ScenePath) throw new InvalidOperationException("Open the stationary scene.");
                string backup = "MigrationBackups/BeforePinchHoldInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
                if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
                if (!PicoHandPinchGrabBootstrap.Configure(scene)) throw new InvalidOperationException("Hand setup failed.");
                foreach (var input in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PicoStablePinchInputReader>(true)))
                {
                    var settings = new SerializedObject(input);
                    settings.FindProperty("pressDwellSeconds").floatValue = 0.05f;
                    settings.FindProperty("releaseGraceSeconds").floatValue = 0.06f;
                    settings.FindProperty("pinchDistance").floatValue = 0.025f;
                    settings.FindProperty("openDistance").floatValue = 0.035f;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save pinch-hold configuration.");
                File.WriteAllText("Temp/PicoPinchHold.result.txt", "Installed pinch-hold input. Backup: " + backup);
            }
            else if (command == "verify")
            {
                Run();
                CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/PicoPinchHold.result.txt", "PASS: pinch hold, open-finger release, return, and liquid/filtration regression.");
            }
            else if (command == "build")
            {
                const string path = "Builds/CopperSulfate-PinchHoldRelease.apk";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { CopperSulfateLiquidInstaller.ScenePath }, locationPathName = path,
                    target = BuildTarget.Android, options = BuildOptions.Development });
                File.WriteAllText("Temp/PicoPinchHold.build.txt", report.summary.result + "\n" + path + "\nErrors: " + report.summary.totalErrors);
            }
            else throw new InvalidOperationException("Unknown pinch-hold command: " + command);
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/PicoPinchHold.result.txt", error.ToString()); Debug.LogException(error);
        }
        finally { _busy = false; }
    }

    internal static void EnableReturns(Scene scene)
    {
        foreach (var grab in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<XRGrabInteractable>(true)))
        {
            if (grab.GetComponentInParent<SemanticObject>() == null) continue;
            var restore = grab.GetComponent<ExperimentGrabReturn>() ?? grab.gameObject.AddComponent<ExperimentGrabReturn>();
            restore.Initialize();
            Invoke(restore, "OnDisable"); Invoke(restore, "OnEnable");
        }
    }

    internal static void FlushReturns(Scene scene)
    {
        foreach (var restore in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ExperimentGrabReturn>(true)))
        {
            restore.GetComponent<XRGrabInteractable>().ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Late);
            Invoke(restore, "LateUpdate");
        }
        Physics.SyncTransforms();
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Verify Pinch Hold And Release")]
    public static void Run()
    {
        string source = CopperSulfateLiquidInstaller.ScenePath;
        byte[] before = File.ReadAllBytes(source);
        string copy = "Assets/Editor/PinchVerification_" + Guid.NewGuid().ToString("N") + ".unity";
        var previous = SceneManager.GetActiveScene();
        var report = new List<string>();
        if (!AssetDatabase.CopyAsset(source, copy)) throw new IOException("Could not copy scene.");
        Scene scene = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
        try
        {
            void Check(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException("FAIL: " + description);
                report.Add("PASS: " + description);
            }
            Check(PicoHandPinchGrabBootstrap.Configure(scene), "Runtime bootstrap configures the saved stationary scene");
            EnableReturns(scene);
            var roots = scene.GetRootGameObjects();
            var mix = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateMixingDetector>(true)).Single();
            var grab = mix.Cylinder.Grab;
            var home = grab.transform.position; var rotation = grab.transform.rotation;
            var managerObject = new GameObject("Pinch Verification Manager"); SceneManager.MoveGameObjectToScene(managerObject, scene);
            var manager = managerObject.AddComponent<XRInteractionManager>(); Invoke(manager, "Awake");
            var hands = roots.SelectMany(r => r.GetComponentsInChildren<XRDirectInteractor>(true))
                .Where(h => h.GetComponent<PicoStablePinchInputReader>() != null).ToArray();
            Check(hands.Length == 2 && hands.All(h => h.selectActionTrigger == XRBaseInputInteractor.InputTriggerType.State),
                "Both hands hold on pinch and release on finger opening");
            foreach (var hand in hands)
            {
                hand.interactionManager = manager; Invoke(hand, "Awake");
                var input = hand.GetComponent<PicoStablePinchInputReader>();
                Invoke(input, "OnDisable"); Invoke(input, "OnEnable");
                hand.PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                var unused = hand.isSelectActive;
            }
            grab.interactionManager = manager; Invoke(grab, "Awake");
            var left = hands.Single(h => h.handedness == InteractorHandedness.Left);
            var right = hands.Single(h => h.handedness == InteractorHandedness.Right);
            float time = 0f;
            void Sample(XRDirectInteractor hand, float distance, bool tracked = true, float elapsed = 0.02f)
            {
                time += elapsed;
                Invoke(hand.GetComponent<PicoStablePinchInputReader>(), "SamplePinch", distance, tracked, time);
                hand.PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
                Invoke(manager, "ClearInteractorSelection", (IXRSelectInteractor)hand, new List<IXRInteractable> { grab });
                FlushReturns(scene);
            }
            void Open(XRDirectInteractor hand)
            {
                Sample(hand, 0.065f); Sample(hand, 0.065f, true, 0.08f);
            }
            void Pinch(XRDirectInteractor hand)
            {
                Sample(hand, 0.015f); Sample(hand, 0.015f, true, 0.06f);
            }
            bool CanGrab(XRDirectInteractor hand) => hand.isSelectActive && manager.IsSelectPossible(hand, grab);
            void Grab(XRDirectInteractor hand) => manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);

            Pinch(left);
            Check(!CanGrab(left), "Starting or recovering with closed fingers cannot select on contact");
            Open(left);
            Check(!CanGrab(left), "An open hand touching a valid apparatus cannot grab it");
            Sample(left, 0.03f, true, 0.2f);
            Check(!CanGrab(left), "Separated fingertips in the hysteresis band cannot start a grab");
            Open(left); Sample(left, 0.015f); Sample(left, 0.015f, true, 0.02f);
            Check(!CanGrab(left), "A brief pinch spike cannot grab");
            Open(left); Pinch(left);
            Check(CanGrab(left), "A stable fingertip pinch permits pickup without an SDK strength requirement");
            Sample(left, 0.015f, true, 0.3f);
            Check(CanGrab(left), "Pinching before reaching a target remains eligible after the press frame");
            Grab(left); grab.transform.position += Vector3.up * 0.3f; mix.Cylinder.SetVolume(99.8f);
            Sample(left, 0.031f, true, 0.2f);
            Check(left.isSelectActive && grab.isSelected, "Small fingertip jitter keeps the current apparatus held");
            Sample(left, 0.037f); Sample(left, 0.037f); Sample(left, 0.025f);
            Check(left.isSelectActive && grab.isSelected, "A short opening spike does not drop the apparatus");
            Open(right); Pinch(right);
            Check(!CanGrab(right), "The other hand cannot steal a selected apparatus");
            Sample(left, 0f, false); Sample(left, float.NaN, true, 0.3f);
            Check(left.isSelectActive && grab.isSelected, "Tracking loss does not become a release gesture");
            Pinch(left);
            Check(left.isSelectActive && grab.isSelected, "Tracking recovery with closed fingers keeps the same selection");
            Open(left);
            Check(!left.isSelectActive && !grab.isSelected && !left.hasSelection,
                "Opening the fingertips automatically releases through the XRI manager without another pinch");
            Check(Vector3.Distance(grab.transform.position, home) < 0.0001f && Quaternion.Angle(grab.transform.rotation, rotation) < 0.01f,
                "Release restores the original position and orientation");
            Check(Mathf.Abs(mix.Cylinder.VolumeMl - 99.8f) < 0.001f, "Return preserves the measured 99.8 mL");
            Check(!CanGrab(left), "An open released hand cannot immediately pick up another object");
            Sample(left, 0f, false); Pinch(left);
            Check(!CanGrab(left), "Tracking recovery with closed fingers and no selection cannot acquire an object");
            Open(left); Pinch(left); Check(CanGrab(left), "Opening and pinching again rearms pickup");
            Grab(left); grab.transform.position += Vector3.right * 0.25f;
            var assemblyPose = grab.transform.position;
            manager.CancelInteractableSelection((IXRSelectInteractable)grab); FlushReturns(scene);
            Check(Vector3.Distance(grab.transform.position, assemblyPose) < 0.0001f,
                "Programmatic assembly cancellation does not trigger return");
            Sample(left, 0.015f, true, 0.3f);
            Check(!CanGrab(left), "Assembly cancellation blocks regrabbing while the same pinch is still held");
            Open(left); Pinch(left);
            Check(CanGrab(left), "Opening after mounting permits a fresh pickup pinch");
            Open(right); Pinch(right); Check(CanGrab(right), "Right hand can independently pick up a free apparatus");
            Grab(right); Open(right);
            Check(!right.isSelectActive && !grab.isSelected, "Right hand also releases automatically on finger opening");
            Open(left); Pinch(left); Grab(left); Open(left);
            Check(Vector3.Distance(grab.transform.position, home) < 0.0001f, "Return home is stable across repeated and alternate-hand grabs");
            Check(grab.GetComponent<Rigidbody>().velocity.sqrMagnitude < 0.0001f && !grab.throwOnDetach,
                "Returned apparatus has no throw velocity");
            report.Add("Scope: simulated tracking samples and targets through real XRI selection; PICO gesture comfort still requires hardware validation.");
        }
        catch (Exception error) { report.Add(error.ToString()); throw; }
        finally
        {
            foreach (var input in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PicoStablePinchInputReader>(true)))
                Invoke(input, "OnDisable");
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); AssetDatabase.DeleteAsset(copy);
            report.Add(before.SequenceEqual(File.ReadAllBytes(source)) ? "PASS: Saved scene unchanged" : "FAIL: Saved scene changed");
            File.WriteAllLines("Temp/PicoPinchHold.verification.txt", report);
        }
    }

    private static void Invoke(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private).Invoke(target, args);
}
