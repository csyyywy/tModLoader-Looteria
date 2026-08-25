using Terraria;
using global::Looteria.Common.Configs;

namespace Looteria.Common.Data;

/// <summary>
/// 游戏阶段（T0~T7，与抽奖档位解锁进度一致）。
/// 掉落经济与阶段挂钩：血岩 / 重铸之尘掉落 × 阶段倍率（初始 1x → 满阶段 MaxStageDropMult，默认 10x）。
/// </summary>
public static class Progression
{
    /// <summary>
    /// 当前阶段 0-7：
    ///   0 初始 / 1 克脑·吞世 / 2 骷髅王 / 3 肉山后(困难模式) / 4 机械三王 /
    ///   5 世纪之花 / 6 石巨人 / 7 月亮领主。
    /// </summary>
    public static int CurrentStage()
    {
        if (NPC.downedMoonlord) return 7;
        if (NPC.downedGolemBoss) return 6;
        if (NPC.downedPlantBoss) return 5;
        if (NPC.downedMechBossAny) return 4;
        if (Main.hardMode) return 3;
        if (NPC.downedBoss3) return 2;
        if (NPC.downedBoss2) return 1;
        return 0;
    }

    /// <summary>
    /// 阶段掉落倍率：阶段 0 = 1x，阶段 7 = 配置上限（默认 10x），线性插值。
    /// 例：阶段 1 ≈ 2.3x，阶段 3 ≈ 4.9x，阶段 5 ≈ 7.4x。
    /// </summary>
    public static float StageDropMult(int stage)
    {
        if (stage <= 0) return 1f;
        float cap = LooteriaConfig.Instance?.MaxStageDropMult ?? 10f;
        return 1f + (cap - 1f) * stage / 7f;
    }
}
