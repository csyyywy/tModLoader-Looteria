using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;

namespace Looteria.Common.Roll;

/// <summary>一次赌博抽到的物品（不自动分解，交由玩家选择保留/分解）。</summary>
public class GambleResult
{
    public Item? Item;      // 抽到的物品
    public bool IsWin;      // 是否抽中当前档装备
}

/// <summary>
/// 高层生成：血岩赌博（抽卡式，分档，无保底）。
/// - 每次抽都产出一件物品：抽中当前档（10%）→ 当前档装备（稀有度见 RollWinGear）；
///   空奖（未中的 35%）→ 谢谢惠顾券（50%）或 同档"时尚小垃圾"（50%，材料/金币/原版宝石）；
///   其余 → 低档"垃圾"装备。
/// - 所有结果进 200 格掠夺容器，玩家选择保留（进背包）还是分解（换重铸之尘）。
/// 装备池 = 全部已装模组物品，按 AnchorTier 加权。
/// </summary>
public static class LootGenerator
{
    private static List<(int Type, ItemCategory Cat, int Tier)>? _candidates;

    /// <summary>L14：建池时按当前配置过滤（AffectWeapons/Armor/Accessories/Tools + 排除表），
    /// 避免中奖抽中不合资格物品时整次奖励白掷。</summary>
    private static void Build()
    {
        var cfg = LooteriaConfig.Instance;
        var list = new List<(int, ItemCategory, int)>();
        for (int i = 1; i < ItemLoader.ItemCount; i++)
        {
            var it = new Item();
            it.SetDefaults(i);
            if (it.IsAir) continue;
            if (cfg != null && !cfg.Enable) break;
            if (!ItemClassifier.IsEligible(it)) continue; // 走同一资格判定（含类别门控与排除表）
            var cat = ItemClassifier.GetCategory(it);
            if (cat == 0) continue;
            list.Add((i, cat, ItemClassifier.GetTier(it)));
        }
        _candidates = list;
    }

    public static void UnloadCache() => _candidates = null;

    /// <summary>L14：首个玩家进场后预构建候选池（避免首次赌博时才遍历全物品 SetDefaults 顿卡）。
    /// 由 LooteriaPlayer.PostUpdateEquips 首帧或 ModSystem 调用；幂等。</summary>
    public static void WarmCache()
    {
        if (_candidates == null) Build();
    }

    /// <summary>按类别取候选池（含缓存构建）。</summary>
    public static List<(int Type, ItemCategory Cat, int Tier)>? GetCandidatePool(ItemCategory cat)
    {
        if (_candidates == null) Build();
        return _candidates!.FindAll(c => (c.Cat & cat) != 0);
    }

    /// <summary>单抽。非法档位/未解锁/血岩不足/容器满 返回 null。</summary>
    public static GambleResult? Gamble(Player player, int tierIndex, bool free = false)
    {
        if (tierIndex < 0 || tierIndex >= GambleTiers.All.Length) return null;
        var tier = GambleTiers.All[tierIndex];
        if (!GambleTiers.IsUnlocked(tierIndex)) return null;
        var lp = player.GetModPlayer<LooteriaPlayer>();
        if (lp.GambleContainer.Count >= ContainerSize) return null; // 容器满
        if (!free)
        {
            if (lp.BloodShards < tier.Cost) return null;
            lp.BloodShards -= tier.Cost;
        }

        // 抽奖结果：WinChance 当前档装备；空奖（未中 35%）→ 谢谢惠顾券或同档小垃圾；其余 → 垃圾装备
        Item item;
        float winChance = LooteriaConfig.Instance?.GambleWinChance ?? 0.10f;
        bool win = Main.rand.NextFloat() < winChance;
        if (win)
        {
            var winGear = RollWinGear(player, tier.AnchorTier);
            // L6：中奖但实际产物回退成垃圾/券时，IsWin 应为 false（避免 UI 误报"抽中装备"）
            if (winGear != null) item = winGear;
            else item = RollEmptyJunk(tierIndex) ?? new Item(ModContent.ItemType<Content.Items.ThankYouTicket>());
            win = winGear != null;
        }
        else if (Main.rand.NextFloat() < 0.35f)
        {
            // 空奖：50% 谢谢惠顾券（+5 尘），50% 同档时尚小垃圾（材料/金币/原版宝石）
            item = Main.rand.NextBool()
                ? new Item(ModContent.ItemType<Content.Items.ThankYouTicket>())
                : RollEmptyJunk(tierIndex) ?? new Item(ModContent.ItemType<Content.Items.ThankYouTicket>());
        }
        else
        {
            item = RollJunk(player, tier)
                   ?? RollEmptyJunk(tierIndex)
                   ?? new Item(ModContent.ItemType<Content.Items.ThankYouTicket>());
        }

        var result = new GambleResult { Item = item, IsWin = win };
        lp.GambleContainer.Add(item); // 存入容器（不替换旧结果）
        return result;
    }

    /// <summary>抽奖存储容器最大格数（可配置；多人同步单包上限 255，代码层钳制）。</summary>
    public static int ContainerSize => Math.Clamp(LooteriaConfig.Instance?.GambleContainerSize ?? 200, 1, 255);

    /// <summary>十连抽：连做 10 次单抽，返回全部结果（血岩不足/容器满则提前结束）。</summary>
    public static List<GambleResult> GambleTen(Player player, int tierIndex)
    {
        var results = new List<GambleResult>();
        for (int i = 0; i < 10; i++)
        {
            var r = Gamble(player, tierIndex);
            if (r == null) break;
            results.Add(r);
        }
        return results;
    }

    /// <summary>
    /// 抽中：生成当前档装备。中奖内稀有度分布按配置权重（相对权重，默认 套装8/传说16/稀有30/魔法46；
    /// 每抽绝对概率 = 中奖率 × 权重/总权重，默认 4.6% 魔法 / 3.0% 稀有 / 1.6% 传说 / 0.8% 套装）。
    /// </summary>
    private static Item? RollWinGear(Player player, int anchorTier)
    {
        var pool = GetCandidatePool(ItemCategory.Weapon | ItemCategory.Armor | ItemCategory.Accessory);
        if (pool == null || pool.Count == 0) return null;

        var pick = PickWeightedNear(pool, anchorTier);
        var item = new Item(pick.Type);
        if (!ItemClassifier.IsEligible(item) || !item.TryGetGlobalItem(out AffixGlobalItem g)) return null;

        var cfg = LooteriaConfig.Instance;
        int wSet = cfg?.WinSetWeight ?? 8;
        int wLeg = cfg?.WinLegendaryWeight ?? 16;
        int wRare = cfg?.WinRareWeight ?? 30;
        int wMagic = cfg?.WinMagicWeight ?? 46;
        int total = wSet + wLeg + wRare + wMagic;

        LootRarity rarity;
        if (total <= 0)
        {
            rarity = LootRarity.Magic; // 权重全 0 的兜底
        }
        else
        {
            int r = Main.rand.Next(total);
            if (r < wSet) rarity = LootRarity.Set;
            else if (r < wSet + wLeg) rarity = LootRarity.Legendary;
            else if (r < wSet + wLeg + wRare) rarity = LootRarity.Rare;
            else rarity = LootRarity.Magic;
        }
        AffixRoller.Roll(item, g, rarity);
        return item;
    }

    /// <summary>未中：生成低档"垃圾"装备（魔法品质，可拆解）。</summary>
    private static Item? RollJunk(Player player, GambleTier tier)
    {
        int lowAnchor = Math.Max(1, tier.AnchorTier - 3);
        var pool = GetCandidatePool(ItemCategory.Weapon | ItemCategory.Armor | ItemCategory.Accessory);
        if (pool == null || pool.Count == 0) return null;

        var pick = PickWeightedNear(pool, lowAnchor);
        var item = new Item(pick.Type);
        if (!ItemClassifier.IsEligible(item) || !item.TryGetGlobalItem(out AffixGlobalItem g)) return null;
        AffixRoller.Roll(item, g, LootRarity.Magic);
        return item;
    }

    // ===== 空奖"时尚小垃圾" =====

    /// <summary>
    /// 空奖的"时尚小垃圾"表：按赌博档位（0-7）产出材料/金币/原版宝石，档位越高越值钱。
    /// 元素 = (ItemID, 最小堆叠, 最大堆叠, 权重)。
    /// 原版宝石也在其中：宝石升阶正需要原版宝石，形成"赌博→宝石→升阶"闭环。
    /// </summary>
    private static readonly (short Type, int Min, int Max, int Weight)[][] EmptyJunkTable =
    {
        // T0 基础：开局材料
        new[] {
            (ItemID.SilverCoin, 5, 40, 30),
            (ItemID.CopperOre, 4, 12, 20),
            (ItemID.IronOre, 3, 10, 18),
            (ItemID.Gel, 6, 20, 16),
            (ItemID.Wood, 10, 30, 14),
            (ItemID.Torch, 5, 15, 10),
            (ItemID.Mushroom, 3, 10, 8),
            (ItemID.Glass, 5, 15, 6),
        },
        // T1 克脑后
        new[] {
            (ItemID.SilverCoin, 10, 60, 28),
            (ItemID.GoldCoin, 1, 5, 18),
            (ItemID.SilverOre, 4, 12, 18),
            (ItemID.GoldOre, 2, 8, 16),
            (ItemID.Amethyst, 1, 3, 12),
            (ItemID.Topaz, 1, 3, 10),
            (ItemID.IronOre, 5, 14, 14),
            (ItemID.Silk, 3, 10, 8),
        },
        // T2 骷髅王（肉山前）
        new[] {
            (ItemID.GoldCoin, 2, 10, 26),
            (ItemID.GoldOre, 4, 12, 18),
            (ItemID.Obsidian, 6, 16, 14),
            (ItemID.Hellstone, 3, 10, 14),
            (ItemID.Sapphire, 1, 3, 12),
            (ItemID.Emerald, 1, 3, 10),
            (ItemID.Bone, 10, 30, 10),
            (ItemID.Hook, 2, 6, 6),
        },
        // T3 肉山后
        new[] {
            (ItemID.GoldCoin, 3, 15, 26),
            (ItemID.CrystalShard, 4, 12, 16),
            (ItemID.SoulofLight, 2, 6, 14),
            (ItemID.SoulofNight, 2, 6, 14),
            (ItemID.Ruby, 1, 3, 12),
            (ItemID.Topaz, 1, 3, 10),
            (ItemID.PixieDust, 4, 12, 10),
            (ItemID.GreaterHealingPotion, 2, 5, 6),
        },
        // T4 机械三王
        new[] {
            (ItemID.PlatinumCoin, 1, 8, 24),
            (ItemID.SoulofMight, 2, 6, 16),
            (ItemID.SoulofSight, 2, 6, 16),
            (ItemID.SoulofFright, 2, 6, 16),
            (ItemID.HallowedBar, 2, 6, 14),
            (ItemID.Ruby, 1, 3, 10),
            (ItemID.GreaterHealingPotion, 2, 6, 8),
            (ItemID.GoldCoin, 5, 20, 10),
        },
        // T5 世纪之花
        new[] {
            (ItemID.PlatinumCoin, 2, 12, 24),
            (ItemID.ChlorophyteOre, 4, 12, 16),
            (ItemID.ChlorophyteBar, 2, 6, 14),
            (ItemID.Ectoplasm, 3, 10, 14),
            (ItemID.Diamond, 1, 3, 12),
            (ItemID.Emerald, 1, 3, 10),
            (ItemID.SuperHealingPotion, 2, 5, 8),
        },
        // T6 石巨人
        new[] {
            (ItemID.PlatinumCoin, 3, 18, 24),
            (ItemID.Ectoplasm, 4, 12, 16),
            (ItemID.BeetleHusk, 2, 6, 14),
            (ItemID.SpectreBar, 2, 5, 12),
            (ItemID.Diamond, 1, 4, 14),
            (ItemID.ChlorophyteBar, 3, 8, 10),
            (ItemID.SuperHealingPotion, 2, 6, 8),
        },
        // T7 月亮领主
        new[] {
            (ItemID.PlatinumCoin, 5, 30, 24),
            (ItemID.FragmentSolar, 2, 8, 12),
            (ItemID.FragmentVortex, 2, 8, 12),
            (ItemID.FragmentNebula, 2, 8, 12),
            (ItemID.FragmentStardust, 2, 8, 12),
            (ItemID.LunarOre, 1, 4, 12),
            (ItemID.Diamond, 1, 4, 12),
            (ItemID.SuperHealingPotion, 3, 8, 8),
        },
    };

    /// <summary>空奖：抽一件同档"时尚小垃圾"（材料/金币/原版宝石）。</summary>
    private static Item? RollEmptyJunk(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= EmptyJunkTable.Length) tierIndex = 0;
        var pool = EmptyJunkTable[tierIndex];
        if (pool == null || pool.Length == 0) return null; // L7：空表防御
        int total = 0;
        foreach (var e in pool) total += e.Weight;
        if (total <= 0) return null; // L7：权重和为 0 防御（避免 Main.rand.Next(0) 异常）
        int r = Main.rand.Next(total);
        var chosen = pool[0];
        foreach (var e in pool)
        {
            r -= e.Weight;
            if (r < 0) { chosen = e; break; }
        }
        var item = new Item(chosen.Type);
        item.stack = Main.rand.Next(chosen.Min, chosen.Max + 1);
        return item;
    }

    /// <summary>在池中按"接近目标档位"加权抽取。</summary>
    private static (int Type, ItemCategory Cat, int Tier) PickWeightedNear(
        List<(int Type, ItemCategory Cat, int Tier)> pool, int anchor)
    {
        double total = 0;
        var weights = new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            weights[i] = 1.0 / (1.0 + Math.Abs(pool[i].Tier - anchor) * 3.0);
            total += weights[i];
        }
        double r = Main.rand.NextDouble() * total;
        var chosen = pool[0];
        for (int i = 0; i < pool.Count; i++)
        {
            r -= weights[i];
            if (r <= 0) { chosen = pool[i]; break; }
        }
        return chosen;
    }
}
