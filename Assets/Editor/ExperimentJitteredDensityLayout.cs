using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 抖动网格双密度布局引擎 v2（2026-10-08）。
/// 目标：把干扰物放置从"纯随机试探"改为"均匀候选池 + 约束过滤 + 旧算法兜底"，
/// 使低档(52 件)/高档(91 件)之间的差异主要来自数量，而不是随机聚簇的运气。
///
/// 使用方式（见 RegionRatioLowSceneInstaller.Place / TryPlaceJittered）：
///   1) ResetAll(seed)            —— 每个布局流程开始时重建候选池；
///   2) TryGetCandidate(table,…)  —— 随机抽取一个尚未占用的网格候选点；
///   3) 调用方逐项校验硬约束（桌内边距 / 操作区禁放 / 任务正面净空 / 两级间距 / 座位视线遮挡）；
///   4) 通过后 MarkUsed(table,…)  —— 标记占用（交换删除，候选点不会被两件物体共用）；
///   5) 池耗尽或单件扫描超限时，调用方回退旧随机算法（PlaceLegacyRandom），保证布局必成功。
///
/// 参数默认值取"中位物体尺寸 ≈ 0.15m"的实验场景：格距 0.12m（约 0.8 倍物宽），
/// 抖动半幅 ±0.042m。该格距下相邻两格无法同时容纳大物体，天然形成"至少一个物宽"的间距，
/// 同时保证候选数 ≈ 所需件数的 2~3 倍，给约束过滤留出余量。
/// </summary>
public static class ExperimentJitteredDensityLayout
{
    /// <summary>网格步距（米）。</summary>
    public const float CellSize = 0.12f;

    /// <summary>抖动半幅相对步距的比例（±0.35 × 格距）。</summary>
    public const float JitterFraction = 0.35f;

    /// <summary>单件物体最多扫描的候选数；超限即由调用方回退旧算法。</summary>
    public const int MaxCandidatesPerItem = 240;

    /// <summary>候选点距桌边的最小内缩（米），与旧算法的 4cm 边距一致。</summary>
    public const float EdgeMargin = 0.04f;

    /// <summary>
    /// 任务"正面净空"半径（米）。大于 0 时，任何放置物与任一任务中心的 XZ 距离不得小于该值。
    /// 默认 0：正面净空依赖既有的 0.08m 任务间距 + 座位眼点视线遮挡检查（弱化版净空）；如需更强约束，在安装器中赋值。
    /// </summary>
    public static float TaskFrontClearance;

    private sealed class Pool
    {
        public readonly List<Vector3> Candidates = new List<Vector3>();
        public readonly List<Rect> Zones = new List<Rect>();
        public int TotalCells;
    }

    private static readonly Dictionary<string, Pool> Pools = new Dictionary<string, Pool>();
    private static int _seed = 20260915;

    /// <summary>清空全部候选池并用指定种子重建。同一布局流程内只应调用一次（低档/高档安装器各自调用，使用同一设种以保持可复现）。</summary>
    public static void ResetAll(int seed = 20260915)
    {
        Pools.Clear();
        _seed = seed;
    }

    /// <summary>某张桌子当前剩余的可用候选点数。</summary>
    public static int Remaining(Bounds table) => GetPool(table).Candidates.Count;

    /// <summary>调试/汇报用：已建候选池的剩余候选概况（用于 verification.txt 记录）。</summary>
    public static string DescribePools()
    {
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, Pool> pair in Pools)
        {
            parts.Add(pair.Key + "->剩" + pair.Value.Candidates.Count + "/" + pair.Value.TotalCells);
        }
        return parts.Count == 0 ? "(本轮未建池)" : string.Join("; ", parts);
    }

    /// <summary>
    /// 为一张桌子登记"操作区禁放"矩形（世界 XZ 坐标，米；Rect(x, z, width, depth)）。
    /// 建议在开始放置前调用；默认无禁区。示例：把明火加热区、倾倒区标为禁放。
    /// </summary>
    public static void SetActionZones(Bounds table, params Rect[] zonesXZ)
    {
        Pool pool = GetPool(table);
        pool.Zones.Clear();
        if (zonesXZ != null)
        {
            pool.Zones.AddRange(zonesXZ);
        }
    }

    /// <summary>随机抽取一个尚未占用的候选点（世界坐标，y 为桌面高度）；池为空时返回 false。</summary>
    public static bool TryGetCandidate(Bounds table, System.Random rng, out Vector3 position, out int index)
    {
        Pool pool = GetPool(table);
        if (pool.Candidates.Count == 0)
        {
            position = default;
            index = -1;
            return false;
        }

        index = rng.Next(pool.Candidates.Count);
        position = pool.Candidates[index];
        return true;
    }

    /// <summary>确认占用某个候选点（交换删除，O(1)）。index 来自最近一次 TryGetCandidate 的返回值。</summary>
    public static void MarkUsed(Bounds table, int index)
    {
        Pool pool = GetPool(table);
        if (index < 0 || index >= pool.Candidates.Count)
        {
            return;
        }

        int last = pool.Candidates.Count - 1;
        pool.Candidates[index] = pool.Candidates[last];
        pool.Candidates.RemoveAt(last);
    }

    /// <summary>物体包围盒是否压到任一"操作区禁放"矩形（世界 XZ 判定）。</summary>
    public static bool HitsZone(Bounds table, Bounds item)
    {
        Pool pool = GetPool(table);
        foreach (Rect zone in pool.Zones)
        {
            if (item.min.x < zone.xMax && item.max.x > zone.xMin &&
                item.min.z < zone.yMax && item.max.z > zone.yMin)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>物体与任一任务中心的 XZ 距离是否小于"任务正面净空"半径（默认关闭）。</summary>
    public static bool BreachesTaskFront(Bounds item, List<Bounds> tasks)
    {
        if (TaskFrontClearance <= 0f)
        {
            return false;
        }

        for (int i = 0; i < tasks.Count; i++)
        {
            Vector2 center = new Vector2(tasks[i].center.x, tasks[i].center.z);
            float dx = center.x < item.min.x ? item.min.x - center.x
                : center.x > item.max.x ? center.x - item.max.x : 0f;
            float dz = center.y < item.min.z ? item.min.z - center.y
                : center.y > item.max.z ? center.y - item.max.z : 0f;
            if (Mathf.Sqrt(dx * dx + dz * dz) < TaskFrontClearance)
            {
                return true;
            }
        }

        return false;
    }

    private static string KeyOf(Bounds table)
    {
        Vector3 c = table.center;
        Vector3 s = table.size;
        return string.Format(CultureInfo.InvariantCulture, "T({0:F2},{1:F2}) {2:F2}x{3:F2}", c.x, c.z, s.x, s.z);
    }

    private static Pool GetPool(Bounds table)
    {
        string key = KeyOf(table);
        if (Pools.TryGetValue(key, out Pool existing))
        {
            return existing;
        }

        Pool pool = BuildPool(table, key);
        Pools[key] = pool;
        return pool;
    }

    private static Pool BuildPool(Bounds table, string key)
    {
        Pool pool = new Pool();
        float jitter = JitterFraction * CellSize;
        float minX = table.min.x + EdgeMargin;
        float maxX = table.max.x - EdgeMargin;
        float minZ = table.min.z + EdgeMargin;
        float maxZ = table.max.z - EdgeMargin;
        if (maxX <= minX || maxZ <= minZ)
        {
            Debug.LogWarning("[JitteredDensity] 桌子太小，无法生成候选池: " + key);
            return pool;
        }

        int countX = Mathf.Max(1, Mathf.FloorToInt((maxX - minX) / CellSize));
        int countZ = Mathf.Max(1, Mathf.FloorToInt((maxZ - minZ) / CellSize));
        float startX = 0.5f * (minX + maxX) - 0.5f * (countX - 1) * CellSize;
        float startZ = 0.5f * (minZ + maxZ) - 0.5f * (countZ - 1) * CellSize;

        // 与字符串哈希无关的稳定种子：只用桌面位置/尺寸的毫米值推导，保证跨进程可复现。
        int tableHash = Mathf.RoundToInt(table.center.x * 1000f);
        tableHash = unchecked(tableHash * 397 ^ Mathf.RoundToInt(table.center.z * 1000f));
        tableHash = unchecked(tableHash * 397 ^ Mathf.RoundToInt(table.size.x * 1000f));
        tableHash = unchecked(tableHash * 397 ^ Mathf.RoundToInt(table.size.z * 1000f));
        System.Random rng = new System.Random(unchecked(_seed * 397 ^ tableHash));

        for (int ix = 0; ix < countX; ix++)
        {
            for (int iz = 0; iz < countZ; iz++)
            {
                float x = startX + ix * CellSize + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                float z = startZ + iz * CellSize + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                if (x < minX || x > maxX || z < minZ || z > maxZ)
                {
                    continue;
                }

                pool.Candidates.Add(new Vector3(x, table.max.y, z));
            }
        }

        pool.TotalCells = countX * countZ;
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[JitteredDensity] {0}: cells={1}x{2}={3} kept={4} cell={5:F3}m jitter=±{6:F3}m",
            key, countX, countZ, pool.TotalCells, pool.Candidates.Count, CellSize, jitter));
        return pool;
    }
}
