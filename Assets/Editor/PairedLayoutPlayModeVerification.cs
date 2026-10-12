using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[InitializeOnLoad]
public static class PairedLayoutPlayModeVerification
{
    private const string Pending = "PairedLayoutVerification.Pending";
    private const string ReportPath = "Temp/PairedLayout/runtime-verification.txt";
    private static readonly List<string> Report = new List<string>();
    private static int _phase = -1;
    private static int _frame;
    private static double _deadline;
    private static ExperimentPairedLayoutController _layout;
    private static SceneObjectRegistry _registry;
    private static ExperimentStateController _state;
    private static PreparationSequenceController _sequence;
    private static ExperimentPairedLayoutController.Item _item;
    private static XRDirectInteractor _hand;

    static PairedLayoutPlayModeVerification()
    {
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Stream Manager/Two Level Layout/Verify Runtime In Play Mode")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
            scene.path != CopperSulfatePairedLayoutInstaller.ScenePath)
            throw new InvalidOperationException("Save the large-table scene and exit Play Mode first.");
        SessionState.SetBool(Pending, true);
        Directory.CreateDirectory("Temp/PairedLayout");
        File.WriteAllText(ReportPath, "RUNNING: entering Play Mode.\n");
        EditorApplication.isPlaying = true;
    }

    private static void Changed(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            Report.Clear();
            _phase = 0;
            _frame = Time.frameCount;
            _deadline = EditorApplication.timeSinceStartup + 30;
        }
        else if (mode == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            _phase = -1;
            File.AppendAllText(ReportPath, "Returned to Edit Mode; scene was not saved by this test.\n");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.Add("PASS: " + message);
    }

    private static void Apply(ExperimentLayoutSelection level)
    {
        Check(_layout.TryApply(level, "play_mode_verification", null, out string error), error ?? "Apply " + level);
    }

    private static void Tick()
    {
        if (_phase < 0 || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Play Mode verification timed out.");
            if (Time.frameCount < _frame + 3) return;
            if (_phase == 0)
            {
                _layout = UnityEngine.Object.FindObjectOfType<ExperimentPairedLayoutController>();
                _registry = UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>();
                _state = UnityEngine.Object.FindObjectOfType<ExperimentStateController>();
                _sequence = UnityEngine.Object.FindObjectOfType<PreparationSequenceController>();
                Check(_layout != null && _registry != null && _state != null && _sequence != null, "Runtime controllers loaded");
                Check(!_layout.IsLocked && _layout.Selection == ExperimentLayoutSelection.Low && _registry.Count == 19,
                    "Saved scene starts in low with 19 visible catalog objects");
                Apply(ExperimentLayoutSelection.High);
                Check(_registry.Count == 31, "High activation registers 31 objects");
                _state.StartTrial("layout-smoke-test", "verification");
                Check(!_layout.TryApply(ExperimentLayoutSelection.Low, "test", null, out _) && _registry.Count == 31,
                    "Standalone active trial rejects layout changes without mutating catalog");
                _state.ResetTrial();
                foreach (var hand in UnityEngine.Object.FindObjectsOfType<XRBaseInteractor>()) hand.enabled = false;
                var go = new GameObject("Layout verification hand");
                go.AddComponent<SphereCollider>().isTrigger = true;
                var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                _hand = go.AddComponent<XRDirectInteractor>();
                _item = _layout.Items.First(item => !item.shared);
                var grab = _item.gameObject.GetComponent<XRGrabInteractable>();
                _hand.transform.position = grab.transform.position;
                _hand.StartManualInteraction((IXRSelectInteractable)grab);
                Check(grab.isSelected, "Newly activated high-only apparatus accepts an XRI grab");
                Check(!_layout.TryApply(ExperimentLayoutSelection.Low, "test", null, out _) && _registry.Count == 31,
                    "Holding an apparatus rejects layout changes");
                grab.transform.position += Vector3.up * 0.2f;
                _hand.EndManualInteraction();
                _phase = 1; _frame = Time.frameCount;
            }
            else if (_phase == 1)
            {
                Check(Vector3.Distance(_item.gameObject.transform.localPosition, _item.homePosition) < 0.001f,
                    "Ordinary XRI release returns the high-only apparatus home in LateUpdate");
                Check(_sequence.StartSequence(), "Existing continuous sequence starts");
                Check(!_layout.TryApply(ExperimentLayoutSelection.Low, "test", null, out _) && _registry.Count == 31,
                    "Running continuous sequence rejects layout changes");
                _item.gameObject.transform.position += Vector3.up * 0.3f;
                _sequence.ResetSequence(); _state.ResetTrial();
                Check(_layout.Selection == ExperimentLayoutSelection.High && _registry.Count == 31 &&
                    Vector3.Distance(_item.gameObject.transform.localPosition, _item.homePosition) < 0.001f,
                    "Sequence reset restores distractor poses and preserves selected complexity");
                Apply(ExperimentLayoutSelection.Low);
                Check(_registry.Count == 19 && !_registry.TryGet(_item.gameObject.GetComponent<SemanticObject>().StableId, out _),
                    "Low removes hidden high-only objects from candidate catalog");
                Apply(ExperimentLayoutSelection.High);
                Check(_registry.Count == 31, "Repeated activation does not duplicate catalog entries");
                var log = UnityEngine.Object.FindObjectOfType<ExperimentLogger>();
                string records = File.ReadAllText(log.LogFilePath);
                Check(records.Contains("layout_changed") && records.Contains("layout_reset") && records.Contains("large-tables-A"),
                    "Runtime log records layout profile, changes and resets");
                Report.Add("Log: " + log.LogFilePath);
                Report.Add("PASS: Runtime verification completed. Synthetic XRI input, not a headset test.");
                Finish();
            }
        }
        catch (Exception error)
        {
            Report.Add("FAIL: " + error);
            Finish();
        }
    }

    private static void Finish()
    {
        File.WriteAllLines(ReportPath, Report);
        _phase = -1;
        EditorApplication.isPlaying = false;
    }
}
