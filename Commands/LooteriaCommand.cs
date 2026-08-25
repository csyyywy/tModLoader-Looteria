using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria.Commands;

/// <summary>Looteria 命令（多人安全）：
/// 只读子命令（info/tier/riftinfo）保留 Chat 供任意玩家查询；写操作子命令（roll/clear/salvage/shards/dust）
/// 走 /loot 的 Write 变体（仅服务器控制台/房主；单人聊天同样可用）。</summary>
public class LooteriaCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "loot";
    public override string Usage => "/loot info | tier | riftinfo | enemy <名称/ID> | roll <none|magic|rare|legendary|set|0-4> | clear | salvage | shards <n> | dust <n>";
    public override string Description => "Looteria 查询命令（写操作请用 /lootadmin）";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        var player = caller.Player;
        if (args.Length == 0) { caller.Reply(Usage); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "roll":
            case "clear":
            case "salvage":
            case "shards":
            case "血岩":
            case "dust":
            case "重铸之尘":
                // H3：写操作不再放 Chat 通道（多人任意玩家可作弊）；
                // 单机/主机在聊天用 /lootadmin 前缀；服务器请在控制台使用。
                caller.Reply("该写操作已移至 /lootadmin（服务器控制台或房主可用）。");
                break;
            case "info":
                Info(caller, player.HeldItem);
                break;
            case "tier":
                caller.Reply($"tier = {ItemClassifier.GetTier(player.HeldItem)}, cat = {ItemClassifier.GetCategory(player.HeldItem)}, eligible = {ItemClassifier.IsEligible(player.HeldItem)}");
                break;
            case "enemy":
                EnemyPower(caller, args);
                break;
            case "riftinfo":
            {
                string dump = global::Looteria.Common.Systems.RiftSystem.DebugDump();
                // 聊天只能显示约 10 行 → 完整信息写入日志（client.log 搜 "RiftInfo:"），聊天只给摘要
                global::Looteria.Looteria.Instance?.Logger.Info("RiftInfo:\n" + dump);
                var headLines = dump.Split('\n');
                string head = headLines.Length > 4 ? string.Join("\n", headLines[0..4]) : dump;
                caller.Reply($"riftinfo 完整内容已写入日志（Logs.txt / client.log 搜 RiftInfo）。摘要：\n{head}");
                break;
            }
            default:
                caller.Reply(Usage);
                break;
        }
    }

    /// <summary>只读查询：/loot enemy &lt;名称/ID&gt; —— 该敌人缓存强度分（秘境池口径）。</summary>
    private static void EnemyPower(CommandCaller caller, string[] args)
    {
        if (args.Length < 2) { caller.Reply("usage: /loot enemy <名称或ID>"); return; }
        int type = 0;
        if (int.TryParse(args[1], out int id))
        {
            type = id;
        }
        else
        {
            // 按显示名模糊查找（原版 + 模组 NPC）
            string target = string.Join(" ", args.Skip(1)).ToLowerInvariant();
            for (int i = 1; i < NPCLoader.NPCCount; i++)
            {
                try
                {
                    string name = Terraria.Lang.GetNPCNameValue(i).ToLowerInvariant();
                    if (name.Contains(target)) { type = i; break; }
                }
                catch { }
            }
            if (type == 0)
            {
                caller.Reply($"未找到 NPC：{string.Join(" ", args.Skip(1))}（可用 ID 或部分名称）");
                return;
            }
        }
        var r = global::Looteria.Common.Systems.RiftSystem.QueryEnemyPower(type);
        if (r == null)
        {
            caller.Reply($"type {type} 不在秘境缓存（被过滤或越界）：{Terraria.Lang.GetNPCNameValue(type)}");
            return;
        }
        caller.Reply($"type {type}: {Terraria.Lang.GetNPCNameValue(type)} life={r.Value.Life} dmg={r.Value.Damage} def={r.Value.Defense} boss={r.Value.Boss} power={r.Value.Power:0.##}");
    }

    private static void Info(CommandCaller caller, Item item)
    {
        if (item == null || item.IsAir) { caller.Reply("no item"); return; }
        if (!item.TryGetGlobalItem(out AffixGlobalItem g)) { caller.Reply("no affix instance (ineligible)"); return; }
        string affixes = "";
        if (g.Affixes != null)
        {
            foreach (var r in g.Affixes)
            {
                var d = AffixDatabase.GetById(r.AffixId);
                affixes += d == null ? $"[?{r.AffixId}+{r.Value:0.#}] " : $"[{d.Key}+{r.Value:0.#}] "; // L1
            }
        }
        caller.Reply($"rarity={g.Rarity} tier={g.Tier} power={g.PowerScore} sockets={g.SocketCount} leg={g.LegendaryPowerId} set={g.SetThemeId} | {affixes}");
    }
}

/// <summary>写操作命令（roll/clear/salvage/shards/dust）。H3+R4：
/// 类型 = Chat | Console（tML 自带命令标准写法）——单机聊天与服务器控制台都可用；
/// 但多人聊天（专用服/主机里任何玩家）一律拒绝，写操作只对控制台/单机开放。</summary>
public class LooteriaAdminCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat | CommandType.Console;
    public override string Command => "lootadmin";
    public override string Usage => "/lootadmin roll <none|magic|rare|legendary|set|0-4> | clear | salvage | shards <n> | dust <n> | gem [类型 等级] | spawn <NPC名/ID> [数量] | debug pool|eaf|power";
    public override string Description => "Looteria 写操作命令（单机聊天 / 服务器控制台）";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        // R4：控制台调用时 caller.Player 为 null（ModCommand.cs:119-122）——先判空防 NRE
        var player = caller.Player;
        if (player == null)
        {
            caller.Reply("控制台无玩家上下文：请用聊天执行（单机），或在聊天里对目标玩家使用。");
            caller.Reply(Usage);
            return;
        }
        // 多人聊天拒绝（专用服/主机里任何玩家都能敲 → 作弊面）
        if (Main.netMode != NetmodeID.SinglePlayer)
        {
            caller.Reply("写操作命令仅服务器控制台或单人可用（多人聊天不可用，防作弊）。");
            return;
        }
        if (args.Length == 0) { caller.Reply(Usage); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "roll":
                Roll(caller, player, args);
                break;
            case "clear":
                if (TryAffix(player.HeldItem, out var g)) AffixRoller.Clear(player.HeldItem, g);
                caller.Reply("cleared");
                break;
            case "salvage":
                Salvage(caller, player);
                break;
            case "shards":
            case "血岩":
                SetShards(caller, player, args);
                break;
            case "dust":
            case "重铸之尘":
                SetDust(caller, player, args);
                break;
            case "gem":
                Gem(caller, player, args);
                break;
            case "spawn":
                Spawn(caller, player, args);
                break;
            case "debug":
                Debug(caller, player, args);
                break;
            default:
                caller.Reply(Usage);
                break;
        }
    }

    private static void SetShards(CommandCaller caller, Player player, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int s) || s < 0)
        {
            caller.Reply("usage: /lootadmin shards <n>（或 /lootadmin 血岩 <n>）");
            return;
        }
        var lp = player.GetModPlayer<LooteriaPlayer>();
        lp.BloodShards = s;
        SyncCurrency(player, lp);
        caller.Reply($"BloodShards = {s}");
    }

    /// <summary>调试：/lootadmin gem [类型 等级] —— 给手持装备插一颗宝石。
    /// 类型 ruby/sapphire/emerald/amethyst/topaz/diamond 或 0-5；等级 0-3（缺省按进度随机）。</summary>
    private static void Gem(CommandCaller caller, Player player, string[] args)
    {
        var held = player.HeldItem;
        if (held == null || held.IsAir) { caller.Reply("no item in hand"); return; }
        if (!held.TryGetGlobalItem(out AffixGlobalItem g) || g.Rarity == LootRarity.None)
        {
            caller.Reply("held item has no affix instance (roll a rarity first)");
            return;
        }

        int gemId = 0;
        if (args.Length >= 3)
        {
            GemType type;
            switch (args[1].ToLowerInvariant())
            {
                case "ruby": case "红": type = GemType.Ruby; break;
                case "sapphire": case "蓝": type = GemType.Sapphire; break;
                case "emerald": case "绿": type = GemType.Emerald; break;
                case "amethyst": case "紫": type = GemType.Amethyst; break;
                case "topaz": case "黄": type = GemType.Topaz; break;
                case "diamond": case "钻": type = GemType.Diamond; break;
                default:
                    if (int.TryParse(args[1], out int ti) && ti >= 0 && ti < GemDatabase.Types)
                        type = (GemType)ti;
                    else { caller.Reply("bad gem type: ruby/sapphire/emerald/amethyst/topaz/diamond or 0-5"); return; }
                    break;
            }
            if (args.Length >= 4 && int.TryParse(args[3], out int lv) && lv >= 0 && lv < GemDatabase.Levels)
                gemId = GemDatabase.Id(type, lv);
            else
                gemId = GemDatabase.Id(type, Main.rand.Next(GemDatabase.Levels));
        }
        else
        {
            gemId = GemDatabase.RollGemIdForProgression(); // 无参：按进度随机
        }

        if (g.SocketCount <= 0 || g.Sockets == null)
        {
            caller.Reply("no sockets (roll legendary/set for sockets, or open slots)");
            return;
        }
        int slot = g.Sockets.IndexOf(0);
        if (slot < 0) { caller.Reply("no free socket"); return; }
        g.Sockets[slot] = gemId; // 插槽值 = gemId + upgrade×1000（0 强化）
        g.PowerScore = AffixRoller.PowerScore(g);
        caller.Reply($"inserted gem {gemId} ({GemDatabase.GetType(gemId)} L{GemDatabase.GetLevel(gemId)}) into {held.Name} slot {slot}");
    }

    /// <summary>调试：/lootadmin spawn &lt;NPC名/ID&gt; [数量] —— 在玩家位置刷怪（测敌人词缀/秘境怪）。</summary>
    private static void Spawn(CommandCaller caller, Player player, string[] args)
    {
        if (args.Length < 2) { caller.Reply("usage: /lootadmin spawn <NPC名或ID> [数量]"); return; }
        int type = 0;
        if (int.TryParse(args[1], out int id))
        {
            type = id;
        }
        else
        {
            string target = string.Join(" ", args.Skip(1)).ToLowerInvariant();
            for (int i = 1; i < NPCLoader.NPCCount; i++)
            {
                try
                {
                    string name = Terraria.Lang.GetNPCNameValue(i).ToLowerInvariant();
                    if (name.Contains(target)) { type = i; break; }
                }
                catch { }
            }
            if (type == 0) { caller.Reply($"未找到 NPC：{string.Join(" ", args.Skip(1))}"); return; }
        }
        if (type <= 0 || type >= NPCLoader.NPCCount)
        {
            caller.Reply($"bad NPC type: {type}");
            return;
        }
        int count = args.Length >= 3 && int.TryParse(args[2], out int c) ? Math.Clamp(c, 1, 50) : 1;
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            int n = NPC.NewNPC(player.GetSource_FromThis(), (int)player.Center.X + Main.rand.Next(-20, 21),
                (int)player.Center.Y - 20, type);
            if (n >= 0 && n < Main.npc.Length) spawned++;
        }
        caller.Reply($"spawned {spawned}/{count} × {Terraria.Lang.GetNPCNameValue(type)}");
    }

    /// <summary>调试：/lootadmin debug pool [稀有度] | eaf | power。</summary>
    private static void Debug(CommandCaller caller, Player player, string[] args)
    {
        if (args.Length < 2) { caller.Reply("usage: /lootadmin debug pool [rarity] | eaf | power"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "pool":
            {
                var held = player.HeldItem;
                if (held == null || held.IsAir) { caller.Reply("no item in hand"); return; }
                var cat = ItemClassifier.GetCategory(held);
                var pool = AffixDatabase.GetPool(cat);
                if (pool.Count == 0) { caller.Reply("empty pool for this item"); return; }
                // 完整写日志，聊天给数量 + 前几条
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"AffixPool for {held.Name} (cat={cat}, {pool.Count} entries):");
                foreach (var d in pool)
                    sb.AppendLine($"  [{d.Id}] {d.Key} stat={d.Stat} base={d.Base} step={d.Step} max={d.Max} pct={d.IsPercent} w={d.Weight}");
                global::Looteria.Looteria.Instance?.Logger.Info("DebugPool:\n" + sb);
                caller.Reply($"pool={pool.Count} 条已写入日志（搜 DebugPool）。前 5 条：");
                for (int i = 0; i < Math.Min(5, pool.Count); i++)
                    caller.Reply($"  [{pool[i].Id}] {pool[i].Key} stat={pool[i].Stat} max={pool[i].Max}");
                break;
            }
            case "eaf":
            {
                // 敌人词缀表（调试：看池子/枚举）
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EnemyAffixDatabase dump:");
                sb.AppendLine("CommonPool:");
                foreach (var a in EnemyAffixDatabase.CommonPool)
                    sb.AppendLine($"  {(int)a} {EnemyAffixDatabase.Key(a)}");
                sb.AppendLine("ChampionPool:");
                foreach (var a in EnemyAffixDatabase.ChampionPool)
                    sb.AppendLine($"  {(int)a} {EnemyAffixDatabase.Key(a)}");
                sb.AppendLine("BossExclusivePool:");
                foreach (var a in EnemyAffixDatabase.BossExclusivePool)
                    sb.AppendLine($"  {(int)a} {EnemyAffixDatabase.Key(a)}");
                global::Looteria.Looteria.Instance?.Logger.Info("DebugEAF:\n" + sb);
                caller.Reply($"敌人词缀表已写入日志（搜 DebugEAF）：common={EnemyAffixDatabase.CommonPool.Length} champ={EnemyAffixDatabase.ChampionPool.Length} boss={EnemyAffixDatabase.BossExclusivePool.Length}");
                break;
            }
            case "power":
            {
                var held = player.HeldItem;
                if (held == null || held.IsAir) { caller.Reply("no item in hand"); return; }
                if (!held.TryGetGlobalItem(out AffixGlobalItem g))
                {
                    caller.Reply("no affix instance");
                    return;
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"PowerBreakdown for {held.Name}: rarity={g.Rarity} tier={g.Tier} power={g.PowerScore}");
                if (g.Affixes != null)
                    foreach (var r in g.Affixes)
                    {
                        var d = AffixDatabase.GetById(r.AffixId);
                        if (d != null)
                            sb.AppendLine($"  [{d.Key}] value={r.Value:0.#} × weight={d.PowerWeight} = {r.Value * d.PowerWeight:0.#}");
                    }
                sb.AppendLine($"  sockets={g.SocketCount} ×15 = {15 * g.SocketCount} | legendary={g.LegendaryPowerId > 0} +30 | set={g.SetThemeId >= 0} +20 | tierBase=10×{g.Tier}={10 * g.Tier}");
                global::Looteria.Looteria.Instance?.Logger.Info("DebugPower:\n" + sb);
                caller.Reply($"力量构成已写入日志（搜 DebugPower）：power={g.PowerScore}, affixes={g.Affixes?.Count ?? 0}, sockets={g.SocketCount}");
                break;
            }
            default:
                caller.Reply("usage: /lootadmin debug pool [rarity] | eaf | power");
                break;
        }
    }

    private static void SetDust(CommandCaller caller, Player player, string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int d) || d < 0)
        {
            caller.Reply("usage: /lootadmin dust <n>（或 /lootadmin 重铸之尘 <n>）");
            return;
        }
        var lp = player.GetModPlayer<LooteriaPlayer>();
        lp.Dust = d;
        SyncCurrency(player, lp);
        caller.Reply($"ReforgeDust = {d}");
    }

    /// <summary>
    /// 货币设置命令在多人/主机模式下于服务端执行，改动的是服务端玩家对象，
    /// 不会自动同步到客户端（UI 读 Main.LocalPlayer），所以表现为"命令无效"。
    /// 这里显式走 Looteria.SendCurrencyTo 定向推送（H5 修复：目标下标在包体、客户端消费）。
    /// </summary>
    private static void SyncCurrency(Player player, LooteriaPlayer lp)
    {
        global::Looteria.Looteria.SendCurrencyTo(player.whoAmI);
    }

    private static void Roll(CommandCaller caller, Player player, string[] args)
    {
        if (args.Length < 2) { caller.Reply("need rarity"); return; }
        var rarity = ParseRarity(args[1]);
        if (rarity == null) { caller.Reply("bad rarity"); return; }
        if (!TryAffix(player.HeldItem, out var g)) { caller.Reply("item ineligible"); return; }
        AffixRoller.Roll(player.HeldItem, g, rarity.Value);
        caller.Reply($"rolled {rarity.Value} on {player.HeldItem.Name}");
    }

    private static void Salvage(CommandCaller caller, Player player)
    {
        var held = player.HeldItem;
        if (held == null || held.IsAir) { caller.Reply("no item"); return; }
        if (!held.TryGetGlobalItem(out AffixGlobalItem g) || !g.HasAffix)
        {
            caller.Reply("item has no affixes");
            return;
        }
        // L15：SalvageDivisor 默认值统一为配置默认 2（与 UI 侧一致）
        int divisor = Math.Max(1, global::Looteria.Common.Configs.LooteriaConfig.Instance?.SalvageDivisor ?? 2);
        int dust = Math.Max(1, g.PowerScore / divisor);
        player.GetModPlayer<LooteriaPlayer>().AddDust(dust);
        player.HeldItem.TurnToAir();
        caller.Reply($"salvaged for {dust} reforge dust");
    }

    private static bool TryAffix(Item item, out AffixGlobalItem g)
    {
        g = null!;
        return item != null && !item.IsAir && item.TryGetGlobalItem(out g);
    }

    private static LootRarity? ParseRarity(string s) => s.ToLowerInvariant() switch
    {
        "none" => LootRarity.None,
        "magic" => LootRarity.Magic,
        "rare" => LootRarity.Rare,
        "legendary" or "leg" => LootRarity.Legendary,
        "set" => LootRarity.Set,
        _ => int.TryParse(s, out int n) && n >= 0 && n <= 4 ? (LootRarity)n : null
    };
}
