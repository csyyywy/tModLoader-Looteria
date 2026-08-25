namespace Looteria.Common.Data;

/// <summary>
/// 物品分类（Flags）。AffixDef.Categories 与物品实际类别做位与判定。
/// Weapon=15 = 全部四种武器；Armor=112 = 头|身|腿。
/// </summary>
[System.Flags]
public enum ItemCategory : ushort
{
    MeleeWeapon = 1 << 0,
    RangedWeapon = 1 << 1,
    MagicWeapon = 1 << 2,
    SummonWeapon = 1 << 3,
    Weapon = MeleeWeapon | RangedWeapon | MagicWeapon | SummonWeapon,
    HeadArmor = 1 << 4,
    BodyArmor = 1 << 5,
    LegsArmor = 1 << 6,
    Armor = HeadArmor | BodyArmor | LegsArmor,
    Accessory = 1 << 7,
    Tool = 1 << 8,
    Fishing = 1 << 9
}

/// <summary>词缀作用的属性类型（同一属性可能有多个主题变体）。</summary>
public enum AffixStatType : byte
{
    AllDamage, MeleeDamage, RangedDamage, MagicDamage, SummonDamage,
    CritChance, CritDamage, AttackSpeed, FlatDamage,
    LifeOnHit, ManaOnHit, ManaCost, Knockback,
    Defense, DamageReduction, MaxLife, LifeRegen, ManaRegen, MoveSpeed,
    BuffResistPoison, BuffResistFire, BuffResistBleed, BuffResistCurse, BuffResistSlow,
    MiningSpeed, FishingPower,
    FlatArmorShred, PctArmorShred   // 破甲：固定破防 / 百分比破防（参照鲨鱼项链：无视 5 点敌防）
}

/// <summary>主题标签（套装同源）。</summary>
public enum Theme : byte
{
    Fire, Ice, Lightning, Poison, Shadow, Holy,
    Melee, Ranged, Magic, Summon, Defense, Speed, Luck
}

/// <summary>
/// 词缀定义（不可变）。一条"机械属性 × 主题"的变体。
/// </summary>
public sealed class AffixDef
{
    public readonly int Id;
    public readonly string Key;                    // 本地化键后缀（Mods.Looteria.Affix.<Key>，值含 {0} 占位符）
    public readonly AffixStatType Stat;
    public readonly ItemCategory Categories;       // 可作用类别（位掩码）
    public readonly float Base, Step, Max;         // 数值 = (Base + Step*(tier-1)) * 品质 * 浮动，clamp Max
    public readonly byte Theme;
    public readonly float Weight;                  // 抽取权重
    public readonly float PowerWeight;             // 力量等级权重
    public readonly bool IsPercent;                // 显示/递减是否按百分比

    public AffixDef(int id, string key, AffixStatType stat, ItemCategory cats,
        float baseV, float step, float max, byte theme, bool isPercent,
        float weight = 1f, float powerWeight = 1f)
    {
        Id = id; Key = key; Stat = stat; Categories = cats;
        Base = baseV; Step = step; Max = max; Theme = theme;
        IsPercent = isPercent; Weight = weight; PowerWeight = powerWeight;
    }
}

/// <summary>一条已掷出的词缀。</summary>
public readonly struct AffixRoll
{
    public readonly int AffixId;
    public readonly float Value;
    public readonly byte Theme;
    public AffixRoll(int affixId, float value, byte theme) { AffixId = affixId; Value = value; Theme = theme; }
}
