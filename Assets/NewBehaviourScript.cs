using Unity.RenderStreaming;
using UnityEngine;

public class StreamManager : MonoBehaviour
{
    [SerializeField] InputReceiver inputReceiver;
    [SerializeField] VideoStreamSender videoStreamSender;

    void Start()
    {
        Application.runInBackground = true;
        inputReceiver.OnStartedChannel += OnStartedChannel;
    }

    void OnStartedChannel(string connectionId)
    {
        CalculateInputRegion();
    }

    void Update()
    {
        CalculateInputRegion();
    }

    void CalculateInputRegion()
    {
        if (!inputReceiver.IsConnected) return;
        var width = (int)(videoStreamSender.width / videoStreamSender.scaleResolutionDown);
        var height = (int)(videoStreamSender.height / videoStreamSender.scaleResolutionDown);
        inputReceiver.CalculateInputRegion(new Vector2Int(width, height), new Rect(0, 0, width, height));
        inputReceiver.SetEnableInputPositionCorrection(false);
    }
}
