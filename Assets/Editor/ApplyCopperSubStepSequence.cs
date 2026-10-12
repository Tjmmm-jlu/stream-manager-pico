using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键将"硫酸铜连续实验"的 20 个元步骤序列应用到当前场景的
/// PreparationSequenceController（目标按 stableId 解析，无需场景对象引用）。
/// 菜单：Tools/Copper Experiment/Apply Sub-Step Sequence (20)
/// </summary>
public static class ApplyCopperSubStepSequence
{
    [MenuItem("Tools/Copper Experiment/Apply Sub-Step Sequence (20)")]
    public static void Apply()
    {
        PreparationSequenceController controller =
            Object.FindObjectOfType<PreparationSequenceController>(true);
        if (controller == null)
        {
            EditorUtility.DisplayDialog(
                "元步骤序列",
                "当前场景中未找到 PreparationSequenceController。",
                "好");
            return;
        }

        List<PreparationSequenceController.Step> steps = BuildSteps();
        Undo.RecordObject(controller, "Apply copper sub-step sequence");
        controller.Configure(
            controller.GuidanceManager, steps, controller.ShowTargetAutomatically);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log(
            $"[SubSteps] 已应用 {steps.Count} 个元步骤配置。请保存场景（Ctrl+S）后运行验证。" +
            "目标物体按 stableId 解析：请确认场景中这些物体均带 SemanticObject。");
    }

    private static List<PreparationSequenceController.Step> BuildSteps()
    {
        var steps = new List<PreparationSequenceController.Step>();
        // --- A 硫酸铜转移 ---
        steps.Add(Step("sub_grab_forceps", "拿起镊子", "forceps", "grab"));
        steps.Add(Step("sub_grab_solute", "用镊子夹取粗硫酸铜", "copper_sulfate_crude", "grab_copper_sulfate"));
        steps.Add(Step("sub_transfer_solute", "将粗硫酸铜放入空烧杯", "beaker", "transfer_copper_sulfate"));
        // --- B 量水与溶解 ---
        steps.Add(Step("sub_grab_cylinder", "拿起量筒", "graduated_cylinder", "grab"));
        steps.Add(Step("sub_measure_water", "用量筒从水源容器中量取约 100 mL 水", "water_source", "measure_water"));
        steps.Add(Step("sub_pour_water", "将量筒中的水倒入烧杯", "beaker", "pour_water_into_beaker"));
        // 玻璃棒未配置 SemanticObject，暂作为执行步（搅拌完成信号来自混合检测器）。
        steps.Add(Step("sub_stir_dissolve", "拿起玻璃棒，搅拌至硫酸铜完全溶解", null, "dissolve_copper_sulfate", false));
        // --- C 过滤 ---
        steps.Add(Step("sub_mount_funnel", "将玻璃漏斗安装到铁架台支撑环上", "glass_funnel", "mount_funnel"));
        steps.Add(Step("sub_place_receiver", "将接收烧杯放到铁架台底座定位区", "filtrate_beaker", "place_receiver"));
        steps.Add(Step("sub_filter", "双手持原烧杯和玻璃棒，将溶液引流过滤", "beaker", "filter_copper_sulfate"));
        steps.Add(Step("sub_collect_receiver", "过滤完成后取出接收烧杯", "filtrate_beaker", "collect_receiver"));
        // --- D 滤液转移 ---
        steps.Add(Step("sub_transfer_filtrate", "将蓝色滤液倒入蒸发皿", "china_dish", "transfer_filtrate"));
        // --- E 加热装置 ---
        steps.Add(Step("sub_remove_funnel", "从铁架台上取下玻璃漏斗", "glass_funnel", "remove_funnel"));
        steps.Add(Step("sub_mount_dish", "将蒸发皿安装到铁架台", "china_dish", "mount_heating_dish"));
        steps.Add(Step("sub_place_lamp", "将酒精灯放到铁架台底座", "spirit_lamp", "place_heating_lamp"));
        // --- F 浓缩与结晶 ---
        steps.Add(Step("sub_remove_cap", "取下酒精灯帽", "lamp_cap", "remove_lamp_cap"));
        steps.Add(Step("sub_ignite_lamp", "用长柄点火器点燃酒精灯", "long_lighter", "ignite_lamp"));
        steps.Add(Step("sub_concentrate_dwell", "等待浓缩至目标刻度", null, "concentrate_dwell", false));
        steps.Add(Step("sub_extinguish", "盖上灯帽熄灭酒精灯", "lamp_cap", "extinguish_lamp"));
        steps.Add(Step("sub_cooling_dwell", "等待冷却，直到蓝色晶体析出", null, "cooling_dwell", false));
        return steps;
    }

    private static PreparationSequenceController.Step Step(
        string id, string instruction, string targetId, string action,
        bool requiresConfirmation = true)
    {
        return new PreparationSequenceController.Step
        {
            id = id,
            instruction = instruction,
            target = null,
            targetId = targetId,
            requiresConfirmation = requiresConfirmation,
            completionAction = action
        };
    }
}
