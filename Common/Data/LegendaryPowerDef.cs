using Terraria;

namespace Looteria.Common.Data;

/// <summary>传说之力 id（1..10，0=无）。分发逻辑在 Phase 4 的 Effects/LegendaryPowerHandler。</summary>
public enum LegendaryPowerId : byte
{
    None = 0,
    ChainLightning = 1,
    Burn = 2,
    LifeSteal = 3,
    Execution = 4,
    Pierce = 5,
    Split = 6,
    SkyThunder = 7,
    Thorns = 8,
    Cleave = 9,
    Frenzy = 10
}

public static class LegendaryPowerDatabase
{
    public const int Count = 10;

    public static LegendaryPowerId PickRandom()
        => (LegendaryPowerId)(1 + Main.rand.Next(Count));

    /// <summary>传说之力是否为"命中触发"类（近战走 GlobalItem.OnHitNPC，弹幕走 GlobalProjectile）。</summary>
    public static bool IsOnHit(LegendaryPowerId id) => id is
        LegendaryPowerId.ChainLightning or
        LegendaryPowerId.Burn or
        LegendaryPowerId.LifeSteal or
        LegendaryPowerId.Execution or
        LegendaryPowerId.Split or
        LegendaryPowerId.Cleave;
}
