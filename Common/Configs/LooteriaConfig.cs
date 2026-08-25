using System.Collections.Generic;
using System.ComponentModel;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using global::Looteria.Common.Data;

namespace Looteria.Common.Configs;

/// <summary>
/// 模组配置。ServerSide：多人下全端一致（配置类自动注册）。
/// ⚠️ 配置项显示名的本地化键 = Mods.Looteria.Configs.LooteriaConfig.<字段>.Label/.Tooltip
///   （注意是 Configs 复数 + 类名；配置名 = ...Configs.LooteriaConfig.DisplayName；见 Localization/*.hjson）
/// ⚠️ 默认值铁律：值类型（bool/int/float）**必须**加 [DefaultValue]！
///    tML 用 Newtonsoft PopulateObject + DefaultValueHandling.IgnoreAndPopulate 反序列化配置：
///    JSON 里缺失的字段会被重置成 default(T)（0/false），字段初始化器 `= x` 不生效。
///    只有 [DefaultValue] 才能让"旧配置缺新字段 / 全新无配置文件"时正确落到默认值。
///    引用类型（如 List）用字段初始化器即可（见 ExampleMod.ModConfigShowcaseDefaultValues）。
/// ⚠️ 滑块范围坑：float/int 不加 [Range] 时，tML 配置 UI 默认只给 0~1（float）/0~100（int）滑块，
///    所以每个数值项都必须显式 [Range(min,max)] + [Increment(step)]，否则 UI 调不到声明范围。
/// </summary>
public class LooteriaConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;

    // ===== 总开关 =====
    /// <summary>总开关。</summary>
    [DefaultValue(true)]
    public bool Enable = true;

    /// <summary>是否作用于其它模组的物品（兼容性需求#1）。</summary>
    [DefaultValue(true)]
    public bool AffectModdedItems = true;

    // ===== 作用类别 =====
    [DefaultValue(true)]
    public bool AffectWeapons = true;
    [DefaultValue(true)]
    public bool AffectArmor = true;
    [DefaultValue(true)]
    public bool AffectAccessories = true;
    [DefaultValue(true)]
    public bool AffectTools = true;   // 默认开启（工具/镐/钓竿也吃词缀）

    // ===== 掉率 / 货币 =====
    /// <summary>掉落率倍率（0.1~5）。作用于魔法及以上稀有度的权重。</summary>
    [Range(0.1f, 5f)]
    [Increment(0.1f)]
    [DefaultValue(1f)]
    public float DropRateMult = 1f;

    /// <summary>血岩获取倍率（1~10）。</summary>
    [Range(1f, 10f)]
    [Increment(0.5f)]
    [DefaultValue(1f)]
    public float BloodShardRate = 1f;

    /// <summary>重铸之尘获取倍率（1~10）。</summary>
    [Range(1f, 10f)]
    [Increment(0.5f)]
    [DefaultValue(1f)]
    public float DustRate = 1f;

    /// <summary>阶段掉落倍率上限：血岩/尘随怪物阶段（0~7）从初始 1x 线性涨到该值（默认 10x）。</summary>
    [Range(1f, 100f)]
    [Increment(1f)]
    [DefaultValue(10f)]
    public float MaxStageDropMult = 10f;

    // ===== 抽奖（原"赌博"） =====
    /// <summary>血岩抽奖中奖率（抽中当前档装备的概率）。</summary>
    [Range(0.01f, 0.5f)]
    [Increment(0.005f)]
    [DefaultValue(0.10f)]
    public float GambleWinChance = 0.10f;

    /// <summary>中奖内稀有度权重：套装（相对权重，默认 8 → 占中奖 8%）。</summary>
    [Range(0, 100)]
    [Increment(1)]
    [DefaultValue(8)]
    public int WinSetWeight = 8;

    /// <summary>中奖内稀有度权重：传说（相对权重，默认 16 → 占中奖 16%）。</summary>
    [Range(0, 100)]
    [Increment(1)]
    [DefaultValue(16)]
    public int WinLegendaryWeight = 16;

    /// <summary>中奖内稀有度权重：稀有（相对权重，默认 30 → 占中奖 30%）。</summary>
    [Range(0, 100)]
    [Increment(1)]
    [DefaultValue(30)]
    public int WinRareWeight = 30;

    /// <summary>中奖内稀有度权重：魔法（相对权重，默认 46 → 占中奖 46%）。</summary>
    [Range(0, 100)]
    [Increment(1)]
    [DefaultValue(46)]
    public int WinMagicWeight = 46;

    /// <summary>掠夺容器最大格数（多人同步上限 255 格/包，故上限钳制 255；单机不受此限但配置统一）。</summary>
    [Range(20, 255)]
    [Increment(10)]
    [DefaultValue(200)]
    public int GambleContainerSize = 200;

    // ===== 重铸 / 升档 / 拆解 =====
    /// <summary>重铸单条费用（重铸之尘）。</summary>
    [Range(0, 500)]
    [Increment(5)]
    [DefaultValue(60)]
    public int RerollOneDust = 60;

    /// <summary>全部重铸费用（重铸之尘）。</summary>
    [Range(0, 500)]
    [Increment(5)]
    [DefaultValue(20)]
    public int RerollAllDust = 20;

    /// <summary>稀有度升档费用（重铸之尘）。</summary>
    [Range(0, 500)]
    [Increment(5)]
    [DefaultValue(120)]
    public int UpgradeDust = 120;

    /// <summary>稀有度升档成功率。</summary>
    [Range(0.01f, 1f)]
    [Increment(0.01f)]
    [DefaultValue(0.30f)]
    public float UpgradeRarityChance = 0.30f;

    /// <summary>开槽费用（重铸之尘）：开槽 = 该值尘 + 装备价值/开槽钱币除数 钱币 + 1 件同名装备。M15（原为完全免费）。</summary>
    [Range(0, 500)]
    [Increment(5)]
    [DefaultValue(40)]
    public int SocketCostDust = 40;

    // ===== 钱币花销（各项独立除数：钱币费用 = 装备价值 / 该值，最低 1 铜）=====
    /// <summary>重铸单条钱币除数（钱币 = 装备价值 ÷ 该值）。默认 50（≈原 value/10 再 ÷50 后 ×10）。</summary>
    [Range(1, 10000)]
    [Increment(10)]
    [DefaultValue(50)]
    public int RerollOneCoinDiv = 50;

    /// <summary>全部重铸钱币除数（钱币 = 装备价值 ÷ 该值）。默认 100（≈原 value/20 再 ÷50 后 ×10）。</summary>
    [Range(1, 10000)]
    [Increment(10)]
    [DefaultValue(100)]
    public int RerollAllCoinDiv = 100;

    /// <summary>稀有度升档钱币除数（钱币 = 装备价值 ÷ 该值）。默认 50（≈原 value/10 再 ÷50 后 ×10）。</summary>
    [Range(1, 10000)]
    [Increment(10)]
    [DefaultValue(50)]
    public int UpgradeCoinDiv = 50;

    /// <summary>开槽钱币除数（钱币 = 装备价值 ÷ 该值）。默认 100（≈原 value/20 再 ÷50 后 ×10）。</summary>
    [Range(1, 10000)]
    [Increment(10)]
    [DefaultValue(100)]
    public int SocketCoinDiv = 100;

    /// <summary>宝石升阶钱币除数（钱币 = 宝石价值 ÷ 该值）。默认 50（≈原 value/10 再 ÷50 后 ×10）。</summary>
    [Range(1, 10000)]
    [Increment(10)]
    [DefaultValue(50)]
    public int GemUpgradeCoinDiv = 50;

    /// <summary>拆解重铸之尘除数：拆解获得尘 = 力量等级 / 该值（≥1，默认 2）。</summary>
    [Range(1, 100)]
    [DefaultValue(2)]
    public int SalvageDivisor = 2;

    // ===== 精英 / 秘境 =====
    /// <summary>非 Boss 敌人成为精英怪的概率。</summary>
    [Range(0f, 0.5f)]
    [Increment(0.01f)]
    [DefaultValue(0.08f)]
    public float EliteChance = 0.08f;

    /// <summary>秘境开启消耗 = 层数 × 该值（血岩）。</summary>
    [Range(0, 500)]
    [Increment(5)]
    [DefaultValue(50)]
    public int RiftCostPerLevel = 50;

    /// <summary>秘境单层时长（分钟，霜月 = 9）。</summary>
    [Range(1f, 30f)]
    [Increment(0.5f)]
    [DefaultValue(9f)]
    public float RiftDurationMinutes = 9f;

    // ===== 杂项 =====
    /// <summary>谢谢惠顾券使用获得的安慰重铸之尘。</summary>
    [Range(0, 100)]
    [DefaultValue(5)]
    public int TicketDust = 5;

    /// <summary>单件装备插槽总数上限。</summary>
    [Range(1, 12)]
    [DefaultValue(6)]
    public int MaxSockets = 6;

    /// <summary>单件装备可通过开槽手动增加的插槽数上限。</summary>
    [Range(0, 8)]
    [DefaultValue(4)]
    public int MaxOpenedSockets = 4;

    // ===== 词缀数值上限（默认 = 设计表内置上限，逐类可调）=====
    // 与 _devmem/02-design-tables.md / AffixDatabase.Max 保持一致；调小即削弱该属性。
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxAllDamage = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(150)] public int AffixMaxMeleeDamage = 150;
    [Range(0, 500)] [Increment(1)] [DefaultValue(150)] public int AffixMaxRangedDamage = 150;
    [Range(0, 500)] [Increment(1)] [DefaultValue(150)] public int AffixMaxMagicDamage = 150;
    [Range(0, 500)] [Increment(1)] [DefaultValue(150)] public int AffixMaxSummonDamage = 150;
    [Range(0, 500)] [Increment(1)] [DefaultValue(30)] public int AffixMaxCritChance = 30;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxCritDamage = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(60)] public int AffixMaxAttackSpeed = 60;
    [Range(0, 500)] [Increment(1)] [DefaultValue(60)] public int AffixMaxFlatDamage = 60;
    [Range(0, 500)] [Increment(1)] [DefaultValue(3)] public int AffixMaxLifeOnHit = 3;
    [Range(0, 500)] [Increment(1)] [DefaultValue(2)] public int AffixMaxManaOnHit = 2;
    [Range(0, 500)] [Increment(1)] [DefaultValue(50)] public int AffixMaxManaCost = 50;
    [Range(0, 500)] [Increment(1)] [DefaultValue(150)] public int AffixMaxKnockback = 150;
    [Range(0, 500)] [Increment(1)] [DefaultValue(12)] public int AffixMaxDefense = 12;
    [Range(0, 500)] [Increment(1)] [DefaultValue(5)] public int AffixMaxDamageReduction = 5;
    [Range(0, 500)] [Increment(1)] [DefaultValue(200)] public int AffixMaxMaxLife = 200;
    [Range(0, 500)] [Increment(1)] [DefaultValue(6)] public int AffixMaxLifeRegen = 6;
    [Range(0, 500)] [Increment(1)] [DefaultValue(10)] public int AffixMaxManaRegen = 10;
    [Range(0, 500)] [Increment(1)] [DefaultValue(40)] public int AffixMaxMoveSpeed = 40;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxBuffResistPoison = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxBuffResistFire = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxBuffResistBleed = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxBuffResistCurse = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(100)] public int AffixMaxBuffResistSlow = 100;
    [Range(0, 500)] [Increment(1)] [DefaultValue(80)] public int AffixMaxMiningSpeed = 80;
    [Range(0, 500)] [Increment(1)] [DefaultValue(40)] public int AffixMaxFishingPower = 40;
    [Range(0, 100)] [Increment(1)] [DefaultValue(5)] public int AffixMaxFlatArmorShred = 5;
    [Range(0, 100)] [Increment(1)] [DefaultValue(5)] public int AffixMaxPctArmorShred = 5;

    /// <summary>词缀掷值上限（默认 = 设计表内置上限；0 等同内置默认）。</summary>
    public int GetAffixMax(AffixStatType stat) => stat switch
    {
        AffixStatType.AllDamage => AffixMaxAllDamage,
        AffixStatType.MeleeDamage => AffixMaxMeleeDamage,
        AffixStatType.RangedDamage => AffixMaxRangedDamage,
        AffixStatType.MagicDamage => AffixMaxMagicDamage,
        AffixStatType.SummonDamage => AffixMaxSummonDamage,
        AffixStatType.CritChance => AffixMaxCritChance,
        AffixStatType.CritDamage => AffixMaxCritDamage,
        AffixStatType.AttackSpeed => AffixMaxAttackSpeed,
        AffixStatType.FlatDamage => AffixMaxFlatDamage,
        AffixStatType.LifeOnHit => AffixMaxLifeOnHit,
        AffixStatType.ManaOnHit => AffixMaxManaOnHit,
        AffixStatType.ManaCost => AffixMaxManaCost,
        AffixStatType.Knockback => AffixMaxKnockback,
        AffixStatType.Defense => AffixMaxDefense,
        AffixStatType.DamageReduction => AffixMaxDamageReduction,
        AffixStatType.MaxLife => AffixMaxMaxLife,
        AffixStatType.LifeRegen => AffixMaxLifeRegen,
        AffixStatType.ManaRegen => AffixMaxManaRegen,
        AffixStatType.MoveSpeed => AffixMaxMoveSpeed,
        AffixStatType.BuffResistPoison => AffixMaxBuffResistPoison,
        AffixStatType.BuffResistFire => AffixMaxBuffResistFire,
        AffixStatType.BuffResistBleed => AffixMaxBuffResistBleed,
        AffixStatType.BuffResistCurse => AffixMaxBuffResistCurse,
        AffixStatType.BuffResistSlow => AffixMaxBuffResistSlow,
        AffixStatType.MiningSpeed => AffixMaxMiningSpeed,
        AffixStatType.FishingPower => AffixMaxFishingPower,
        AffixStatType.FlatArmorShred => AffixMaxFlatArmorShred,
        AffixStatType.PctArmorShred => AffixMaxPctArmorShred,
        _ => 0
    };

    /// <summary>排除物品（按显示名）。</summary>
    public List<string> ExcludedItems = new();

    private static LooteriaConfig? _instance;
    public static LooteriaConfig Instance => _instance ??= ModContent.GetInstance<LooteriaConfig>();

    /// <summary>是否在排除列表（按显示名）。</summary>
    public bool IsExcluded(Item item) => ExcludedItems != null && ExcludedItems.Contains(item.Name);
}
