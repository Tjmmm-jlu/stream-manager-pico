using UnityEngine;
using UnityEngine.InputSystem;
using Unity.RenderStreaming;

public class WebObjectSelector : MonoBehaviour
{
    [Header("Input Actions")]
    public InputActionReference clickAction;
    public InputActionReference positionAction;

    [Header("Camera Requirements")]
    [Tooltip("����������븺������Ⱦ����ҳ���Ǹ� Camera��ǧ��Ҫ�� VR ��ҵ� Camera��")]
    public Camera renderStreamingCamera;

    [Header("Stream Resolution")]
    [Tooltip("������ VideoStreamSender ���õķֱ��ʱ�ֱ������������ InputReceiver �� InputRegion ƥ��")]
    public int streamWidth = 1280;
    public int streamHeight = 720;

    [Tooltip("自动获取实际输入分辨率；如未赋值则使用上方的 streamWidth/streamHeight")]
    public VideoStreamSender videoStreamSender; // 可选：自动计算实际分辨率

    [Header("Selection Feedback")]
    public Material highlightMaterial; // ѡ��ʱ�Ĳ���
    private Material originalMaterial; // ��¼����ԭ���Ĳ���
    private Renderer currentSelectedRenderer; // ��ǰѡ�е�����

    // ��¶���ⲿ�����ԣ���������� Avatar ִ�ж���
    public GameObject SelectedObject { get; private set; }

    private void OnEnable()
    {
        clickAction.action.Enable();
        positionAction.action.Enable();
        // �󶨵���¼������������ʱ����
        clickAction.action.started += OnClick;
    }

    private void OnDisable()
    {
        clickAction.action.Disable();
        positionAction.action.Disable();
        clickAction.action.started -= OnClick;
    }

    private void OnClick(InputAction.CallbackContext context)
    {
        // 1. 获取网页端传来的坐标（范围等于 VideoStreamSender 实际输出分辨率）
        Vector2 screenPosition = positionAction.action.ReadValue<Vector2>();

        // 自动计算实际输入区域分辨率（与 StreamManager.CalculateInputRegion 保持一致）
        int actualWidth = streamWidth;
        int actualHeight = streamHeight;
        if (videoStreamSender != null)
        {
            actualWidth = (int)(videoStreamSender.width / videoStreamSender.scaleResolutionDown);
            actualHeight = (int)(videoStreamSender.height / videoStreamSender.scaleResolutionDown);
        }

        // 2. 将视频流坐标转换为 0-1 的 Viewport 坐标，再发射射线
        // 注意：renderStreamingCamera 的实际像素分辨率（Quest 眼纹理）与视频流分辨率不同，
        // 不能直接用 ScreenPointToRay，必须用 ViewportPointToRay
        // 网页端鼠标 Y 轴向下增加（左上角原点），Unity Viewport 左下角原点，必须翻转 Y
        float vx = screenPosition.x / actualWidth;
        float vy = 1f - (screenPosition.y / actualHeight);
        Ray ray = renderStreamingCamera.ViewportPointToRay(new Vector3(vx, vy, 0));
        Debug.DrawRay(ray.origin, ray.direction * 50f, Color.red, 2f);
        RaycastHit hit;

        // 3. 执行射线检测（可添加 LayerMask 过滤）
        if (Physics.Raycast(ray, out hit, 100f))
        {
            GameObject hitObject = hit.collider.gameObject;

            // TODO: 若需在 Unity 中过滤仅选中特定物体，请在 Edit > Project Settings > Tags and Layers 添加 Interactable 标签后恢复下方检查
            // bool isInteractable = hitObject.CompareTag("Interactable");
            // if (isInteractable) { SelectObject(hitObject); } else { DeselectCurrent(); }
            SelectObject(hitObject);
        }
        else
        {
            DeselectCurrent(); // 未命中，取消选中
        }
    }

    private void SelectObject(GameObject obj)
    {
        // ��������ͬһ�����壬�Ͳ��ظ�����
        if (SelectedObject == obj) return;

        // ��ȡ����һ�������ѡ��״̬
        DeselectCurrent();

        // ��¼��ѡ�е�����
        SelectedObject = obj;
        currentSelectedRenderer = obj.GetComponent<Renderer>();

        // �����Ӿ��������滻Ϊ�������ʣ���Ҳ���Ի����ⷢ���� Outline��
        if (currentSelectedRenderer != null && highlightMaterial != null)
        {
            originalMaterial = currentSelectedRenderer.material;
            currentSelectedRenderer.material = highlightMaterial;
        }

        Debug.Log("��ҳ��ѡ��������: " + obj.name);

        // ==========================================
        // ����һ�������Ľӿڡ�
        // ���������Ե������⻯��(Avatar)�Ľű�
        // ���磺 avatarController.WalkToAndGrab(SelectedObject);
        // ==========================================
    }

    private void DeselectCurrent()
    {
        if (currentSelectedRenderer != null && originalMaterial != null)
        {
            // �ָ�ԭ���Ĳ���
            currentSelectedRenderer.material = originalMaterial;
        }
        SelectedObject = null;
        currentSelectedRenderer = null;
    }
}