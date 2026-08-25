using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Utilities;

namespace Looteria.Common.Data;

/// <summary>
/// 全部词缀定义表（见 _devmem/02-design-tables.md）。
/// 同一机械属性可有多个主题变体（用于套装协同）；抽取时同物品内不重复 Stat。
/// </summary>
public static class AffixDatabase
{
    public static readonly List<AffixDef> All = new()
    {
        // —— 全伤害（元素主题变体）——
        New("AllDamage.Fire", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Fire, true),
        New("AllDamage.Ice", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Ice, true),
        New("AllDamage.Lightning", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Lightning, true),
        New("AllDamage.Poison", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Poison, true),
        New("AllDamage.Shadow", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Shadow, true),
        New("AllDamage.Holy", AffixStatType.AllDamage, ItemCategory.Weapon, 5, 2, 100, Theme.Holy, true),
        // —— 职业伤害 ——
        New("MeleeDamage", AffixStatType.MeleeDamage, ItemCategory.MeleeWeapon, 8, 3, 150, Theme.Melee, true, 1.2f),
        New("RangedDamage", AffixStatType.RangedDamage, ItemCategory.RangedWeapon, 8, 3, 150, Theme.Ranged, true, 1.2f),
        New("MagicDamage", AffixStatType.MagicDamage, ItemCategory.MagicWeapon, 8, 3, 150, Theme.Magic, true, 1.2f),
        New("SummonDamage", AffixStatType.SummonDamage, ItemCategory.SummonWeapon, 8, 3, 150, Theme.Summon, true, 1.2f),
        // —— 暴击 ——
        New("CritChance.Speed", AffixStatType.CritChance, ItemCategory.Weapon | ItemCategory.Accessory, 2, 1, 30, Theme.Speed, true),
        New("CritChance.Luck", AffixStatType.CritChance, ItemCategory.Weapon | ItemCategory.Accessory, 2, 1, 30, Theme.Luck, true),
        New("CritDamage", AffixStatType.CritDamage, ItemCategory.Weapon | ItemCategory.Accessory, 8, 4, 100, Theme.Speed, true),
        New("AttackSpeed", AffixStatType.AttackSpeed, ItemCategory.Weapon, 4, 2, 60, Theme.Speed, true),
        // —— 平坦/命中 ——
        New("FlatDamage.Melee", AffixStatType.FlatDamage, ItemCategory.Weapon, 2, 1.5f, 60, Theme.Melee, false),
        New("FlatDamage.Ranged", AffixStatType.FlatDamage, ItemCategory.Weapon, 2, 1.5f, 60, Theme.Ranged, false),
        New("FlatDamage.Magic", AffixStatType.FlatDamage, ItemCategory.Weapon, 2, 1.5f, 60, Theme.Magic, false),
        New("FlatDamage.Summon", AffixStatType.FlatDamage, ItemCategory.Weapon, 2, 1.5f, 60, Theme.Summon, false),
        New("LifeOnHit.Holy", AffixStatType.LifeOnHit, ItemCategory.Weapon | ItemCategory.Accessory, 1, 0.5f, 3, Theme.Holy, false),
        New("LifeOnHit.Shadow", AffixStatType.LifeOnHit, ItemCategory.Weapon | ItemCategory.Accessory, 1, 0.5f, 3, Theme.Shadow, false),
        New("ManaOnHit", AffixStatType.ManaOnHit, ItemCategory.Weapon | ItemCategory.Accessory, 1, 0.5f, 2, Theme.Magic, false),
        New("ManaCost", AffixStatType.ManaCost, ItemCategory.MagicWeapon, 4, 2, 50, Theme.Magic, true),
        New("Knockback", AffixStatType.Knockback, ItemCategory.Weapon, 10, 5, 150, Theme.Melee, true, 0.5f),
        // —— 防御系 ——
        New("Defense", AffixStatType.Defense, ItemCategory.Armor, 1, 1, 12, Theme.Defense, false, 1f, 1.5f),
        New("DamageReduction", AffixStatType.DamageReduction, ItemCategory.Armor | ItemCategory.Accessory, 2, 1, 5, Theme.Defense, true, 1f, 2f),
        New("MaxLife", AffixStatType.MaxLife, ItemCategory.Armor | ItemCategory.Accessory, 10, 8, 200, Theme.Defense, false, 1f, 0.5f),
        New("LifeRegen", AffixStatType.LifeRegen, ItemCategory.Armor | ItemCategory.Accessory, 0.5f, 0.3f, 6, Theme.Holy, false, 0.8f, 1.2f),
        New("ManaRegen", AffixStatType.ManaRegen, ItemCategory.Armor | ItemCategory.Accessory, 1, 0.5f, 10, Theme.Magic, false, 0.8f, 0.8f),
        New("MoveSpeed", AffixStatType.MoveSpeed, ItemCategory.Armor | ItemCategory.Accessory, 3, 2, 40, Theme.Speed, true),
        // —— 状态免疫 ——
        New("BuffResistPoison", AffixStatType.BuffResistPoison, ItemCategory.Armor | ItemCategory.Accessory, 100, 0, 100, Theme.Poison, true, 0.5f),
        New("BuffResistFire", AffixStatType.BuffResistFire, ItemCategory.Armor | ItemCategory.Accessory, 100, 0, 100, Theme.Fire, true, 0.5f),
        New("BuffResistBleed", AffixStatType.BuffResistBleed, ItemCategory.Armor | ItemCategory.Accessory, 100, 0, 100, Theme.Shadow, true, 0.5f),
        New("BuffResistCurse", AffixStatType.BuffResistCurse, ItemCategory.Armor | ItemCategory.Accessory, 100, 0, 100, Theme.Shadow, true, 0.5f),
        New("BuffResistSlow", AffixStatType.BuffResistSlow, ItemCategory.Armor | ItemCategory.Accessory, 100, 0, 100, Theme.Ice, true, 0.5f),
        // —— 工具 ——
        New("MiningSpeed", AffixStatType.MiningSpeed, ItemCategory.Tool, 8, 4, 80, Theme.Melee, true),
        New("FishingPower", AffixStatType.FishingPower, ItemCategory.Fishing, 5, 3, 40, Theme.Luck, true),
        // —— 破甲（参照原版鲨鱼项链：无视 5 点敌防）——
        New("ArmorShred.Flat", AffixStatType.FlatArmorShred, ItemCategory.Weapon, 1, 0.5f, 5, Theme.Shadow, false),
        New("ArmorShred.Pct", AffixStatType.PctArmorShred, ItemCategory.Weapon, 1, 0.5f, 5, Theme.Shadow, true)
    };

    private static int _id = 1;
    private static AffixDef New(string key, AffixStatType stat, ItemCategory cats, float baseV, float step, float max, Theme theme, bool isPercent, float weight = 1f, float powerWeight = 1f)
        => new(_id++, key, stat, cats, baseV, step, max, (byte)theme, isPercent, weight, powerWeight);

    private static readonly Dictionary<int, AffixDef> ById = All.ToDictionary(a => a.Id);

    /// <summary>按 id 取词缀定义；未知 id 返回 null（调用方必须判空，避免静默回退成真实词缀）。</summary>
    public static AffixDef? GetById(int id) => ById.TryGetValue(id, out var d) ? d : null;

    /// <summary>按类别过滤后的可抽取池。</summary>
    public static List<AffixDef> GetPool(ItemCategory cat) =>
        All.FindAll(d => (d.Categories & cat) != 0);

    /// <summary>加权抽取一个词缀，且 Stat 不与已用集合重复；找不到返回 null。</summary>
    public static AffixDef? PickWeighted(List<AffixDef> pool, HashSet<AffixStatType> usedStats, int? biasTheme = null)
    {
        var cands = pool.FindAll(d => !usedStats.Contains(d.Stat));
        if (cands.Count == 0) return null;

        double total = 0;
        foreach (var d in cands)
        {
            double w = d.Weight;
            if (biasTheme.HasValue && d.Theme == biasTheme.Value) w *= 3;
            total += w;
        }

        double r = Main.rand.NextDouble() * total;
        foreach (var d in cands)
        {
            double w = d.Weight;
            if (biasTheme.HasValue && d.Theme == biasTheme.Value) w *= 3;
            r -= w;
            if (r <= 0) return d;
        }
        return cands[^1];
    }
}
