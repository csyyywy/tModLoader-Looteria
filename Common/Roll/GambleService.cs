using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;

namespace Looteria.Common.Roll;

/// <summary>
/// 抽奖/领取/分解的多人服务端权威入口（M13/H2/H5 修复；协议见 MULTIPLAYER-DESIGN.md §2.3 阶段 3）。
/// - 单机：UI 直接调本地方法（无网络开销，行为与旧版一致）。
/// - 多人：客户端发 GambleRequest/ContainerOpRequest → 服务端结算 → 回发 ContainerPush + CurrencyPush + ContainerOpResult。
/// - 服务端账户 = 权威：扣费/入账/容器改动只在服务端执行；客户端容器是镜像（整体替换）。
/// </summary>
public static class GambleService
{
    /// <summary>R5：抽奖请求限速（500ms）/ 容器操作限速（200ms），按玩家。</summary>
    private static readonly Dictionary<int, int> _lastGambleTick = new();
    private static readonly Dictionary<int, int> _lastOpTick = new();

    private static bool RateLimited(Dictionary<int, int> table, int requester, int ticks)
    {
        int now = (int)(Main.GameUpdateCount % int.MaxValue);
        if (table.TryGetValue(requester, out int last) && now - last < ticks) return true;
        table[requester] = now;
        return false;
    }
    // ===== 客户端入口（UI 调用）=====

    /// <summary>抽奖（单次）。多人：发请求包；单机：本地直通。</summary>
    public static void RequestGamble(int tierIndex, int count)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var p = Looteria.Instance.GetPacket();
            p.Write((byte)LootMsg.GambleRequest);
            p.Write((byte)tierIndex);
            p.Write((byte)(count == 10 ? 10 : 1));
            p.Send();
            return;
        }
        // 单机/服务端：直接结算（服务端视角 = 本机玩家）
        HandleRequest(Main.myPlayer, (byte)tierIndex, (byte)(count == 10 ? 10 : 1));
    }

    /// <summary>领取/分解操作。op: 0=ClaimOne, 1=ClaimAll, 2=SalvageAll。</summary>
    public static void RequestContainerOp(byte op, int slot)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var p = Looteria.Instance.GetPacket();
            p.Write((byte)LootMsg.ContainerOpRequest);
            p.Write(op);
            p.Write((ushort)slot);
            p.Send();
            return;
        }
        HandleContainerOp(Main.myPlayer, op, (ushort)slot);
    }

    // ===== 服务端结算 =====

    public static void HandleRequest(int requester, byte tierIndex, byte count)
    {
        if (Main.netMode != NetmodeID.Server && Main.netMode != NetmodeID.SinglePlayer) return;
        if (!Looteria.TryGetLooteriaPlayer(requester, out var lp)) return;
        // R5：限速 500ms（30 tick）。被限时回失败回执，客户端能看到"操作失败"而非毫无反应（复审补正）
        if (Main.netMode == NetmodeID.Server && RateLimited(_lastGambleTick, requester, 30))
        {
            SendOpResult(requester, 0, ok: false, "OpFailed");
            return;
        }
        var player = lp.Player;

        if (tierIndex < 0 || tierIndex >= GambleTiers.All.Length)
        {
            SendOpResult(requester, 0, ok: false, "OpFailed");
            return;
        }
        if (!GambleTiers.IsUnlocked(tierIndex))
        {
            SendOpResult(requester, 0, ok: false, "NotEnoughShards"); // 未解锁≈没资格抽该档
            return;
        }
        int n = count == 10 ? 10 : 1;
        int totalCost = GambleTiers.All[tierIndex].Cost * n;
        // 校验余额 + 容器余量（服务端权威账户）
        if (lp.BloodShards < totalCost)
        {
            SendOpResult(requester, 0, ok: false, "NotEnoughShards");
            return;
        }
        if (lp.GambleContainer.Count + n > LootGenerator.ContainerSize)
        {
            SendOpResult(requester, 0, ok: false, "ContainerFull");
            return;
        }

        // 扣费 + 结算（复用现有全套逻辑，操作的是服务端账户与容器）
        lp.BloodShards -= totalCost;
        for (int i = 0; i < n; i++)
            LootGenerator.Gamble(player, tierIndex, free: true); // 已扣费，free 跳过二次扣费

        SendContainerTo(requester, lp.GambleContainer);
        Looteria.SendCurrencyTo(requester);
        SendOpResult(requester, 0, ok: true, "Gambled");
    }

    public static void HandleContainerOp(int requester, byte op, ushort slot)
    {
        if (Main.netMode != NetmodeID.Server && Main.netMode != NetmodeID.SinglePlayer) return;
        if (!Looteria.TryGetLooteriaPlayer(requester, out var lp)) return;
        // R5：限速 200ms（12 tick）。被限时回失败回执（复审补正）
        if (Main.netMode == NetmodeID.Server && RateLimited(_lastOpTick, requester, 12))
        {
            SendOpResult(requester, op, ok: false, "OpFailed");
            return;
        }
        var player = lp.Player;

        switch (op)
        {
            case 0: // ClaimOne
            {
                if (slot < 0 || slot >= lp.GambleContainer.Count)
                {
                    SendOpResult(requester, op, ok: false, null);
                    return;
                }
                var it = lp.GambleContainer[slot];
                lp.GambleContainer.RemoveAt(slot);
                if (it != null && !it.IsAir)
                    player.QuickSpawnItem(player.GetSource_FromThis(), it, it.stack); // H2：保留堆叠
                break;
            }
            case 1: // ClaimAll
            {
                foreach (var it in lp.GambleContainer)
                    if (it != null && !it.IsAir)
                        player.QuickSpawnItem(player.GetSource_FromThis(), it, it.stack);
                lp.GambleContainer.Clear();
                break;
            }
            case 2: // SalvageAll（只分解带词缀装备，其余领回——M6）
            {
                int divisor = Math.Max(1, LooteriaConfig.Instance?.SalvageDivisor ?? 2);
                int dust = 0;
                foreach (var it in lp.GambleContainer)
                {
                    if (it == null || it.IsAir) continue;
                    if (it.TryGetGlobalItem(out AffixGlobalItem g) && g.HasAffix)
                        dust += Math.Max(1, g.PowerScore / divisor);
                    else
                        player.QuickSpawnItem(player.GetSource_FromThis(), it, it.stack);
                }
                if (dust > 0) lp.AddDust(dust);
                lp.GambleContainer.Clear();
                break;
            }
            default:
                SendOpResult(requester, op, ok: false, null);
                return;
        }

        SendContainerTo(requester, lp.GambleContainer);
        Looteria.SendCurrencyTo(requester);
        SendOpResult(requester, op, ok: true, null);
    }

    // ===== 容器全量同步（M13：ItemIO 序列化）=====

    public static void SendContainerTo(int toClient, List<Item> container)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.ContainerPush);

        var items = container.Where(i => i != null && !i.IsAir).ToList();
        // 钳制：数量上限 255（byte）；单包上限 ushort.MaxValue=65535（ModPacket.cs:71-72）。
        // 200 格 × 词缀物约 60~150B ≈ 最坏 ~30KB，仍在单包内。
        int n = Math.Min(items.Count, 255);
        p.Write((byte)n);
        for (int i = 0; i < n; i++)
            ItemIO.Send(items[i], p, writeStack: true); // netID+prefix+stack+全部 GlobalItem 词缀数据
        p.Send(toClient: toClient);
    }

    public static void ReceiveContainer(BinaryReader reader)
    {
        int n = reader.ReadByte();
        var list = new List<Item>(n);
        for (int i = 0; i < n; i++)
        {
            try
            {
                var it = ItemIO.Receive(reader, readStack: true);
                if (it != null && !it.IsAir) list.Add(it);
            }
            catch
            {
                // 单件损坏：跳过该件，尽力恢复其余
            }
        }
        var lp = Main.LocalPlayer.GetModPlayer<LooteriaPlayer>();
        lp.GambleContainer = list; // 整体替换镜像（服务端权威，不做增量合并）
        // UI 若开着，由调用方随后 Rebuild() 反映新容器
    }

    /// <summary>操作回执（定向）。客户端在赌博页显示消息。</summary>
    private static void SendOpResult(int toClient, byte op, bool ok, string? msgKey)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.ContainerOpResult);
        p.Write(op);
        p.Write(ok);
        p.Write(msgKey ?? "");
        p.Send(toClient: toClient);
    }

    public static void ApplyOpResult(byte op, bool ok, string key)
    {
        var ui = Common.UI.LooteriaUIState.Instance;
        if (ui == null) return;
        if (!ok)
        {
            Looteria.Instance?.Logger.Info($"[Looteria] container op {op} failed: {key}");
            ui.ShowOpResult(key ?? "OpFailed", ok: false); // R11：失败原因显示到赌博页日志
        }
        else
        {
            ui.ShowOpResult(key ?? "", ok: true); // 反映容器镜像变化
        }
    }
}
