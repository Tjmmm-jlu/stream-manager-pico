using UnityEditor;

public static class PicoBuildMenu
{
    [MenuItem("PICO/Build Preparation APK", priority = 10)]
    public static void BuildPreparationApk()
    {
        PreparationExperimentSceneBuilder.BuildAndroid();
    }
}
