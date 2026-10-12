using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// 使用 XRI 人工输入验证区域布局、干扰物释放归位和流程重置。
[InitializeOnLoad]
public static class RegionRatioLowRuntimeVerification
{
    private const string Pending = "RegionRatioLow.Verifying";
    private static string Report => SessionState.GetString(Pending + ".report", RegionRatioLowSceneInstaller.Output + "/runtime.txt");
    private static int _index = -1, _frame, _phase;
    private static double _deadline;
    private static ExperimentPairedLayoutController _layout;
    private static XRDirectInteractor _hand;
    private static SemanticObject[] _relocatedTasks;
    private static Pose[] _taskHomes;
    static RegionRatioLowRuntimeVerification()
    {
        EditorApplication.playModeStateChanged += mode => {
            if (!SessionState.GetBool(Pending, false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            { _index = 0; _phase = 0; _frame = Time.frameCount; _deadline = EditorApplication.timeSinceStartup + 60; }
            if (mode == PlayModeStateChange.EnteredEditMode)
            { SessionState.SetBool(Pending, false); _index = -1; File.AppendAllText(Report, "Returned to Edit Mode without saving runtime state.\n"); }
        };
        EditorApplication.update += Tick;
    }
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save the scene and exit Play Mode first.");
        var layout = UnityEngine.Object.FindObjectOfType<ExperimentPairedLayoutController>();
        string output = layout != null && layout.ProfileId == RegionRatioHighSceneInstaller.Profile
            ? RegionRatioHighSceneInstaller.Output : RegionRatioLowSceneInstaller.Output;
        Directory.CreateDirectory(output);
        SessionState.SetString(Pending + ".report", output + "/runtime.txt");
        SessionState.SetBool(Pending, true);
        File.WriteAllText(Report, "RUNNING: synthetic XRI input, not a headset test.\n");
        EditorApplication.isPlaying = true;
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Tick()
    {
        if (_index < 0 || !EditorApplication.isPlaying) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < _deadline, "Runtime verification timed out.");
            if (Time.frameCount < _frame + 3) return;
            if (_phase == 0)
            {
                _layout = UnityEngine.Object.FindObjectOfType<ExperimentPairedLayoutController>();
                Require(_layout != null && (_layout.ProfileId == RegionRatioLowSceneInstaller.Profile ||
                    _layout.ProfileId == RegionRatioHighSceneInstaller.Profile), "Regional profile did not start.");
                Require(SceneObjectRegistry.Instance.Count == 13 + _layout.ActiveCount, "Incorrect runtime counts.");
                if (_layout.HighAvailable)
                {
                    Require(_layout.TryApply(ExperimentLayoutSelection.Low, "verification", null, out _), "Low switch failed.");
                    Require(SceneObjectRegistry.Instance.Count == 52 && _layout.ActiveCount == 39 &&
                        _layout.Items.Where(i => !i.shared).All(i => !i.gameObject.activeInHierarchy), "Low catalog or visibility mismatch.");
                    Require(_layout.TryApply(ExperimentLayoutSelection.High, "verification", null, out _), "High switch failed.");
                    Require(SceneObjectRegistry.Instance.Count == 91 && _layout.ActiveCount == 78, "High catalog mismatch.");
                    File.AppendAllText(Report, "PASS: low/high switch, 52/91 registry counts and high-only visibility.\n");
                }
                else Require(SceneObjectRegistry.Instance.Count == 52 && _layout.ActiveCount == 39, "Incorrect low counts.");
                var rays = UnityEngine.Object.FindObjectsOfType<XRRayInteractor>();
                Require(rays.Length == 2, "Two controller ray components not present.");
                File.AppendAllText(Report, "PASS: two controller ray components retained. Active=" + rays.Count(r => r.enabled) +
                    "; tracking/visibility requires the headset and is not tested here.\n");
                // 无真机跟踪时，正式逻辑会关闭射线；人工 XRI 检查不依赖跟踪状态。
                foreach (var tracking in UnityEngine.Object.FindObjectsOfType<PicoControllerRayTracking>()) tracking.enabled = false;
                _relocatedTasks = UnityEngine.Object.FindObjectsOfType<SemanticObject>()
                    .Where(s => new[] { "filtrate_beaker", "china_dish", "spirit_lamp", "lamp_cap" }.Contains(s.StableId)).ToArray();
                Require(_relocatedTasks.Length == 4, "Relocated task objects missing.");
                _taskHomes = _relocatedTasks.Select(s => new Pose(s.transform.position, s.transform.rotation)).ToArray();
                if (!_layout.HighAvailable)
                    Require(!_layout.TryApply(ExperimentLayoutSelection.High, "verification", null, out _), "Unbuilt high accepted.");
                foreach (var interactor in UnityEngine.Object.FindObjectsOfType<XRBaseInteractor>()) interactor.enabled = false;
                var go = new GameObject("Regional verification hand"); go.AddComponent<SphereCollider>().isTrigger = true;
                var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                _hand = go.AddComponent<XRDirectInteractor>(); _phase = 1; _frame = Time.frameCount; return;
            }
            if (_phase == 1)
            {
                var item = _layout.Items[_index]; var grab = item.gameObject.GetComponent<XRGrabInteractable>();
                _hand.transform.position = grab.transform.position;
                _hand.StartManualInteraction((IXRSelectInteractable)grab);
                Require(grab.isSelected, "XRI grab failed: " + item.gameObject.name);
                Require(!_layout.TryApply(ExperimentLayoutSelection.Low, "verification", null, out _), "Layout change while held accepted.");
                grab.transform.position += Vector3.up * 0.15f; _hand.EndManualInteraction();
                _phase = 2; _frame = Time.frameCount; return;
            }
            var current = _layout.Items[_index];
            Require(Vector3.Distance(current.gameObject.transform.localPosition, current.homePosition) < 0.002f,
                "Return failed: " + current.gameObject.GetComponent<SemanticObject>().StableId);
            File.AppendAllText(Report, "PASS: grab and return " + current.gameObject.GetComponent<SemanticObject>().StableId + "\n");
            _index++;
            if (_index < _layout.Items.Count) { _phase = 1; _frame = Time.frameCount; return; }
            _hand.enabled = false;
            var sequence = UnityEngine.Object.FindObjectOfType<PreparationSequenceController>();
            Require(sequence.StartSequence(), "Continuous task did not start.");
            Require(!_layout.TryApply(ExperimentLayoutSelection.Low, "verification", null, out _), "Running task did not lock layout.");
            foreach (var item in _layout.Items) item.gameObject.transform.position += Vector3.up * 0.1f;
            foreach (var item in _relocatedTasks.Where(s => s.StableId != "lamp_cap")) item.transform.position += Vector3.up * 0.1f;
            sequence.ResetSequence(); UnityEngine.Object.FindObjectOfType<ExperimentStateController>().ResetTrial();
            Require(_layout.Items.All(i => Vector3.Distance(i.gameObject.transform.localPosition, i.homePosition) < 0.002f), "Reset failed.");
            for (int i = 0; i < _relocatedTasks.Length; i++)
                Require(Vector3.Distance(_relocatedTasks[i].transform.position, _taskHomes[i].position) < 0.002f &&
                    Quaternion.Angle(_relocatedTasks[i].transform.rotation, _taskHomes[i].rotation) < 0.1f,
                    "Relocated task reset failed: " + _relocatedTasks[i].StableId);
            Require(SceneObjectRegistry.Instance.Count == 13 + _layout.Items.Count, "Reset changed registry.");
            if (_layout.HighAvailable)
            {
                Require(_layout.TryApply(ExperimentLayoutSelection.Low, "verification", null, out _) && SceneObjectRegistry.Instance.Count == 52,
                    "Low switch after reset failed.");
                Require(_layout.TryApply(ExperimentLayoutSelection.High, "verification", null, out _) && SceneObjectRegistry.Instance.Count == 91,
                    "High switch after reset failed.");
            }
            File.AppendAllText(Report, "PASS: all " + _layout.Items.Count + " grabs/releases, catalog, task lock, distractor reset and four relocated task poses.\n"); Finish();
        }
        catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e + "\n"); Debug.LogException(e); Finish(); }
    }
    private static void Finish() { _index = -1; EditorApplication.isPlaying = false; }
}
