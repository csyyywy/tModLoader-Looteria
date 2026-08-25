using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Roll;

namespace Looteria.Common.Systems;

/// <summary>
/// 词缀生成入口：覆盖"所有获得装备的途径"——
///  掉落（EntitySource_Loot）/ 开箱·藏宝袋·钓鱼箱（EntitySource_ItemOpen）/ 合成（OnCreated·配方）/
///  以及进入背包的兜底（UpdateInventory，如普通宝箱取出）。
/// 幂等：AffixGlobalItem.Checked 标记一次，绝不重复掷。
/// </summary>
public class LootSystem : ModSystem
{
    /// <summary>
    /// 判定并掷词缀。source 命中特定来源时用对应掉落表，否则用 fallback 表。
    /// </summary>
    public static void MaybeRoll(Item item, IEntitySource? source, DropSource fallback)
    {
        if (item == null || item.IsAir) return;
        if (!item.TryGetGlobalItem(out AffixGlobalItem g)) return;

        // H1 自愈：会话内手动掷点产物（赌博/秘境/命令，已由 AffixRoller.Roll 置 Checked=true）在此早退，
        // 不再被兜底重掷；读档物品由 LoadData 的 Checked = ck || HasAffix 保证有标记。
        if (g.Checked) return;

        // M12：多人掷点分工——每件物品恰好在其"诞生端"掷一次：
        //   NPC 掉落(EntitySource_Loot) 由服务端结算，词缀随物品自动下行（原版 SyncItem 携带 ItemIO 数据）；
        //   开箱取出/合成/背包兜底的物品诞生于交互客户端，由客户端掷完随物品槽上行；
        //   对端实例因 Checked=true 天然幂等早退，不会重复掷。
        if (Main.netMode == NetmodeID.MultiplayerClient && source is EntitySource_Loot) return;
        if (Main.netMode == NetmodeID.Server && source is not EntitySource_Loot) return;

        g.Checked = true; // 先标记：即使本次掷空（None）也不再重掷

        DropSource src = fallback;
        if (source is EntitySource_Loot loot)
            src = loot.Entity is NPC npc && npc.boss ? DropSource.Boss : DropSource.Normal;
        else if (source is EntitySource_ItemOpen)
            src = DropSource.Chest;

        var cfg = LooteriaConfig.Instance;
        var rarity = DropTable.RollRarity(src, cfg?.DropRateMult ?? 1f, RiftSystem.CurrentLevel);
        if (rarity != LootRarity.None)
            AffixRoller.Roll(item, g, rarity);
    }
}
