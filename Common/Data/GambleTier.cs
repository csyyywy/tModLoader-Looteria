using System;
using Terraria;

namespace Looteria.Common.Data;

/// <summary>
/// 血岩赌博档位：费用与装备档位随 Boss 进度逐级递增（25→50→100→200→400→800→1600→3000）。
/// 门槛：没打败对应 Boss 就不能抽其后的档位。见 _devmem/02-design-tables.md。
/// </summary>
public sealed class GambleTier
{
    public int Index;
    public int Cost;
    public int AnchorTier;         // 抽中时生成的装备基准档位（item tier）
    public Func<bool> Unlock;
    public string Key;             // 本地化键后缀（Mods.Looteria.UI.GambleTier.<Key>）

    public GambleTier(int index, int cost, int anchorTier, Func<bool> unlock, string key)
    {
        Index = index; Cost = cost; AnchorTier = anchorTier; Unlock = unlock; Key = key;
    }
}

public static class GambleTiers
{
    public static readonly GambleTier[] All =
    {
        new(0, 25,   2, () => true,                      "T0"),   // 基础（克脑/吞世前）
        new(1, 50,   3, () => NPC.downedBoss2,           "T1"),   // 克脑/世界吞噬者后
        new(2, 100,  5, () => NPC.downedBoss3 || Main.hardMode, "T2"), // 骷髅王（肉山前）
        new(3, 200,  6, () => Main.hardMode,             "T3"),   // 肉山后
        new(4, 400,  7, () => NPC.downedMechBossAny,     "T4"),   // 机械三王
        new(5, 800,  8, () => NPC.downedPlantBoss,       "T5"),   // 世纪之花
        new(6, 1600, 9, () => NPC.downedGolemBoss,       "T6"),   // 石巨人
        new(7, 3000, 10, () => NPC.downedMoonlord,       "T7")    // 月亮领主
    };

    public static bool IsUnlocked(int index)
        => index >= 0 && index < All.Length && All[index].Unlock();

    /// <summary>当前已解锁的最高档（未解锁任何返回 -1）。</summary>
    public static int MaxUnlockedIndex()
    {
        for (int i = All.Length - 1; i >= 0; i--)
            if (All[i].Unlock()) return i;
        return -1;
    }
}
