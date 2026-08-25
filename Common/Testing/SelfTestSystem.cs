using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria.Common.Testing;

/// <summary>
/// 无头服务器自检桥（doc: skills .../resources/13-headless-testing.md）。
/// 仅当环境变量 Looteria_TESTDIR 存在时启用（headless-test.ps1 设置）。
/// 指令文件 in/commands.txt 每行一个用例名；结果写 out/results.txt（[TEST] 行）。
/// 全部用例走服务端语义：不依赖 Main.LocalPlayer / 渲染。
/// </summary>
public class SelfTestSystem : ModSystem
{
    private static readonly Dictionary<string, Func<string>> Tests = new();
    private string? _root;
    private int _consumed;
    private int _tick;

    public static void RegisterTest(string name, Func<string> body) => Tests[name] = body;

    /// <summary>无条件启用：Load 里再按环境变量决定是否注册用例/启动桥。
    /// （用 IsLoadingEnabled 返回 false 会导致整个类不加载，PostUpdateWorld 永远不跑。）</summary>
    public override void Load()
    {
        _root = Environment.GetEnvironmentVariable("Looteria_TESTDIR");
        if (_root == null) return;

        RegisterTest("ping", () => "pong");

        // 货币入账（ModPlayer 直改，服务端权威面）
        RegisterTest("currency", () =>
        {
            var lp = Main.player[0].GetModPlayer<LooteriaPlayer>();
            int sh = lp.BloodShards, du = lp.Dust;
            lp.AddBloodShards(10);
            lp.AddDust(20);
            if (lp.BloodShards != sh + 10 || lp.Dust != du + 20)
                throw new Exception($"currency mismatch: shards {sh}->{lp.BloodShards}, dust {du}->{lp.Dust}");
            return $"shards+10 dust+20 ok";
        });

        // 掷点：套装（主题 + 3~5 词缀 + 1~2 槽）
        RegisterTest("rollset", () =>
        {
            var item = NewEligible();
            if (!item.TryGetGlobalItem(out AffixGlobalItem g)) throw new Exception("no global item");
            AffixRoller.Roll(item, g, LootRarity.Set);
            if (g.SetThemeId < 0) throw new Exception("set theme not rolled");
            if (g.Affixes is not { Count: >= 3 and <= 5 }) throw new Exception($"affix count {g.Affixes?.Count}");
            if (g.SocketCount is < 1 or > 2) throw new Exception($"socket count {g.SocketCount}");
            if (g.PowerScore <= 0) throw new Exception("power <= 0");
            return $"theme={g.SetThemeId} affixes={g.Affixes.Count} sockets={g.SocketCount} power={g.PowerScore}";
        });

        // 升档保留（传说 → 套装）：词缀/插槽/传说之力全保留，只补主题；修复"5→4、2→1"
        RegisterTest("upgrade-preserve", () =>
        {
            var cfg = LooteriaConfig.Instance;
            if (cfg == null) throw new Exception("no config");
            float oldChance = cfg.UpgradeRarityChance;
            var item = NewEligible();
            if (!item.TryGetGlobalItem(out AffixGlobalItem g)) throw new Exception("no global item");
            AffixRoller.Roll(item, g, LootRarity.Legendary);
            if (g.Rarity != LootRarity.Legendary) throw new Exception("roll failed");
            int affixes = g.Affixes!.Count;
            int sockets = g.SocketCount;
            int leg = g.LegendaryPowerId;
            if (leg <= 0) throw new Exception("legendary power missing");
            try
            {
                cfg.UpgradeRarityChance = 1f; // 确定性成功
                bool ok = AffixRoller.UpgradeRarity(item, g);
                if (!ok) throw new Exception("upgrade failed at 100%");
                if (g.Rarity != LootRarity.Set) throw new Exception($"rarity {g.Rarity} != Set");
                if (g.SetThemeId < 0) throw new Exception("set theme not granted");
                if (g.Affixes!.Count != affixes) throw new Exception($"affixes {g.Affixes.Count} != {affixes} (lost affixes on upgrade)");
                if (g.SocketCount != sockets) throw new Exception($"sockets {g.SocketCount} != {sockets} (lost sockets on upgrade)");
                if (g.LegendaryPowerId != leg) throw new Exception("legendary power lost");
            }
            finally
            {
                cfg.UpgradeRarityChance = oldChance;
            }
            return $"affixes={affixes} sockets={sockets} leg={leg} -> Set theme={g.SetThemeId} preserved";
        });

        // 套装进度统计口径（SetBonusHandler 扫描真实槽，时装位不计）——服务端只验证口径不报错
        RegisterTest("set-scan", () =>
        {
            // 无玩家实体时扫描应为 0 且不抛异常（空 armor 防御）
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < Main.player[0].armor.Length; i++)
            {
                if (Main.player[0].armor[i].TryGetGlobalItem(out AffixGlobalItem ag) && ag.SetThemeId >= 0)
                    counts[ag.SetThemeId] = counts.TryGetValue(ag.SetThemeId, out int c) ? c + 1 : 1;
            }
            return $"slots={Main.player[0].armor.Length} sets={counts.Count} (no crash)";
        });

        // 敌人词缀：刷出后掷取 → 属性应用 → 倍率记录 → SendExtraAI 往返一致 → RiftSystem 防线新分母
        RegisterTest("enemy-affix", () =>
        {
            var cfg = EnemyAffixConfig.Instance;
            if (cfg == null) throw new Exception("no enemy affix config");
            bool oldEnable = cfg.Enable;
            float oldCommon = cfg.CommonAffixChance;
            float oldElite = cfg.EliteChance;
            int oldBossMin = cfg.BossAffixCountMin, oldBossMax = cfg.BossAffixCountMax;
            int oldBossExMin = cfg.BossExclusiveCountMin, oldBossExMax = cfg.BossExclusiveCountMax;
            try
            {
                cfg.Enable = true;
                cfg.CommonAffixChance = 0f;   // 只测精英/Boss 路径（确定性）
                cfg.EliteChance = 1f;         // 必然精英
                cfg.BossAffixCountMin = 1; cfg.BossAffixCountMax = 1;
                cfg.BossExclusiveCountMin = 1; cfg.BossExclusiveCountMax = 1;

                // 用非 Boss 小怪（蓝史莱姆）：必然精英 → 词缀非空
                var npc = new NPC();
                npc.SetDefaults(NPCID.BlueSlime);
                if (!npc.TryGetGlobalNPC(out EnemyAffixGlobalNPC g))
                    throw new Exception("no enemy affix global on npc");

                // 手工触发掷取（OnSpawn 走 Main.netMode 服务端守卫；无头服务端 netMode 可能不是 Server，
                // 直接用内部掷取方法验证核心逻辑）
                g.RollForTest(npc);
                if (!g.HasAffixes) throw new Exception("elite rolled no affixes");
                if (g.LifeMult < 1f || g.DamageMult < 1f)
                    throw new Exception($"bad mults life={g.LifeMult} dmg={g.DamageMult}");

                // 属性已乘：lifeMax >= 基础值（记录在 BaseLifeMax）
                if (g.BaseLifeMax <= 0) throw new Exception("base life not recorded");
                if (npc.lifeMax < g.BaseLifeMax) throw new Exception("life not scaled");

                // SendExtraAI / ReceiveExtraAI 往返（BitWriter.WriteBit/Flush + BitReader.ReadBit）
                var ms = new MemoryStream();
                using (var bw2 = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    var bitWriter = new BitWriter();
                    g.SendExtraAI(npc, bitWriter, bw2);
                    bitWriter.Flush(bw2);
                    bw2.Flush();
                }
                ms.Position = 0;
                var g2 = new EnemyAffixGlobalNPC();
                using (var br2 = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    var bitReader = new BitReader(br2);
                    g2.ReceiveExtraAI(npc, bitReader, br2);
                }
                if (g2.Affixes.Count != g.Affixes.Count)
                    throw new Exception($"net roundtrip count {g.Affixes.Count} -> {g2.Affixes.Count}");
                for (int i = 0; i < g.Affixes.Count; i++)
                    if (g2.Affixes[i] != g.Affixes[i])
                        throw new Exception($"net roundtrip mismatch at {i}: {g.Affixes[i]} -> {g2.Affixes[i]}");

                // RiftSystem 防线新分母：预期值 = 缓存基础 × 层缩放 × 词缀倍率；比值 ≈ 1（合法）
                int level = 5;
                double expectedLife = g.BaseLifeMax * (1f + 0.15f * level) * g.LifeMult;
                double lifeRatio = npc.lifeMax / Math.Max(1.0, expectedLife);
                if (lifeRatio > 100.0)
                    throw new Exception($"defense-line would false-kill: lifeRatio={lifeRatio:0.#}");

                return $"elite affixes={g.Affixes.Count} lifeMult={g.LifeMult:0.##} dmgMult={g.DamageMult:0.##} net={g2.Affixes.Count} ratio={lifeRatio:0.##}";
            }
            finally
            {
                cfg.Enable = oldEnable;
                cfg.CommonAffixChance = oldCommon;
                cfg.EliteChance = oldElite;
                cfg.BossAffixCountMin = oldBossMin; cfg.BossAffixCountMax = oldBossMax;
                cfg.BossExclusiveCountMin = oldBossExMin; cfg.BossExclusiveCountMax = oldBossExMax;
            }
        });
    }

    /// <summary>造一件可打宝的武器（原版木剑，全阶段可用）。</summary>
    private static Item NewEligible()
    {
        var item = new Item(ItemID.WoodenSword);
        if (item.IsAir) throw new Exception("wooden sword is air?");
        if (!ItemClassifier.IsEligible(item)) throw new Exception("wooden sword not eligible");
        return item;
    }

    public override void PostUpdateWorld()
    {
        if (_root == null || ++_tick % 30 != 0) return; // ~2x/sec
        try { Pump(); }
        catch { /* 吞掉：下个 tick 重试，保持服务器存活 */ }
    }

    private void Pump()
    {
        var cmdFile = Path.Combine(_root!, "in", "commands.txt");
        var resFile = Path.Combine(_root!, "out", "results.txt");
        if (!File.Exists(cmdFile)) return;

        string[] lines;
        try { lines = File.ReadAllLines(cmdFile); }
        catch (IOException) { return; }

        for (; _consumed < lines.Length; _consumed++)
        {
            var line = lines[_consumed].Trim();
            if (line.Length == 0) continue;

            if (line.Equals("#exit", StringComparison.OrdinalIgnoreCase))
            {
                WorldFile.SaveWorld();
                Environment.Exit(0);
            }
            if (line.StartsWith("#")) continue;

            var name = line.Split(' ')[0];
            if (!Tests.TryGetValue(name, out var body))
            {
                Emit(resFile, name, "FAIL", "no such test registered");
                continue;
            }
            try { Emit(resFile, name, "PASS", body() ?? ""); }
            catch (Exception e)
            {
                Emit(resFile, name, "FAIL", e.GetType().Name + ": " + e.Message);
            }
        }
    }

    private static void Emit(string file, string name, string status, string detail)
        => File.AppendAllText(file, $"[TEST] {name} {status} {detail}{Environment.NewLine}", Encoding.UTF8);
}
