using System;
using System.Collections.Generic;
using Terraria;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.Roll;

/// <summary>
/// 词缀掷点引擎（纯函数，输入物品+稀有度 → 填 AffixGlobalItem 实例）。
/// 数值 = (Base + Step*(tier-1)) × 品质 × 浮动(0.8~1.2)，clamp Max，保留 1 位小数。
/// </summary>
public static class AffixRoller
{
    public static void Roll(Item item, AffixGlobalItem g, LootRarity rarity)
    {
        // R7：掷空（None）也标记已判定（防 MaybeRoll 兜底重掷）；带物品清词缀并还原售价（复审补正）
        if (rarity == LootRarity.None) { Clear(item, g); g.Checked = true; return; }

        // M1：复用缓存档位（首次掷点时 GetTier 于价值放大前取值是正确的；
        // 升档/重铸时 item.value 已被稀有度倍率放大，重算会系统性膨胀）
        int tier = g.Tier > 0 ? g.Tier : ItemClassifier.GetTier(item);
        g.Rarity = rarity;
        g.Tier = tier;

        if (rarity == LootRarity.Set)
            g.SetThemeId = (byte)SetThemeDatabase.PickRandom();
        else
            g.SetThemeId = -1;

        g.Affixes = RollAffixList(item, tier, rarity, g.SetThemeId >= 0 ? g.SetThemeId : null);

        // 插槽（传说/套装）
        g.SocketCount = RarityInfo.HasSockets(rarity) ? (Main.rand.NextBool(3) ? 2 : 1) : 0;
        g.Sockets = new List<int>();
        for (int i = 0; i < g.SocketCount; i++) g.Sockets.Add(0);

        // 传说之力（仅传说）
        g.LegendaryPowerId = rarity == LootRarity.Legendary ? (int)LegendaryPowerDatabase.PickRandom() : -1;

        ApplyValue(item, g);
        g.PowerScore = PowerScore(g);
        g.Checked = true; // H1：手动掷点产物标记已判定，杜绝 MaybeRoll 兜底二次重掷/清空
    }

    /// <summary>掷一组词缀（不写状态），供 Roll / 全部重铸演练复用。</summary>
    private static List<AffixRoll> RollAffixList(Item item, int tier, LootRarity rarity, int? setThemeId)
    {
        var pool = AffixDatabase.GetPool(ItemClassifier.GetCategory(item));
        var used = new HashSet<AffixStatType>();
        var list = new List<AffixRoll>();
        int count = RarityInfo.AffixCount(rarity);
        for (int i = 0; i < count; i++)
        {
            var def = AffixDatabase.PickWeighted(pool, used, setThemeId);
            if (def == null) break;
            used.Add(def.Stat);
            list.Add(new AffixRoll(def.Id, RollValue(def, tier, rarity), def.Theme));
        }
        return list;
    }

    /// <summary>重掷单条词缀（保留其余/稀有度/插槽/传说/套装）。若传入 preset 则写 preset（"演练"先预览后确认）。</summary>
    public static void RerollOne(Item item, AffixGlobalItem g, int index, AffixRoll? preset = null)
    {
        if (g == null || g.Affixes == null || index < 0 || index >= g.Affixes.Count) return;
        var roll = preset ?? PreviewRerollOne(item, g, index);
        if (roll == null) return;
        g.Affixes[index] = roll.Value;
        ApplyValue(item, g);
        g.PowerScore = PowerScore(g);
    }

    /// <summary>演练：掷一条新词缀但不写入，供 UI 显示"旧 → 新"。</summary>
    public static AffixRoll? PreviewRerollOne(Item item, AffixGlobalItem g, int index)
    {
        if (g == null || g.Affixes == null || index < 0 || index >= g.Affixes.Count) return null;
        var cat = ItemClassifier.GetCategory(item);
        int tier = g.Tier > 0 ? g.Tier : ItemClassifier.GetTier(item); // M1：复用缓存档位

        var used = new HashSet<AffixStatType>();
        for (int i = 0; i < g.Affixes.Count; i++)
        {
            if (i == index) continue;
            var d = AffixDatabase.GetById(g.Affixes[i].AffixId);
            if (d != null) used.Add(d.Stat); // L1：未知 id 防御
        }
        var def = AffixDatabase.PickWeighted(AffixDatabase.GetPool(cat), used, g.SetThemeId >= 0 ? g.SetThemeId : null);
        if (def == null) return null;
        return new AffixRoll(def.Id, RollValue(def, tier, g.Rarity), def.Theme);
    }

    /// <summary>全部重掷词缀（保留稀有度/插槽/传说/套装主题）。若传入 preset 则写 preset（"演练"先预览后确认）。</summary>
    public static void RerollAll(Item item, AffixGlobalItem g, List<AffixRoll>? preset = null)
    {
        if (g == null || g.Rarity == LootRarity.None) return;
        var affixes = preset ?? PreviewRerollAll(item, g);
        if (affixes == null) return;
        g.Affixes = affixes;
        ApplyValue(item, g);
        g.PowerScore = PowerScore(g);
    }

    /// <summary>演练：掷一组新词缀但不写入（保留插槽/传说/套装主题），供"旧 → 新"预览。</summary>
    public static List<AffixRoll>? PreviewRerollAll(Item item, AffixGlobalItem g)
    {
        if (g == null || g.Rarity == LootRarity.None) return null;
        int tier = g.Tier > 0 ? g.Tier : ItemClassifier.GetTier(item); // M1：复用缓存档位
        return RollAffixList(item, tier, g.Rarity, g.SetThemeId >= 0 ? g.SetThemeId : null);
    }

    /// <summary>
    /// 升一档稀有度（成功率可配置，默认 30%，失败不降级）。
    /// ⚠️ 保留式升级（修复"升档后词条变少/插槽变少"）：只升级稀有度并补齐新稀有度的专属项
    /// （传说之力 / 套装主题 / 自带插槽），【不重掷词缀、不动已有插槽与宝石】。
    /// 返回是否升级。
    /// </summary>
    public static bool UpgradeRarity(Item item, AffixGlobalItem g)
    {
        if (g == null || g.Rarity == LootRarity.None || g.Rarity >= LootRarity.Set) return false;
        float chance = LooteriaConfig.Instance?.UpgradeRarityChance ?? 0.30f;
        if (Main.rand.NextFloat() >= chance) return false;
        var next = (LootRarity)((int)g.Rarity + 1);
        g.Rarity = next;

        // 传说：原来没有传说之力才掷一条（升档新增，不覆盖已有）
        if (next == LootRarity.Legendary && g.LegendaryPowerId <= 0)
            g.LegendaryPowerId = (int)LegendaryPowerDatabase.PickRandom();
        // 套装：原来没有主题才掷（升级到套装获得主题）
        if (next == LootRarity.Set && g.SetThemeId < 0)
            g.SetThemeId = (byte)SetThemeDatabase.PickRandom();
        // 传说/套装自带插槽：一个都没有才补 1~2 个（已有插槽/宝石一律保留）
        if (RarityInfo.HasSockets(next) && g.SocketCount <= 0)
        {
            g.SocketCount = Main.rand.NextBool(3) ? 2 : 1;
            g.Sockets = new List<int>();
            for (int i = 0; i < g.SocketCount; i++) g.Sockets.Add(0);
        }

        ApplyValue(item, g);       // 价值按新稀有度倍率（BaseValue 幂等保留原值）
        g.PowerScore = PowerScore(g);
        g.Checked = true;
        return true;
    }

    public static void Clear(AffixGlobalItem g)
    {
        if (g == null) return;
        g.Rarity = LootRarity.None;
        g.Affixes ??= new List<AffixRoll>();
        g.Affixes.Clear();
        g.SocketCount = 0;
        g.Sockets = new List<int>();
        g.LegendaryPowerId = -1;
        g.SetThemeId = -1;
        g.Tier = 0;
        g.PowerScore = 0;
        g.BaseValue = 0;
    }

    /// <summary>M2：清词缀并还原物品售价（BaseValue 只存在于掷稀有度前，清后售价应回到原始值，否则传说×20 售价永久虚高）。</summary>
    public static void Clear(Item item, AffixGlobalItem g)
    {
        if (g == null) return;
        if (g.BaseValue > 0 && item != null && item.value > g.BaseValue)
            item.value = (int)Math.Min(g.BaseValue, int.MaxValue);
        Clear(g);
    }

    /// <summary>按稀有度倍率套用价值（幂等：BaseValue 只记一次）。</summary>
    public static void ApplyValue(Item item, AffixGlobalItem g)
    {
        if (g == null || g.Rarity == LootRarity.None) return;
        if (g.BaseValue <= 0) g.BaseValue = Math.Max(item.value, 1);
        item.value = (int)(g.BaseValue * RarityInfo.ValueMult(g.Rarity));
    }

    public static float RollValue(AffixDef def, int tier, LootRarity rarity)
    {
        float v = (def.Base + def.Step * (tier - 1)) * RarityInfo.Quality(rarity) * (0.8f + 0.4f * (float)Main.rand.NextDouble());
        // 上限：配置覆盖（0 = 用内置默认 Max）
        int ov = LooteriaConfig.Instance?.GetAffixMax(def.Stat) ?? 0;
        float max = ov > 0 ? ov : def.Max;
        if (v > max) v = max;
        if (v < 0) v = 0;
        return MathF.Round(v, 1);
    }

    /// <summary>力量等级 = 10*tier + Σ(值×权重)。</summary>
    public static int PowerScore(AffixGlobalItem g)
    {
        if (g == null) return 0;
        float s = 10f * g.Tier;
        if (g.Affixes != null)
        {
            foreach (var r in g.Affixes)
            {
                var def = AffixDatabase.GetById(r.AffixId);
                if (def == null) continue; // L1：未知词缀 id 防御（坏档/版本迁移）
                s += r.Value * def.PowerWeight;
            }
        }
        if (g.SocketCount > 0) s += 15 * g.SocketCount;
        if (g.LegendaryPowerId > 0) s += 30;
        if (g.SetThemeId >= 0) s += 20;
        return (int)s;
    }
}
