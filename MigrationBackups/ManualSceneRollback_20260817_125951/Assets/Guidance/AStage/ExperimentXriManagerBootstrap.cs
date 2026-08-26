using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public static class ExperimentXriManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureManager()
    {
        if (Object.FindObjectOfType<XRInteractionManager>(true) != null)
        {
            return;
        }

        var managerObject = new GameObject("XR Interaction Manager (Runtime)");
        Object.DontDestroyOnLoad(managerObject);
        managerObject.AddComponent<XRInteractionManager>();
        Debug.Log("[XriManagerBootstrap] Runtime interaction manager created.");
    }
}
