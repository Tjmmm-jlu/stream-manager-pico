using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateApparatusFixInstaller
{
    private const string RequestPath = "Temp/CopperSulfateApparatusFix.request";
    private static bool _busy;
    static CopperSulfateApparatusFixInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
            if (command == "install") Install();
            else if (command == "verify")
            {
                PicoPinchToggleVerification.Run(); CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/CopperSulfateApparatusFix.result.txt", "PASS: receiver pickup, stand filtration, dish geometry, liquids and pinch input.");
            }
            else if (command == "build" || command == "build-relaxed-filtration")
            {
                string apk = command == "build" ? "Builds/CopperSulfate-ReceiverAndDishFix.apk" : "Builds/CopperSulfate-RelaxedFiltration.apk";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { CopperSulfateLiquidInstaller.ScenePath },
                    locationPathName = apk, target = BuildTarget.Android, options = BuildOptions.Development });
                File.WriteAllText("Temp/CopperSulfateApparatusFix.build.txt", report.summary.result + "\n" + apk + "\nErrors: " + report.summary.totalErrors);
            }
            else throw new InvalidOperationException("Unknown apparatus fix command: " + command);
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/CopperSulfateApparatusFix.result.txt", error.ToString()); Debug.LogException(error);
        }
        finally { _busy = false; }
    }

    private static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath) throw new InvalidOperationException("Open the stationary scene.");
        var filter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateFiltrationDetector>(true)).Single();
        var dish = filter.Dish;
        if (dish.transform.Find("Evaporating Dish Colliders") != null) throw new InvalidOperationException("Dish shape is already installed.");
        string backup = "MigrationBackups/BeforeApparatusFixInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        float baseY = dish.GetComponent<Renderer>().bounds.min.y;
        Mesh mesh = CreateDish();
        AssetDatabase.CreateAsset(mesh, CopperSulfateLiquidInstaller.AssetFolder + "/EvaporatingDishWithSpout.asset");
        dish.GetComponent<MeshFilter>().sharedMesh = mesh;
        dish.transform.localScale = Vector3.one;
        dish.transform.position = new Vector3(dish.transform.position.x, baseY, dish.transform.position.z);
        var porcelain = CopperSulfateLiquidInstaller.MaterialAsset("EvaporatingPorcelain", new Color(0.66f, 0.69f, 0.72f), false);
        porcelain.SetFloat("_Glossiness", 0.35f); EditorUtility.SetDirty(porcelain);
        dish.GetComponent<Renderer>().sharedMaterial = porcelain;
        foreach (var collider in dish.GetComponentsInChildren<Collider>(true))
        { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
        var colliders = new GameObject("Evaporating Dish Colliders").transform; colliders.SetParent(dish.transform, false);
        var bottom = colliders.gameObject.AddComponent<BoxCollider>(); bottom.center = new Vector3(0f, 0.003f, 0f);
        bottom.size = new Vector3(0.052f, 0.006f, 0.052f);
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI / 16f;
            Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float spout = Spout(angle);
            Vector3 lower = radial * 0.028f + Vector3.up * 0.006f;
            Vector3 upper = radial * (0.064f + 0.016f * spout) + Vector3.up * (0.034f - 0.004f * spout);
            var wall = new GameObject("Wall " + i).transform; wall.SetParent(colliders, false);
            wall.localPosition = (lower + upper) * 0.5f;
            wall.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.Cross(Vector3.up, radial), upper - lower), upper - lower);
            wall.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.016f, Vector3.Distance(lower, upper), 0.003f);
        }
        dish.Grab.colliders.Clear(); dish.Grab.colliders.AddRange(colliders.GetComponentsInChildren<Collider>());
        dish.Grab.attachTransform.localPosition = new Vector3(0f, 0.025f, -0.055f);
        dish.Grab.throwOnDetach = false;
        var liquid = dish.transform.Find("Measured Liquid");
        dish.Configure(150f, 0f, new Vector3(0f, 0.006f, 0f), 0.057f, 0.025f,
            liquid.GetComponent<MeshFilter>(), liquid.GetComponent<MeshRenderer>(), 0.026f / 0.057f);
        var semantic = dish.GetComponent<SemanticObject>();
        semantic.ConfigureDetailed(semantic.StableId, semantic.DisplayName, semantic.ObjectType, semantic.Category, semantic.Function,
            "white", "opaque", "shallow_dish_with_spout", semantic.Materials, semantic.Contents, semantic.ContentColor,
            semantic.StateTags, semantic.Aliases.Concat(new[] { "\u5e26\u5bfc\u6d41\u5634\u84b8\u53d1\u76bf", "spouted evaporating dish" }).ToArray(), true, true);
        filter.Receiver.Grab.enabled = true;
        foreach (var item in new UnityEngine.Object[] { dish, dish.transform, dish.Grab, dish.GetComponent<MeshFilter>(), dish.GetComponent<Renderer>(), semantic, filter.Receiver.Grab })
        { EditorUtility.SetDirty(item); PrefabUtility.RecordPrefabInstancePropertyModifications(item); }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save apparatus fixes.");
        var eye = dish.Mouth + new Vector3(0.08f, 0.17f, -0.2f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateApparatusFix.dish.png", eye, Quaternion.LookRotation(dish.Mouth - eye));
        File.WriteAllText("Temp/CopperSulfateApparatusFix.result.txt", "Installed spouted shallow dish; receiver grab enabled. Backup: " + backup);
    }

    private static float Spout(float angle)
    {
        float distance = Mathf.DeltaAngle(0f, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        return Mathf.Exp(-distance * distance / 0.045f);
    }

    private static Mesh CreateDish()
    {
        var profile = new[] { new Vector2(0f, 0f), new Vector2(0.026f, 0f), new Vector2(0.030f, 0.003f),
            new Vector2(0.050f, 0.017f), new Vector2(0.066f, 0.032f), new Vector2(0.066f, 0.035f),
            new Vector2(0.063f, 0.035f), new Vector2(0.026f, 0.006f), new Vector2(0f, 0.006f) };
        const int sides = 96;
        var vertices = new Vector3[profile.Length * sides]; var triangles = new List<int>();
        for (int row = 0; row < profile.Length; row++)
        for (int i = 0; i < sides; i++)
        {
            float angle = i * 2f * Mathf.PI / sides;
            float spout = Spout(angle) * Mathf.InverseLerp(0.01f, 0.032f, profile[row].y);
            float radius = profile[row].x + 0.016f * spout;
            vertices[row * sides + i] = new Vector3(Mathf.Sin(angle) * radius, profile[row].y - 0.004f * spout, Mathf.Cos(angle) * radius);
            if (row == profile.Length - 1) continue;
            int a = row * sides + i, b = row * sides + (i + 1) % sides, c = a + sides, d = b + sides;
            triangles.AddRange(new[] { a, b, c, b, d, c });
        }
        var mesh = new Mesh { name = "Spouted Shallow Evaporating Dish" };
        mesh.vertices = vertices; mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
