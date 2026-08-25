using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Configs;

namespace Looteria.Common.Data;

/// <summary>
/// 物品资格判定 / 分类 / tier（对所有物品，含其它模组）。
/// </summary>
public static class ItemClassifier
{
    /// <summary>是否可附加词缀（保守判定，逐项排除）。</summary>
    public static bool IsEligible(Item item)
    {
        var cfg = LooteriaConfig.Instance;
        if (cfg == null || !cfg.Enable) return false;
        if (item == null || item.IsAir || item.maxStack != 1) return false;

        // 硬排除
        if (item.consumable || item.material) return false;
        if (item.ammo != AmmoID.None) return false;
        if (item.mountType != -1) return false;
        if (item.createTile >= TileID.Dirt || item.createWall >= 0) return false; // 可放置物
        if (item.vanity) return false;
        if (item.bait > 0 || item.potion) return false;
        if (cfg.IsExcluded(item)) return false;

        // 类别门控（正判定）
        if (item.pick > 0 || item.axe > 0 || item.hammer > 0) return cfg.AffectTools;
        if (item.fishingPole > 0) return cfg.AffectTools;
        if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0) return cfg.AffectArmor;
        if (item.accessory) return cfg.AffectAccessories;
        if (item.damage > 0) return cfg.AffectWeapons;
        return false;
    }

    /// <summary>物品类别（不含未知类武器的特殊处理：返回 Weapon 即通用武器池）。</summary>
    public static ItemCategory GetCategory(Item item)
    {
        if (item.fishingPole > 0) return ItemCategory.Fishing;
        if (item.pick > 0 || item.axe > 0 || item.hammer > 0) return ItemCategory.Tool;
        if (item.headSlot >= 0) return ItemCategory.HeadArmor;
        if (item.bodySlot >= 0) return ItemCategory.BodyArmor;
        if (item.legSlot >= 0) return ItemCategory.LegsArmor;
        if (item.accessory) return ItemCategory.Accessory;
        if (item.damage > 0)
        {
            var dc = item.DamageType;
            if (dc == DamageClass.Melee) return ItemCategory.MeleeWeapon;
            if (dc == DamageClass.Ranged) return ItemCategory.RangedWeapon;
            if (dc == DamageClass.Magic) return ItemCategory.MagicWeapon;
            if (dc == DamageClass.Summon) return ItemCategory.SummonWeapon;
            // 其它模组的自定义伤害类：按继承关系映射；都不继承 → 通用武器池
            if (dc.GetModifierInheritance(DamageClass.Melee).damageInheritance > 0) return ItemCategory.MeleeWeapon;
            if (dc.GetModifierInheritance(DamageClass.Ranged).damageInheritance > 0) return ItemCategory.RangedWeapon;
            if (dc.GetModifierInheritance(DamageClass.Magic).damageInheritance > 0) return ItemCategory.MagicWeapon;
            if (dc.GetModifierInheritance(DamageClass.Summon).damageInheritance > 0) return ItemCategory.SummonWeapon;
            return ItemCategory.Weapon;
        }
        return 0;
    }

    /// <summary>物品基准值 tier 1~10（词缀数值规模基准）。</summary>
    public static int GetTier(Item item)
    {
        int tier = 1
            + (int)(item.value / 10000f)
            + (item.rare + 1) / 2
            + (item.damage > 0 ? item.damage / 40 : 0)
            + (item.defense > 0 ? item.defense / 6 : 0);
        return Math.Clamp(tier, 1, 10);
    }
}
