using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;

namespace Looteria.Common.Effects;

/// <summary>
/// 词缀数值应用（组合式钩子共用入口）。
/// 武器 → 武器钩子；护甲/饰品 → UpdateEquip。
/// 只做乘法/加法修饰，绝不直接改 item.damage/defense，保证与其它模组共存。
/// </summary>
public static class AffixStats
{
    public static float Sum(AffixGlobalItem? g, AffixStatType stat)
    {
        if (g == null || g.Affixes == null) return 0;
        float s = 0;
        foreach (var r in g.Affixes)
        {
            var d = AffixDatabase.GetById(r.AffixId);
            if (d != null && d.Stat == stat) s += r.Value; // L1：未知 id 防御
        }
        return s;
    }

    /// <summary>反饱和：x/(1+x*0.02)，防止同属性叠加爆炸。</summary>
    public static float Diminish(float x) => x / (1f + x * 0.02f);

    /// <summary>武器伤害：全伤害(递减) + 职业伤害(递减) 合并乘法 + 平坦伤害。</summary>
    public static void ApplyWeaponDamage(Item item, AffixGlobalItem g, ref StatModifier damage)
    {
        float all = Sum(g, AffixStatType.AllDamage);
        float specific = 0;
        var dc = item.DamageType;
        if (dc == DamageClass.Melee) specific = Sum(g, AffixStatType.MeleeDamage);
        else if (dc == DamageClass.Ranged) specific = Sum(g, AffixStatType.RangedDamage);
        else if (dc == DamageClass.Magic) specific = Sum(g, AffixStatType.MagicDamage);
        else if (dc == DamageClass.Summon) specific = Sum(g, AffixStatType.SummonDamage);

        damage *= 1f + Diminish(all + specific) / 100f;
        damage.Flat += Sum(g, AffixStatType.FlatDamage);
    }

    /// <summary>护甲/饰品词缀（UpdateEquip 里调用）。</summary>
    public static void ApplyEquip(Player player, AffixGlobalItem g)
    {
        if (g == null || g.Rarity == LootRarity.None) return;

        player.statDefense += (int)Sum(g, AffixStatType.Defense);
        player.endurance += Diminish(Sum(g, AffixStatType.DamageReduction)) / 100f;
        player.statLifeMax2 += (int)Sum(g, AffixStatType.MaxLife);
        // L11：LifeRegen 词缀低 tier（Base 0.5/Step 0.3）向下取整恒为 0 → 低级词缀完全无效。
        // 改用 Ceiling 保证 ≥1 tick 生效。
        player.lifeRegen += (int)MathF.Ceiling(Sum(g, AffixStatType.LifeRegen) * 2);
        player.manaRegenBonus += (int)Sum(g, AffixStatType.ManaRegen);
        player.moveSpeed *= 1f + Diminish(Sum(g, AffixStatType.MoveSpeed)) / 100f;
        player.GetDamage(DamageClass.Generic) += Diminish(Sum(g, AffixStatType.AllDamage)) / 100f;
        player.GetCritChance(DamageClass.Generic) += (int)Sum(g, AffixStatType.CritChance);

        if (Sum(g, AffixStatType.BuffResistPoison) > 0) player.buffImmune[BuffID.Poisoned] = true;
        if (Sum(g, AffixStatType.BuffResistFire) > 0) player.buffImmune[BuffID.OnFire] = true;
        if (Sum(g, AffixStatType.BuffResistBleed) > 0) player.buffImmune[BuffID.Bleeding] = true;
        if (Sum(g, AffixStatType.BuffResistCurse) > 0) player.buffImmune[BuffID.Cursed] = true;
        if (Sum(g, AffixStatType.BuffResistSlow) > 0) player.buffImmune[BuffID.Slow] = true;

        // 命中类/暴伤类词缀是"被动"（武器上用钩子，饰品上攒到 ModPlayer 命中时统一结算）
        var lp = player.GetModPlayer<LooteriaPlayer>();
        lp.PassiveLifeOnHit += Sum(g, AffixStatType.LifeOnHit);
        lp.PassiveManaOnHit += (int)Sum(g, AffixStatType.ManaOnHit);
        lp.PassiveCritDamage += Sum(g, AffixStatType.CritDamage);

        // 插槽宝石（值编码 = gemId + upgrade*1000；0=空）
        if (g.Sockets != null)
        {
            foreach (int sock in g.Sockets)
            {
                if (sock > 0)
                {
                    int gemId = sock % 1000;
                    int upgrade = sock / 1000;
                    if (GemDatabase.IsValid(gemId)) ApplyGem(player, gemId, upgrade);
                }
            }
        }
    }

    /// <summary>宝石效果（6 型 × 4 级 × 强化）。强化每阶 +10% 效果。</summary>
    private static void ApplyGem(Player player, int gemId, int upgrade)
    {
        var type = GemDatabase.GetType(gemId);
        float k = (GemDatabase.GetLevel(gemId) + 1) * (1f + 0.1f * upgrade);
        switch (type)
        {
            case GemType.Ruby: player.GetDamage(DamageClass.Generic) += 0.03f * k; break;                 // +3%/级 全伤害
            case GemType.Sapphire: player.GetCritChance(DamageClass.Generic) += 1.5f * k; break;          // +1.5%/级 暴击
            case GemType.Emerald: player.moveSpeed *= 1f + 0.02f * k; player.lifeRegen += (int)MathF.Ceiling(0.3f * k * 2); break; // L11：Ceiling 防低级截断为 0
            case GemType.Amethyst: player.statLifeMax2 += (int)(12f * k); break;                          // +12/级 生命
            case GemType.Topaz: player.statDefense += (int)(1.5f * k); break;                             // +1.5/级 防御
            case GemType.Diamond: player.endurance += 0.01f * k; break;                                   // +1%/级 减伤
        }
    }
}
