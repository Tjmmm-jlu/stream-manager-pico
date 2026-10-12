using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时布局规格（2026-10-09）：由编辑器"布局库生成器"离线求解并导出的单套变体数据。
/// 设备端由 ExperimentRuntimeLayoutLoader 按本规格瞬时装载，新增布局无需重打 APK。
/// 字段名即 JSON 键名（JsonUtility 规范）。
/// </summary>
[Serializable]
public sealed class RuntimeLayoutItemSpec
{
    /// <summary>稳定 ID（沿用烘焙规范，如 regional_high_2_03_glass_funnel）。</summary>
    public string id;
    /// <summary>预制体类型键（对应装载器 prefabPool 中的 type）。</summary>
    public string type;
    /// <summary>true = 低档也存在的 39 件；false = 仅高档的 39 件。</summary>
    public bool shared;
    /// <summary>所属工作台（PreparationStation / MixingStation / FilteringStation）。</summary>
    public string station;
    /// <summary>世界位置。</summary>
    public Vector3 position;
    /// <summary>世界旋转（实际只绕 Y）。</summary>
    public Vector3 eulerAngles;
    /// <summary>世界缩放（已归一化）。</summary>
    public Vector3 scale;
    /// <summary>根 BoxCollider 的局部中心（装载时直接使用，设备端不做包围盒重算）。</summary>
    public Vector3 colliderCenter;
    /// <summary>根 BoxCollider 的局部尺寸。</summary>
    public Vector3 colliderSize;
}

[Serializable]
public sealed class RuntimeLayoutSpec
{
    public string profile = "runtime-joint-v1";
    public int index = -1;
    public int seed;
    public int itemCount;
    public int sharedCount;
    public List<RuntimeLayoutItemSpec> items = new List<RuntimeLayoutItemSpec>();
}

/// <summary>装载器预制体池条目：类型 → 预制体 + 语义模板（每件规格只提供 id 与位姿）。</summary>
[Serializable]
public sealed class RuntimeLayoutPrefabEntry
{
    public string type;
    public GameObject prefab;
    public SemanticObjectSummary semantic = new SemanticObjectSummary();
}
