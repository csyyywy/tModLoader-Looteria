using System.Collections.Generic;
using System.ComponentModel;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace Looteria.Common.Configs;

/// <summary>敌人词缀显示模式：InName = 名字前缀（原模式）；UnderHealthBar = 血条下方彩色词缀标签（默认）。</summary>
public enum AffixDisplayMode
{
    /// <summary>词缀渲染进敌人名字前缀（如「狂暴的骷髅」），并按稀有度染色。</summary>
    InName,

    /// <summary>名字不带词缀；词缀以彩色标签渲染在敌人血条下方（每条一行）。</summary>
    UnderHealthBar,
}

/// <summary>
/// 敌人词缀配置（独立配置文件，专门管理敌人系统）。
/// ⚠️ 值类型（bool/int/float）**必须**加 [DefaultValue]（铁律，见 LooteriaConfig 头注释）。
/// ⚠️ float/int 必须显式 [Range] + [Increment]（否则 tML 配置 UI 只给 0~1 / 0~100 滑块）。
/// 配置项显示名本地化键 = Mods.Looteria.Configs.EnemyAffixConfig.&lt;字段&gt;.Label/.Tooltip
///   （注意是 Configs 复数 + 类名；配置名 = ...Configs.EnemyAffixConfig.DisplayName）。
/// </summary>
public class EnemyAffixConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;

    /// <summary>敌人词缀总开关。</summary>
    [DefaultValue(true)]
    public bool Enable = true;

    /// <summary>普通怪带 1 条普通词缀的概率。</summary>
    [Range(0f, 0.5f)]
    [Increment(0.01f)]
    [DefaultValue(0.20f)]
    public float CommonAffixChance = 0.20f;

    /// <summary>非 Boss 敌人成为精英怪的概率（自 LooteriaConfig.EliteChance 迁移）。</summary>
    [Range(0f, 0.5f)]
    [Increment(0.01f)]
    [DefaultValue(0.08f)]
    public float EliteChance = 0.08f;

    /// <summary>精英怪词缀条数下限。</summary>
    [Range(1, 6)]
    [Increment(1)]
    [DefaultValue(2)]
    public int EliteAffixCountMin = 2;

    /// <summary>精英怪词缀条数上限。</summary>
    [Range(1, 6)]
    [Increment(1)]
    [DefaultValue(3)]
    public int EliteAffixCountMax = 3;

    /// <summary>Boss 普通词缀条数下限（Boss 吃普通词缀 + Boss 专属）。</summary>
    [Range(0, 8)]
    [Increment(1)]
    [DefaultValue(3)]
    public int BossAffixCountMin = 3;

    /// <summary>Boss 普通词缀条数上限。</summary>
    [Range(0, 8)]
    [Increment(1)]
    [DefaultValue(4)]
    public int BossAffixCountMax = 4;

    /// <summary>Boss 专属词缀条数下限。</summary>
    [Range(0, 4)]
    [Increment(1)]
    [DefaultValue(1)]
    public int BossExclusiveCountMin = 1;

    /// <summary>Boss 专属词缀条数上限。</summary>
    [Range(0, 4)]
    [Increment(1)]
    [DefaultValue(2)]
    public int BossExclusiveCountMax = 2;

    /// <summary>词缀数值全局倍率。</summary>
    [Range(0.25f, 3f)]
    [Increment(0.05f)]
    [DefaultValue(1f)]
    public float AffixPowerMult = 1f;

    /// <summary>生命乘数上限（防词缀+模式+层叠加爆炸）。</summary>
    [Range(1f, 10f)]
    [Increment(0.5f)]
    [DefaultValue(4f)]
    public float LifeMultCap = 4f;

    /// <summary>伤害乘数上限（防词缀+模式+层叠加爆炸）。</summary>
    [Range(1f, 10f)]
    [Increment(0.5f)]
    [DefaultValue(4f)]
    public float DamageMultCap = 4f;

    /// <summary>词缀数值是否随游戏阶段（0~7）上浮（×(1+0.15×阶段)）。</summary>
    [DefaultValue(true)]
    public bool StageScaling = true;

    /// <summary>词缀显示模式：InName = 名字前缀（如「狂暴的骷髅」）；UnderHealthBar = 血条下方彩色词缀标签。</summary>
    [DefaultValue(AffixDisplayMode.UnderHealthBar)]
    public AffixDisplayMode AffixDisplayMode = AffixDisplayMode.UnderHealthBar;

    /// <summary>词缀是否显示在敌人名字前缀（如「狂暴的骷髅」）。旧配置项，仅 AffixDisplayMode=InName 时生效。</summary>
    [DefaultValue(true)]
    public bool ShowAffixInName = true;

    /// <summary>精英掉落加成（精英货币 ×(1 + 词缀数 × 该值)）。</summary>
    [Range(0f, 1f)]
    [Increment(0.05f)]
    [DefaultValue(0.15f)]
    public float EliteDropBonusPerAffix = 0.15f;

    /// <summary>按显示名排除的 NPC（不附加词缀；Boss 专属也可排除）。</summary>
    public List<string> ExcludedNpcs = new();

    /// <summary>
    /// 词缀数值覆盖：key = 词缀英文 Key（如 "Strong"/"Berserk"/"Thorns"），value = 覆盖倍率（0 = 用默认）。
    /// 例：{"Strong": 3} → 「强壮」生命加成 ×3；{"Thorns": 0} → 关掉荆棘反伤。
    /// 适用于：数值类词缀（生命/伤害/防御/减伤/荆棘/吸血/再生/移速/爆炸半径/分裂比例）。
    /// </summary>
    public Dictionary<string, float> AffixValueOverrides = new();

    private static EnemyAffixConfig? _instance;
    public static EnemyAffixConfig Instance => _instance ??= ModContent.GetInstance<EnemyAffixConfig>();

    /// <summary>查询词缀覆盖倍率（0 = 无覆盖，用默认）。</summary>
    public float GetAffixOverride(string key)
    {
        if (AffixValueOverrides != null && AffixValueOverrides.TryGetValue(key, out float v))
            return v;
        return 0f;
    }

    /// <summary>是否在排除列表（按显示名）。</summary>
    public bool IsExcluded(NPC npc)
    {
        if (ExcludedNpcs == null || ExcludedNpcs.Count == 0) return false;
        string name = npc.GivenOrTypeName;
        if (string.IsNullOrEmpty(name)) name = Terraria.Lang.GetNPCNameValue(npc.type);
        return ExcludedNpcs.Contains(name);
    }
}
