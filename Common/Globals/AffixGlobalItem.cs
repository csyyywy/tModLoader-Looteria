using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Data;
using global::Looteria.Common.Effects;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;
using global::Looteria.Common.Systems;

namespace Looteria.Common.Globals;

/// <summary>
/// ★核心：对"所有物品（含其它模组）"附加词缀的全局类。
/// 模式照抄官方 ExampleMod/Common/GlobalItems/WeaponWithGrowingDamage：
///   InstancePerEntity → 每物品一个实例，实例字段即词缀数据；
///   SaveData/LoadData → 对所有物品持久化（ItemIO.SaveGlobals 遍历所有 GlobalItem）；
///   Clone → 物品克隆（tooltip/拆分）时数据随迁。
/// </summary>
public class AffixGlobalItem : GlobalItem
{
    /// <summary>真实装备槽数量（0-8 装备+饰品扩展，9 大师饰品位）；10 起为时装/社交位，不计入任何效果。M7。</summary>
    public const int RealEquipSlots = 10;

    // ===== 词缀数据（实例字段 = 每物品数据）=====
    public LootRarity Rarity;
    public List<AffixRoll>? Affixes;
    public int SocketCount;
    public List<int> Sockets = new();          // 每槽宝石 id，0=空（Phase 3 使用）
    public int LegendaryPowerId = -1;
    public int SetThemeId = -1;
    public int Tier;
    public int PowerScore;
    public long BaseValue;             // 掷稀有度前的原始价值（幂等价值套用）
    /// <summary>通过"开槽"打开的插槽数（最多 4；传说/套装自带插槽不计入）。</summary>
    public int OpenedSockets;
    /// <summary>是否已判定过词缀（掷空也算，防 UpdateInventory 每帧重掷）。持久化。</summary>
    public bool Checked;

    public bool HasAffix => Rarity != LootRarity.None;

    public override bool InstancePerEntity => true;

    public override bool AppliesToEntity(Item entity, bool lateInstantiation)
        => lateInstantiation && ItemClassifier.IsEligible(entity);

    /// <summary>
    /// 默认 Clone 是浅拷贝（MemberwiseClone），引用字段（Affixes/Sockets）会在克隆间共享 → 重写为深拷贝。
    /// 否则对克隆体（tooltip/拆分）改词缀会污染原件。
    /// </summary>
    public override GlobalItem Clone(Item? from, Item to)
    {
        var clone = (AffixGlobalItem)base.Clone(from, to);
        clone.Affixes = Affixes == null ? new List<AffixRoll>() : new List<AffixRoll>(Affixes);
        clone.Sockets = Sockets == null ? new List<int>() : new List<int>(Sockets);
        return clone;
    }

    // ===== 持久化 =====
    public override void SaveData(Item item, TagCompound tag)
    {
        // 只有"已判定但无词缀"才需要存 ck（有词缀本身就隐含已判定）
        if (Checked && !HasAffix) tag["ck"] = true;
        if (!HasAffix) return;
        tag["r"] = (byte)Rarity;
        tag["t"] = Tier;
        tag["ps"] = PowerScore;
        tag["bv"] = BaseValue;
        tag["lp"] = LegendaryPowerId;
        tag["st"] = SetThemeId;
        tag["sc"] = SocketCount;
        if (OpenedSockets > 0) tag["os"] = OpenedSockets;
        if (Sockets is { Count: > 0 }) tag["sk"] = Sockets;
        if (Affixes is { Count: > 0 })
        {
            var list = new List<TagCompound>();
            foreach (var r in Affixes)
                list.Add(new TagCompound { ["a"] = r.AffixId, ["v"] = r.Value });
            tag["af"] = list;
        }
    }

    public override void LoadData(Item item, TagCompound tag)
    {
        try
        {
            // M14：坏档钳制——稀有度 clamp 到 Set(4)，插槽数以列表为准（多出的槽位不再生效，
            // 防 ApplyEquip 的 foreach 放大 / UI 直达越界）
            Rarity = (LootRarity)Math.Min(tag.GetByte("r"), (byte)LootRarity.Set);
            Checked = tag.GetBool("ck") || HasAffix;
            if (!HasAffix) return;
            Tier = tag.GetInt("t");
            PowerScore = tag.GetInt("ps");
            BaseValue = tag.GetLong("bv");
            LegendaryPowerId = tag.GetInt("lp");
            SetThemeId = tag.GetInt("st");
            OpenedSockets = tag.GetInt("os");
            var sk = tag.GetList<int>("sk") ?? new List<int>();
            SocketCount = sk.Count; // M14：以列表为准
            Sockets = sk.ToList();
            Affixes = new List<AffixRoll>();
            var list = tag.GetList<TagCompound>("af");
            foreach (var t in list)
                Affixes.Add(new AffixRoll(t.GetInt("a"), t.GetFloat("v"), 0));
        }
        catch
        {
            // 防御性加载：任何缺失/损坏字段一律回退为无词缀
            ClearData();
        }
    }

    // ===== 网络同步 =====
    public override void NetSend(Item item, BinaryWriter writer)
    {
        writer.Write((byte)Rarity);
        writer.Write(Checked);
        writer.Write(Tier);
        writer.Write(PowerScore);
        writer.Write(BaseValue); // L5：BaseValue 必须同步，否则客户端副本 BaseValue=0 → 后续 ApplyValue 二次放大
        writer.Write(LegendaryPowerId);
        writer.Write(SetThemeId);
        writer.Write(SocketCount);
        writer.Write(OpenedSockets);
        int ac = Affixes?.Count ?? 0;
        writer.Write(ac);
        for (int i = 0; i < ac; i++)
        {
            writer.Write(Affixes![i].AffixId);
            writer.Write(Affixes[i].Value);
        }
        for (int i = 0; i < SocketCount; i++)
            writer.Write(i < (Sockets?.Count ?? 0) ? Sockets![i] : 0);
    }

    public override void NetReceive(Item item, BinaryReader reader)
    {
        Rarity = (LootRarity)reader.ReadByte();
        Checked = reader.ReadBoolean();
        Tier = reader.ReadInt32();
        PowerScore = reader.ReadInt32();
        BaseValue = reader.ReadInt64(); // L5
        LegendaryPowerId = reader.ReadInt32();
        SetThemeId = reader.ReadInt32();
        SocketCount = reader.ReadInt32();
        OpenedSockets = reader.ReadInt32();
        int ac = reader.ReadInt32();
        Affixes = new List<AffixRoll>();
        for (int i = 0; i < ac; i++)
            Affixes.Add(new AffixRoll(reader.ReadInt32(), reader.ReadSingle(), 0));
        Sockets = new List<int>();
        for (int i = 0; i < SocketCount; i++) Sockets.Add(reader.ReadInt32());
    }

    // ===== 数值应用（武器）=====
    public override void ModifyWeaponDamage(Item item, Player player, ref StatModifier damage)
    {
        if (!HasAffix) return;
        AffixStats.ApplyWeaponDamage(item, this, ref damage);
    }

    public override void ModifyWeaponCrit(Item item, Player player, ref float crit)
    {
        if (!HasAffix) return;
        crit += AffixStats.Sum(this, AffixStatType.CritChance);
    }

    public override void ModifyWeaponKnockback(Item item, Player player, ref StatModifier knockback)
    {
        if (!HasAffix) return;
        knockback *= 1f + AffixStats.Sum(this, AffixStatType.Knockback) / 100f;
    }

    public override float UseSpeedMultiplier(Item item, Player player)
    {
        if (!HasAffix) return 1f;
        return 1f + AffixStats.Diminish(AffixStats.Sum(this, AffixStatType.AttackSpeed)) / 100f;
    }

    public override void ModifyManaCost(Item item, Player player, ref float reduce, ref float mult)
    {
        if (!HasAffix) return;
        mult *= 1f - AffixStats.Sum(this, AffixStatType.ManaCost) / 100f;
    }

    public override void ModifyHitNPC(Item item, Player player, NPC target, ref NPC.HitModifiers modifiers)
    {
        if (!HasAffix) return;
        float cd = AffixStats.Sum(this, AffixStatType.CritDamage)
                 + player.GetModPlayer<LooteriaPlayer>().PassiveCritDamage;
        if (cd > 0) modifiers.CritDamage += cd / 100f;

        // 10 狂乱：生命 <30% 时 +25% 伤害
        if ((global::Looteria.Common.Data.LegendaryPowerId)LegendaryPowerId == global::Looteria.Common.Data.LegendaryPowerId.Frenzy
            && player.statLife > 0
            && player.statLife <= (int)(player.statLifeMax2 * 0.3f))
        {
            modifiers.FinalDamage *= 1.25f;
        }

        // 破甲（参照原版鲨鱼项链：命中削减敌防；先百分比、后固定）：
        // 有效防御 = 敌防 × (1 - 百分比/100) - 固定值（百分比乘到防御倍率先算，固定以 Flat 在倍率之后扣减）
        float pct = AffixStats.Sum(this, AffixStatType.PctArmorShred);
        float flat = AffixStats.Sum(this, AffixStatType.FlatArmorShred);
        if (pct > 0 || flat > 0)
        {
            var def = modifiers.Defense;
            if (pct > 0) def *= 1f - pct / 100f;   // 先：百分比
            if (flat > 0) def.Flat -= flat;          // 后：固定穿甲
            modifiers.Defense = def;
        }
    }

    // ===== 数值应用（护甲/饰品）=====
    public override void UpdateEquip(Item item, Player player)
    {
        if (!HasAffix) return;
        AffixStats.ApplyEquip(player, this);
    }

    // ===== 命中结算（命中回血/回蓝；Phase 4 传说之力分发）=====
    public override void OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (!HasAffix) return;
        var lp = player.GetModPlayer<LooteriaPlayer>();
        // 命中回血：固定数值（不再是伤害百分比吸血，上限默认 3，可配置）
        float lifeFlat = AffixStats.Sum(this, AffixStatType.LifeOnHit) + lp.PassiveLifeOnHit;
        int mana = (int)AffixStats.Sum(this, AffixStatType.ManaOnHit) + lp.PassiveManaOnHit;
        int heal = (int)lifeFlat;
        if (heal > 0 && damageDone > 0)
        {
            player.statLife += heal;
            player.HealEffect(heal);
        }
        if (mana > 0)
        {
            player.statMana += mana;
            player.ManaEffect(mana);
        }

        // 传说之力（近战命中类：连锁/灼烧/嗜血/处刑/顺劈）
        // M8（审计 r3）：OnHitNPC 只在"命中的客户端"触发（GlobalItem.cs:579），不存在双端重复执行；
        // 由 LegendaryPowerHandler.Strike 内部配对 SendStrikeNPC 把命中同步给服务端（客户端权威，与原版武器同通道）。
        if (LegendaryPowerId > 0)
            Effects.LegendaryPowerHandler.OnMeleeHit(player, target, this, damageDone);
    }

    // ===== 掉落掷词缀（入口：NPC 掉落 / 开箱·藏宝袋·钓鱼箱）=====
    public override void OnSpawn(Item item, IEntitySource source)
    {
        LootSystem.MaybeRoll(item, source, DropSource.Chest);
    }

    // ===== 合成掷词缀（入口：制造装备）=====
    public override void OnCreated(Item item, ItemCreationContext context)
    {
        if (context is RecipeItemCreationContext)
            LootSystem.MaybeRoll(item, null, DropSource.Craft);
    }

    // ===== 进入背包兜底（入口：普通宝箱取出/任意拾取等）=====
    public override void UpdateInventory(Item item, Player player)
    {
        LootSystem.MaybeRoll(item, null, DropSource.Chest);
    }

    // ===== 工具提示 =====
    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (!HasAffix) return;
        // 物品名按稀有度着色
        foreach (var line in tooltips)
        {
            if (line.Name == "ItemName")
            {
                // M14：坏档稀有度越界防御（Colors 数组 5 项）
                line.OverrideColor = RarityInfo.Colors[Math.Clamp((int)Rarity, 0, RarityInfo.Count - 1)];
                break;
            }
        }
        TooltipBuilder.Build(this, Mod, tooltips);
    }

    private void ClearData()
    {
        Rarity = LootRarity.None;
        Affixes?.Clear();
        SocketCount = 0;
        Sockets = new List<int>();
        LegendaryPowerId = -1;
        SetThemeId = -1;
        Tier = 0;
        PowerScore = 0;
    }
}
