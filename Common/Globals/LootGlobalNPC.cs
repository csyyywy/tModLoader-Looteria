using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Players;
using global::Looteria.Common.Systems;

namespace Looteria.Common.Globals;

/// <summary>
/// 掉落经济：精英怪 + 击杀后血岩/重铸之尘自动入账（服务端判定）。
/// 货币走 ModPlayer 账户（零新增物品）；CombatText 提示。
/// </summary>
public class LootGlobalNPC : GlobalNPC
{
    /// <summary>是否精英怪（InstancePerEntity 每 NPC 一个实例）。
    /// ⚠️ 已迁移到 EnemyAffixGlobalNPC（词缀系统判定）；此字段保留仅为旧引用兼容，不再被写入。</summary>
    public bool IsElite;

    /// <summary>是否秘境入侵刷出的敌人（只计秘境进度）。</summary>
    public bool IsRiftSpawned;

    /// <summary>是否秘境关底 Boss（即使模组 Boss 没标 npc.boss，也保证掉落宝石）。</summary>
    public bool IsRiftBoss;

    public override bool InstancePerEntity => true;

    public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        => entity != null && !entity.friendly && entity.damage > 0;

    /// <summary>秘境入侵激活时停掉自然刷怪（秘境怪由 RiftSystem 直接 NewNPC，不受影响）——用户要求。</summary>
    public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
    {
        if (RiftSystem.RiftActive)
            spawnRate = 0;
    }

    /// <summary>生成时：秘境激活时按层缩放敌人（Boss 也缩放）。
    /// 精英/词缀逻辑已迁移到 EnemyAffixGlobalNPC（刷出后掷取，属性/倍率记录/名字染色）。</summary>
    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (RiftSystem.RiftActive)
        {
            float lifeMult = 1f + 0.15f * RiftSystem.CurrentLevel;
            float dmgMult = 1f + 0.10f * RiftSystem.CurrentLevel;
            npc.lifeMax = (int)(npc.lifeMax * lifeMult);
            npc.life = npc.lifeMax;
            npc.damage = (int)(npc.damage * dmgMult);
        }
    }

    /// <summary>该 NPC 是否为精英（词缀系统判定：Champion 档或有词缀的非 Boss）。</summary>
    public bool IsEliteNPC(NPC npc)
    {
        if (npc.boss) return false;
        if (!npc.TryGetGlobalNPC(out EnemyAffixGlobalNPC g)) return false;
        return g.Rarity == EnemyAffixRarity.Champion || g.HasAffixes;
    }

    public override void OnKill(NPC npc)
    {
        // 服务端权威（单机=本机）。客户端/远程不重复结算。
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        // 秘境击杀计数（Phase 4 接入；当前 RiftSystem 仅占位）
        if (RiftSystem.RiftActive) RiftSystem.OnEnemyKilled(npc);

        int killerIndex = npc.lastInteraction;
        if (killerIndex < 0 || killerIndex >= Main.maxPlayers) return;
        var player = Main.player[killerIndex];
        if (player == null || !player.active) return;

        bool isBossKill = npc.boss || IsRiftBoss;
        bool elite = !isBossKill && IsEliteNPC(npc);

        // 阶段倍率：血岩/尘掉落随游戏阶段（0~7）从初始 1x 涨到上限（默认 10x）
        int stage = Progression.CurrentStage();
        float stageMult = Progression.StageDropMult(stage);

        // 血岩基础：普通 1~3 / 精英 8~15 / Boss 150~300（Boss 配得上身份）
        int shards = isBossKill ? 150 + Main.rand.Next(151)
                    : elite ? 8 + Main.rand.Next(8)
                    : 1 + Main.rand.Next(3);
        // 重铸之尘基础：Boss 30~60 / 精英 3~7 / 普通 0
        int dust = isBossKill ? 30 + Main.rand.Next(31)
                : elite ? 3 + Main.rand.Next(5) : 0;

        // 精英掉落加成：× (1 + 词缀数 × EliteDropBonusPerAffix)（词缀越多越值钱）
        float eliteBonus = 1f;
        if (elite && npc.TryGetGlobalNPC(out EnemyAffixGlobalNPC eag) && eag.HasAffixes)
            eliteBonus = 1f + eag.Affixes.Count * (EnemyAffixConfig.Instance?.EliteDropBonusPerAffix ?? 0.15f);

        // 阶段倍率 → 难度倍率（专家 1.5 / 大师 2）→ 各自可配置的货币倍率（血岩 / 重铸之尘分开）
        var cfg = LooteriaConfig.Instance;
        float diff = Main.masterMode ? 2f : Main.expertMode ? 1.5f : 1f;
        shards = (int)(shards * stageMult * diff * eliteBonus * (cfg?.BloodShardRate ?? 1f));
        dust = (int)(dust * stageMult * diff * eliteBonus * (cfg?.DustRate ?? 1f));

        var lp = player.GetModPlayer<LooteriaPlayer>();
        if (shards > 0) lp.BloodShards += shards;
        if (dust > 0) lp.Dust += dust;

        // 宝石：仅 Boss 掉落（原版/模组 Boss 用 npc.boss；秘境关底 Boss 用 IsRiftBoss，覆盖未标 boss 的模组 Boss）
        if (isBossKill)
        {
            int gemId = GemDatabase.RollGemIdForProgression();
            int gemType = Content.Items.Gems.GemItemHelper.TypeForGem(gemId);
            if (gemType > 0)
                Item.NewItem(npc.GetSource_Loot(), npc.getRect(), gemType, 1);
        }

        // H5：服务端记账后把权威货币定向回推击杀者客户端（修复"记账不告知"）；
        // L10：击杀飘字改由 KillNotify 定向包让击杀者客户端本地渲染（服务端 myPlayer 判断永不成立）。
        global::Looteria.Looteria.SendCurrencyTo(killerIndex);
        global::Looteria.Looteria.SendKillNotify(killerIndex, shards, dust);
    }
}
