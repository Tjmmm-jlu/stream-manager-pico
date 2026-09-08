using UnityEditor;
using UnityEngine;

public sealed class CopperSulfateTransferDebugWindow : EditorWindow
{
    private bool _held;

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Transfer Debugger")]
    private static void Open() { GetWindow<CopperSulfateTransferDebugWindow>("Copper Transfer"); }

    private void OnGUI()
    {
        var detector = FindObjectOfType<CopperSulfateTransferDetector>();
        if (detector == null || !EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode in the copper sulfate scene.", MessageType.Info);
            return;
        }
        var sequence = detector.GetComponent<PreparationSequenceController>();
        var state = detector.GetComponent<ExperimentStateController>();
        EditorGUILayout.LabelField("Stage", detector.Stage.ToString());
        EditorGUILayout.LabelField("Step", sequence.CurrentStep?.id ?? "Idle");
        EditorGUILayout.Slider("Dwell", detector.Progress, 0f, 1f);
        if (GUILayout.Button("Start / Restart"))
        {
            state?.StartTrial("editor-debug-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), "editor_debug");
            sequence.StartSequence();
            _held = false;
        }
        bool held = EditorGUILayout.Toggle("Simulate forceps held", _held);
        if (held != _held) { _held = held; detector.EditorSetHeld(_held); }
        if (GUILayout.Button("Move tip to crystals")) MoveTip(detector, detector.Pickup.position);
        if (GUILayout.Button("Move tip to beaker opening")) MoveTip(detector, detector.Mouth.position + detector.Mouth.up * 0.025f);
        if (GUILayout.Button("Move tip away")) MoveTip(detector, detector.Mouth.position + Vector3.up * 0.3f);
        if (GUILayout.Button("Reset"))
        {
            sequence.ResetSequence();
            state?.ResetTrial();
            _held = false;
        }
        Repaint();
    }

    private static void MoveTip(CopperSulfateTransferDetector detector, Vector3 destination)
    {
        detector.Forceps.transform.position += destination - detector.Tip.position;
    }

    private void OnDisable()
    {
        var detector = FindObjectOfType<CopperSulfateTransferDetector>();
        if (detector != null) detector.EditorSetHeld(false);
    }
}
