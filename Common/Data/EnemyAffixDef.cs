using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using global::Looteria.Common.Configs;

namespace Looteria.Common.Data;

/// <summary>
/// 敌人词缀稀有度：None（无词缀）/
/// 普通词缀（小怪 1 条）/
/// 精英词缀（Champion，精英怪 2~3 条）/
/// Boss 专属（BossExclusive，Boss 1~2 条，与普通词缀叠加）。
/// </summary>
public enum EnemyAffixRarity : byte
{
    None,
    Common,
    Champion,
    BossExclusive,
}

/// <summary>敌人词缀 ID（全部枚举，含 Boss 专属）。</summary>
public enum EnemyAffixId : byte
{
    // —— 普通 / 精英共用（CommonPool）——
    Strong,        // 强壮：生命 +50%（Champion 档 +100%）
    Berserk,       // 狂暴：伤害 +30%（Champion 档 +60%）
    Ironhide,      // 铁壁：防御 +10、减伤 10%
    Swift,         // 迅捷：移速 +25%（AI 每帧轻微加速，近似）
    Regen,         // 再生：每秒回复 1% 最大生命
    Burning,       // 燃烧：命中附加灼烧
    Poisonous,     // 剧毒：命中附加中毒
    Frost,         // 冰冻：命中附加霜冻（减速）
    Cursed,        // 诅咒：命中附加诅咒焰
    Bleeding,      // 流血：命中附加流血
    Thorns,        // 荆棘：反弹 25% 近战伤害
    Vampiric,      // 吸血：攻击伤害的 5% 回复自身
    Split,         // 分裂：死亡时分裂 2 只 50% 生命小怪
    Summoner,      // 召唤：死亡时召唤 2 只 20% 生命小怪
    Explosive,     // 爆炸：死亡时 160px 爆炸

    // —— Boss 专属（BossExclusivePool）——
    Apocalypse,    // 天启：伤害 +75%、攻速 +25%
    Immortal,      // 不朽：生命 +100%、每秒回复 2%
    Annihilation,  // 湮灭：死亡时 300px 大爆炸
    ElementLord,   // 元素领主：命中附加 2 种减益
    Warlord,       // 统帅：死亡时召唤 4 只小怪
    SplitRampage,  // 分裂狂潮：死亡时分裂 3 只 40% 生命小怪
    ThornsCrown,   // 荆棘王冠：反弹 50% 近战伤害
    VampireLord,   // 吸血伯爵：攻击伤害的 10% 回复自身
    StormEye,      // 风暴之眼：每 3 秒朝玩家发射 1 枚弹幕
    Fury,          // 狂怒：生命 <30% 时伤害 ×2（二阶段狂暴）
    SwiftHunter,   // 迅捷猎手：移速 +40%
    Unbreakable,   // 坚不可摧：防御 +30、减伤 20%
}

/// <summary>
/// 敌人词缀定义表 + 池。
/// 数值标定：全部词缀数值 × AffixPowerMult（配置，默认 1）× (1 + 0.15×阶段)（阶段缩放，可配置关闭）；
/// 属性乘数 clamp 到配置上限（默认 4×）防爆炸。数值全部集中在此表 + 配置，便于平衡回调。
/// </summary>
public static class EnemyAffixDatabase
{
    /// <summary>词缀显示名本地化键后缀（Mods.Looteria.EnemyAffix.&lt;Key&gt;）。</summary>
    public static string Key(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Strong => "Strong",
        EnemyAffixId.Berserk => "Berserk",
        EnemyAffixId.Ironhide => "Ironhide",
        EnemyAffixId.Swift => "Swift",
        EnemyAffixId.Regen => "Regen",
        EnemyAffixId.Burning => "Burning",
        EnemyAffixId.Poisonous => "Poisonous",
        EnemyAffixId.Frost => "Frost",
        EnemyAffixId.Cursed => "Cursed",
        EnemyAffixId.Bleeding => "Bleeding",
        EnemyAffixId.Thorns => "Thorns",
        EnemyAffixId.Vampiric => "Vampiric",
        EnemyAffixId.Split => "Split",
        EnemyAffixId.Summoner => "Summoner",
        EnemyAffixId.Explosive => "Explosive",
        EnemyAffixId.Apocalypse => "Apocalypse",
        EnemyAffixId.Immortal => "Immortal",
        EnemyAffixId.Annihilation => "Annihilation",
        EnemyAffixId.ElementLord => "ElementLord",
        EnemyAffixId.Warlord => "Warlord",
        EnemyAffixId.SplitRampage => "SplitRampage",
        EnemyAffixId.ThornsCrown => "ThornsCrown",
        EnemyAffixId.VampireLord => "VampireLord",
        EnemyAffixId.StormEye => "StormEye",
        EnemyAffixId.Fury => "Fury",
        EnemyAffixId.SwiftHunter => "SwiftHunter",
        EnemyAffixId.Unbreakable => "Unbreakable",
        _ => "Unknown",
    };

    /// <summary>词缀归属池。</summary>
    public static EnemyAffixRarity RarityOf(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Apocalypse or EnemyAffixId.Immortal or EnemyAffixId.Annihilation
            or EnemyAffixId.ElementLord or EnemyAffixId.Warlord or EnemyAffixId.SplitRampage
            or EnemyAffixId.ThornsCrown or EnemyAffixId.VampireLord or EnemyAffixId.StormEye
            or EnemyAffixId.Fury or EnemyAffixId.SwiftHunter or EnemyAffixId.Unbreakable
            => EnemyAffixRarity.BossExclusive,
        EnemyAffixId.Thorns or EnemyAffixId.Vampiric or EnemyAffixId.Split or EnemyAffixId.Summoner
            or EnemyAffixId.Explosive
            => EnemyAffixRarity.Champion,
        _ => EnemyAffixRarity.Common,
    };

    /// <summary>普通/精英共用池（按稀有度分档：普通用前段，精英用后段 + 中段）。</summary>
    public static readonly EnemyAffixId[] CommonPool =
    {
        EnemyAffixId.Strong,
        EnemyAffixId.Berserk,
        EnemyAffixId.Ironhide,
        EnemyAffixId.Swift,
        EnemyAffixId.Regen,
        EnemyAffixId.Burning,
        EnemyAffixId.Poisonous,
        EnemyAffixId.Frost,
        EnemyAffixId.Cursed,
        EnemyAffixId.Bleeding,
    };

    /// <summary>精英专属（Champion 档才有，效果更强）。</summary>
    public static readonly EnemyAffixId[] ChampionPool =
    {
        EnemyAffixId.Thorns,
        EnemyAffixId.Vampiric,
        EnemyAffixId.Split,
        EnemyAffixId.Summoner,
        EnemyAffixId.Explosive,
    };

    /// <summary>Boss 专属池。</summary>
    public static readonly EnemyAffixId[] BossExclusivePool =
    {
        EnemyAffixId.Apocalypse,
        EnemyAffixId.Immortal,
        EnemyAffixId.Annihilation,
        EnemyAffixId.ElementLord,
        EnemyAffixId.Warlord,
        EnemyAffixId.SplitRampage,
        EnemyAffixId.ThornsCrown,
        EnemyAffixId.VampireLord,
        EnemyAffixId.StormEye,
        EnemyAffixId.Fury,
        EnemyAffixId.SwiftHunter,
        EnemyAffixId.Unbreakable,
    };

    /// <summary>该词缀是否属于精英专属档（Champion 档效果翻倍/更强）。</summary>
    public static bool IsChampionTier(EnemyAffixId id) => RarityOf(id) == EnemyAffixRarity.Champion;

    /// <summary>该词缀是否为 Boss 专属。</summary>
    public static bool IsBossExclusive(EnemyAffixId id) => RarityOf(id) == EnemyAffixRarity.BossExclusive;

    // ===== 数值应用（全部走配置倍率 + 阶段缩放，集中在此表，便于平衡回调）=====

    /// <summary>阶段缩放系数：词缀数值 ×(1 + 0.15×阶段)（阶段 0 = 1x，阶段 7 ≈ 2.05x）。</summary>
    public static float StageScale()
    {
        if (EnemyAffixConfig.Instance is { StageScaling: false }) return 1f;
        return 1f + 0.15f * global::Looteria.Common.Data.Progression.CurrentStage();
    }

    /// <summary>词缀全局倍率（配置 AffixPowerMult × 阶段缩放）。</summary>
    public static float PowerMult()
    {
        var cfg = EnemyAffixConfig.Instance;
        float m = cfg?.AffixPowerMult ?? 1f;
        return m * StageScale();
    }

    /// <summary>生命乘数（含配置上限 clamp；记录到 g.LifeMult 供秘境防线还原）。</summary>
    public static float LifeMultFor(EnemyAffixId id, bool champion)
    {
        var cfg = EnemyAffixConfig.Instance;
        float cap = cfg?.LifeMultCap ?? 4f;
        float m = id switch
        {
            EnemyAffixId.Strong => champion ? 2.0f : 1.5f,
            EnemyAffixId.Immortal => 2.0f,
            _ => 1f,
        };
        return System.Math.Min(cap, m * PowerMult());
    }

    /// <summary>伤害乘数（含配置上限 clamp；记录到 g.DamageMult 供秘境防线还原）。</summary>
    public static float DamageMultFor(EnemyAffixId id, bool champion)
    {
        var cfg = EnemyAffixConfig.Instance;
        float cap = cfg?.DamageMultCap ?? 4f;
        float m = id switch
        {
            EnemyAffixId.Berserk => champion ? 1.6f : 1.3f,
            EnemyAffixId.Apocalypse => 1.75f,
            _ => 1f,
        };
        return System.Math.Min(cap, m * PowerMult());
    }

    /// <summary>减伤%（玩家 → NPC 伤害减免，0~0.8）。</summary>
    public static float DamageReductionFor(EnemyAffixId id)
    {
        float m = id switch
        {
            EnemyAffixId.Ironhide => 0.10f,
            EnemyAffixId.Unbreakable => 0.20f,
            _ => 0f,
        };
        return System.Math.Min(0.8f, m * PowerMult());
    }

    /// <summary>防御加成（平坦值）。</summary>
    public static int DefenseBonusFor(EnemyAffixId id)
    {
        float m = id switch
        {
            EnemyAffixId.Ironhide => 10f,
            EnemyAffixId.Unbreakable => 30f,
            _ => 0f,
        };
        return (int)(m * PowerMult());
    }

    /// <summary>荆棘固定反伤（不随玩家伤害放大；× PowerMult 全局倍率）。</summary>
    public static int ThornsDamageFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Thorns => 15,
        EnemyAffixId.ThornsCrown => 30,
        _ => 0,
    };

    /// <summary>吸血（攻击造成伤害的 % 回复自身，0~0.5）。</summary>
    public static float LifestealFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Vampiric => 0.05f,
        EnemyAffixId.VampireLord => 0.10f,
        _ => 0f,
    };

    /// <summary>再生（每秒回复最大生命 %，0~0.1）。</summary>
    public static float RegenPctFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Regen => 0.01f,
        EnemyAffixId.Immortal => 0.02f,
        _ => 0f,
    };

    /// <summary>移速加成（% 每帧，仅 AI 近似，0~0.6）。</summary>
    public static float SpeedMultFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Swift => 0.25f,
        EnemyAffixId.SwiftHunter => 0.40f,
        _ => 0f,
    };

    /// <summary>狂怒二阶段：生命 <30% 时伤害 ×2（Boss 专属）。</summary>
    public static bool HasFury(EnemyAffixId id) => id == EnemyAffixId.Fury;

    /// <summary>命中附加的减益（BuffID，0=无）。</summary>
    public static int HitDebuffFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Burning => BuffID.OnFire,
        EnemyAffixId.Poisonous => BuffID.Poisoned,
        EnemyAffixId.Frost => BuffID.Frostburn,
        EnemyAffixId.Cursed => BuffID.CursedInferno,
        EnemyAffixId.Bleeding => BuffID.Bleeding,
        EnemyAffixId.ElementLord => BuffID.OnFire,   // 主减益；ElementLord 另加一种见下
        _ => 0,
    };

    /// <summary>ElementLord 第二减益。</summary>
    public static int HitDebuff2For(EnemyAffixId id) => id switch
    {
        EnemyAffixId.ElementLord => BuffID.Frostburn,
        _ => 0,
    };

    /// <summary>分裂/召唤：死亡时生成的小怪比例（0=无）。</summary>
    public static float SplitRatioFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Split => 0.5f,
        EnemyAffixId.Summoner => 0.2f,
        EnemyAffixId.SplitRampage => 0.4f,
        EnemyAffixId.Warlord => 0.2f,
        _ => 0f,
    };

    /// <summary>分裂/召唤：死亡时生成的小怪数量（0=无）。</summary>
    public static int SpawnCountFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Split => 2,
        EnemyAffixId.Summoner => 2,
        EnemyAffixId.SplitRampage => 3,
        EnemyAffixId.Warlord => 4,
        _ => 0,
    };

    /// <summary>死亡爆炸半径（px，0=无）。</summary>
    public static float ExplosionRadiusFor(EnemyAffixId id) => id switch
    {
        EnemyAffixId.Explosive => 160f,
        EnemyAffixId.Annihilation => 300f,
        _ => 0f,
    };

    /// <summary>风暴之眼：每 N tick 发射 1 枚弹幕（0=无）。</summary>
    public static int StormEyeIntervalFor(EnemyAffixId id) => id == EnemyAffixId.StormEye ? 180 : 0;
}
