using Microsoft.Xna.Framework;
using Terraria;

namespace Looteria.Common.Data;

/// <summary>稀有度（0=无词缀/普通，1=魔法 … 4=套装）。</summary>
public enum LootRarity : byte
{
    None = 0,
    Magic = 1,
    Rare = 2,
    Legendary = 3,
    Set = 4
}

/// <summary>稀有度静态信息表（见 _devmem/02-design-tables.md）。</summary>
public static class RarityInfo
{
    public const int Count = 5;

    public static readonly Color[] Colors = new Color[Count]
    {
        Color.White,                         // None/普通
        new Color(70, 130, 255),             // 魔法 蓝
        new Color(255, 200, 0),              // 稀有 金
        new Color(255, 130, 0),              // 传说 橙
        new Color(0, 255, 120)               // 套装 绿
    };

    /// <summary>词缀条数（含随机）。</summary>
    public static int AffixCount(LootRarity r) => r switch
    {
        LootRarity.Magic => 1 + Main.rand.Next(2),        // 1~2
        LootRarity.Rare => 3 + Main.rand.Next(3),         // 3~5
        LootRarity.Legendary => 3 + Main.rand.Next(3),    // 3~5
        LootRarity.Set => 3 + Main.rand.Next(3),          // 3~5
        _ => 0
    };

    /// <summary>价值倍率（掷稀有度时应用，幂等：BaseValue 只记一次）。</summary>
    public static float ValueMult(LootRarity r) => r switch
    {
        LootRarity.Magic => 2f,
        LootRarity.Rare => 5f,
        LootRarity.Legendary => 20f,
        LootRarity.Set => 15f,
        _ => 1f
    };

    /// <summary>词缀数值品质系数。</summary>
    public static float Quality(LootRarity r) => r switch
    {
        LootRarity.Magic => 0.8f,
        LootRarity.Rare => 1.0f,
        LootRarity.Legendary => 1.2f,
        LootRarity.Set => 1.1f,
        _ => 1f
    };

    /// <summary>是否有插槽（传说/套装）。</summary>
    public static bool HasSockets(LootRarity r) => r == LootRarity.Legendary || r == LootRarity.Set;
}
