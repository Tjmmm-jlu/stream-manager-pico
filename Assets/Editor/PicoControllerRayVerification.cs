using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using Unity.XR.PXR.Input;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Exercise the actual PICO Input System layout and XRI ray selection, without manual SelectEnter.
[InitializeOnLoad]
public static class PicoControllerRayVerification
{
    private const string Pending = "ControllerRayVerification.Pending";
    private static readonly List<string> Report = new List<string>();
    private static int _phase = -1;
    private static double _next, _deadline;
    private static PXR_Controller _leftDevice, _rightDevice;
    private static XRRayInteractor _left, _right;
    private static XRGrabInteractable _a, _b;
    private static Vector3 _homeA, _homeB;
    private static GameObject _blocker;
    private static float _distance;
    private static Quaternion _rotation;
    private static XRGrabInteractable _apparatus;
    private static Vector3 _apparatusHome;

    static PicoControllerRayVerification()
    {
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Stream Manager/Controller Rays/Verify Input In Play Mode")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != PicoControllerRayInstaller.ScenePath)
            throw new InvalidOperationException("Save the LargeTables scene and exit Play Mode first.");
        Directory.CreateDirectory(PicoControllerRayInstaller.Output);
        File.WriteAllText(PicoControllerRayInstaller.Output + "/verification.txt", "RUNNING\n");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Report.Clear(); _phase = 0;
            _next = EditorApplication.timeSinceStartup + 1;
            _deadline = _next + 45;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false); _phase = -1;
            File.AppendAllText(PicoControllerRayInstaller.Output + "/verification.txt", "Returned to Edit Mode; runtime changes discarded.\n");
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Report.Add("PASS: " + message);
    }

    private static void Pose(PXR_Controller device, Vector3 position, Quaternion rotation)
    {
        InputSystem.QueueDeltaStateEvent(device.devicePosition, position);
        InputSystem.QueueDeltaStateEvent(device.deviceRotation, rotation);
        InputSystem.QueueDeltaStateEvent(device.trackingState, 3);
        Button(device.isTracked, 1);
    }

    private static void Button(ButtonControl control, float value)
    {
        // PICO buttons are bitfields; use the control serializer, not QueueDeltaStateEvent.
        using (DeltaStateEvent.From(control, out var stateEvent))
        {
            control.WriteValueIntoEvent(value, stateEvent);
            InputSystem.QueueEvent(stateEvent);
        }
    }

    private static void Grip(PXR_Controller device, float value)
    {
        InputSystem.QueueDeltaStateEvent(device.grip, value);
        Button(device.gripPressed, value);
    }

    private static XRGrabInteractable Target(string name, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.position = position; go.transform.localScale = Vector3.one * 0.16f;
        var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var grab = go.AddComponent<XRGrabInteractable>();
        grab.useDynamicAttach = true; grab.matchAttachRotation = true;
        go.AddComponent<ExperimentGrabReturn>();
        return grab;
    }

    private static void Tick()
    {
        if (_phase < 0 || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Ray input verification timed out.");
            switch (_phase)
            {
                case 0:
                    PicoControllerRayInstaller.Validate(SceneManager.GetActiveScene());
                    Check(true, "Saved scene has two handed rays, material, no active hands/direct interaction");
                    var rays = UnityEngine.Object.FindObjectsOfType<XRRayInteractor>(true);
                    _left = rays.Single(r => r.handedness == InteractorHandedness.Left);
                    _right = rays.Single(r => r.handedness == InteractorHandedness.Right);
                    InputSystem.RegisterLayout<PXR_Controller>();
                    _leftDevice = InputSystem.AddDevice<PXR_Controller>();
                    _rightDevice = InputSystem.AddDevice<PXR_Controller>();
                    InputSystem.SetDeviceUsage(_leftDevice, CommonUsages.LeftHand);
                    InputSystem.SetDeviceUsage(_rightDevice, CommonUsages.RightHand);
                    Pose(_leftDevice, new Vector3(-0.3f, 10, 0), Quaternion.identity);
                    Pose(_rightDevice, new Vector3(0.3f, 10, 0), Quaternion.identity);
                    break;
                case 1:
                    Check(_left.enabled && _right.enabled, "PICO tracked controls enable both rays");
                    Check(Vector3.Distance(_left.transform.parent.localPosition, new Vector3(-0.3f, 10, 0)) < 0.01f,
                        "Device position input drives controller transform");
                    _homeA = _left.transform.position + _left.transform.forward * 2f;
                    _homeB = _right.transform.position + _right.transform.forward * 2f;
                    _a = Target("Left ray verification target", _homeA);
                    _b = Target("Right ray verification target", _homeB);
                    break;
                case 2:
                    Check(_left.interactablesHovered.Contains(_a) && _right.interactablesHovered.Contains(_b), "Both rays hover objects two metres away");
                    Check(!_a.isSelected && !_b.isSelected, "Pointing without Grip does not grab");
                    Check(_left.GetComponent<LineRenderer>().enabled && _left.GetComponent<LineRenderer>().positionCount >= 2,
                        "Tracked ray generates visible line geometry");
                    CopperSulfateLiquidVerification.Render(SceneManager.GetActiveScene(),
                        PicoControllerRayInstaller.Output + "/rays.png",
                        (_left.transform.position + _right.transform.position) * 0.5f - _left.transform.forward * 0.45f + Vector3.up * 0.15f,
                        _left.transform.rotation);
                    Button(_leftDevice.triggerPressed, 1);
                    break;
                case 3:
                    Check(!_a.isSelected, "Trigger does not accidentally grab apparatus");
                    Button(_leftDevice.isTracked, 0);
                    Button(_leftDevice.triggerPressed, 0);
                    Grip(_leftDevice, 1);
                    break;
                case 4:
                    Check(_left.enabled && _left.GetComponent<LineRenderer>().enabled,
                        "Valid position/rotation keeps ray visible even when isTracked is not asserted");
                    Check(_a.interactorsSelecting.Contains(_left) && !_b.isSelected, "Left Grip independently selects through ray input");
                    Check(Vector3.Distance(_a.transform.position, _left.transform.position) < 0.5f, "Distant object moves to controller hold point");
                    Grip(_rightDevice, 1);
                    break;
                case 5:
                    Check(_b.interactorsSelecting.Contains(_right) && LiquidPourInteractor.OppositeHands(_a, _b), "Both rays satisfy two-hand chemistry predicates");
                    Check(LiquidPourInteractor.HeldByHand(_a), "Ray selection satisfies apparatus-held predicate");
                    _distance = Vector3.Distance(_left.attachTransform.position, _left.transform.position);
                    InputSystem.QueueDeltaStateEvent(_leftDevice.thumbstick, Vector2.up);
                    break;
                case 6:
                    Check(Vector3.Distance(_left.attachTransform.position, _left.transform.position) > _distance + 0.02f, "Thumbstick moves held object along ray");
                    InputSystem.QueueDeltaStateEvent(_leftDevice.thumbstick, Vector2.zero);
                    _rotation = _a.transform.rotation;
                    Pose(_leftDevice, new Vector3(-0.3f, 10, 0), Quaternion.Euler(0, 0, 65));
                    break;
                case 7:
                    Check(Quaternion.Angle(_rotation, _a.transform.rotation) > 45, "Controller rotation rotates held apparatus for pouring");
                    Grip(_leftDevice, 0);
                    break;
                case 8:
                    Check(!_a.isSelected && _b.isSelected, "Releasing left Grip keeps right-hand object held");
                    Check(Vector3.Distance(_a.transform.position, _homeA) < 0.01f, "Release returns loose apparatus home");
                    Button(_rightDevice.isTracked, 0);
                    InputSystem.QueueDeltaStateEvent(_rightDevice.trackingState, 0);
                    break;
                case 9:
                    Check(!_right.enabled && !_b.isSelected && !_right.GetComponent<LineRenderer>().enabled,
                        "Tracking loss releases object and hides that ray");
                    Check(Vector3.Distance(_b.transform.position, _homeB) < 0.01f, "Tracking-loss release returns apparatus home");
                    Grip(_rightDevice, 0);
                    Pose(_leftDevice, new Vector3(-0.3f, 10, 0), Quaternion.identity);
                    _blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _blocker.transform.position = (_homeA + _left.transform.position) * 0.5f;
                    _blocker.transform.localScale = Vector3.one * 0.3f;
                    break;
                case 10:
                    Check(!_left.interactablesHovered.Contains(_a), "Solid obstruction blocks target behind it");
                    UnityEngine.Object.Destroy(_blocker);
                    break;
                case 11:
                    Check(_left.interactablesHovered.Contains(_a), "Target is reachable after obstruction is removed");
                    Grip(_leftDevice, 1);
                    break;
                case 12:
                    Check(_a.isSelected, "Re-grabbing after release works");
                    Grip(_leftDevice, 0);
                    break;
                case 13:
                    Check(!_a.isSelected, "Second release works");
                    _a.gameObject.SetActive(false);
                    _apparatus = UnityEngine.Object.FindObjectOfType<CopperSulfateMixingDetector>().Beaker.Grab;
                    _apparatusHome = _apparatus.transform.position;
                    _apparatus.transform.position = _homeA;
                    Physics.SyncTransforms();
                    var collider = _apparatus.colliders.First(c => c.enabled && !c.isTrigger);
                    _apparatus.transform.position += _homeA - collider.bounds.center;
                    Physics.SyncTransforms();
                    break;
                case 14:
                    _left.TryGetCurrent3DRaycastHit(out var apparatusHit);
                    Check(_left.interactablesHovered.Contains(_apparatus), "Ray hits the actual mixing beaker collider at a distance" +
                        $" (hit={apparatusHit.collider?.name}, ray={_left.transform.position}, beaker={_apparatus.transform.position})");
                    Grip(_leftDevice, 1);
                    break;
                case 15:
                    Check(_apparatus.interactorsSelecting.Contains(_left) && LiquidPourInteractor.HeldByHand(_apparatus),
                        "Actual mixing beaker can be held by a ray");
                    Grip(_leftDevice, 0);
                    break;
                case 16:
                    Check(!_apparatus.isSelected && Vector3.Distance(_apparatus.transform.position, _apparatusHome) < 0.01f,
                        "Actual mixing beaker returns to its laboratory position after Grip release");
                    Finish(null);
                    return;
            }
            _phase++;
            _next = EditorApplication.timeSinceStartup + 0.65;
        }
        catch (Exception ex) { Finish(ex); }
    }

    private static void Finish(Exception error)
    {
        if (error != null) Report.Add("FAIL: " + error);
        else Report.Add("PASS: synthetic PICO input verification complete; hardware not tested.");
        File.WriteAllLines(PicoControllerRayInstaller.Output + "/verification.txt", Report);
        _phase = -1;
        if (_leftDevice != null && _leftDevice.added) InputSystem.RemoveDevice(_leftDevice);
        if (_rightDevice != null && _rightDevice.added) InputSystem.RemoveDevice(_rightDevice);
        EditorApplication.isPlaying = false;
    }
}
