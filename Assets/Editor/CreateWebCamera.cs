using UnityEngine;
using UnityEditor;

public class CreateWebCamera
{
    [MenuItem("Tools/Create Web Camera")]
    static void Create()
    {
        GameObject camObj = new GameObject("WebCamera");
        Camera cam = camObj.AddComponent<Camera>();
        cam.tag = "Untagged";

        // 放到场景中间，稍微抬高到人眼高度
        camObj.transform.position = new Vector3(0, 1.7f, 0);

        Undo.RegisterCreatedObjectUndo(camObj, "Create Web Camera");
        Selection.activeGameObject = camObj;

        Debug.Log("WebCamera 创建完成，请在 Inspector 里调整位置，并将其拖入 VideoStreamSender 的 Camera 字段。");
    }
}
