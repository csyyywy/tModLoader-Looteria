using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
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

    public override bool IsLoadingEnabled(Mod mod)
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Looteria_TESTDIR"));

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
