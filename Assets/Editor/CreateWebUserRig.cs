using UnityEngine;
using UnityEditor;

public class CreateWebUserRig
{
    [MenuItem("Tools/Create Web User Rig")]
    static void Create()
    {
        // Root
        GameObject root = new GameObject("[WebUserRig]");

        // Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
        Object.DestroyImmediate(body.GetComponent<Collider>());

        // Head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform);
        head.transform.localPosition = new Vector3(0, 2.0f, 0);
        head.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        Object.DestroyImmediate(head.GetComponent<Collider>());

        // EyeAnchor
        GameObject eyeAnchor = new GameObject("EyeAnchor");
        eyeAnchor.transform.SetParent(head.transform);
        eyeAnchor.transform.localPosition = new Vector3(0, 0.05f, 0.6f);

        // WebCamera
        GameObject camObj = new GameObject("WebCamera");
        camObj.transform.SetParent(eyeAnchor.transform);
        camObj.transform.localPosition = Vector3.zero;
        camObj.transform.localRotation = Quaternion.identity;
        camObj.AddComponent<Camera>().tag = "Untagged";

        Undo.RegisterCreatedObjectUndo(root, "Create Web User Rig");
        Selection.activeGameObject = root;
        Debug.Log("[WebUserRig] 创建完成。将 WebCamera 拖入 VideoStreamSender 的 Camera 字段，并拖入 PlayerController 的 webCamera 字段。");
    }
}
