using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria;

// 模组主类：类名 = 工程名（Looteria），继承 Mod。
// ⚠️ 命名空间陷阱：类 Looteria 与命名空间 Looteria 同名，
//    任何地方都不要写全限定 "Looteria.Common.X"（会解析成类 → CS0426），
//    一律用相对命名空间（如 Common.Data.AffixData）或 global:: 前缀。
public class Looteria : Mod
{
    /// <summary>模组实例（单例，Load 时赋值）。</summary>
    public static Looteria Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;
    }

    public override void Unload()
    {
        // Reload 前必须清静态引用，避免旧数据残留。
        Instance = null!;
        Common.Roll.LootGenerator.UnloadCache();
        Common.Systems.RiftSystem.ResetStaticData(); // 清秘境易变静态（含旧缩放缓存），Reload 后不残留
    }

    /// <summary>
    /// 跨模组公共 API（Mod.Call 字符串协议，弱类型）。
    /// 调用方示例：ModLoader.GetMod("Looteria")?.Call("GetRarity", item)
    /// 已实现："IsEligible"(Item)->bool、"GetRarity"(Item)->int(0-4)、"GetPowerScore"(Item)->int、
    ///        "RollAffix"(Item,int rarity 0-4)->bool、"ClearAffix"(Item)->bool、
    ///        "AddCurrency"(Player,int shards,int dust)->bool
    /// </summary>
    public override object Call(params object[] args)
    {
        if (args.Length == 0 || args[0] is not string name) return null!;
        try
        {
            switch (name)
            {
                case "IsEligible":
                    return args.Length >= 2 && args[1] is Item e && ItemClassifier.IsEligible(e);

                case "GetRarity":
                    return args.Length >= 2 && args[1] is Item r && r.TryGetGlobalItem(out AffixGlobalItem gr) ? (int)gr.Rarity : 0;

                case "GetPowerScore":
                    return args.Length >= 2 && args[1] is Item p && p.TryGetGlobalItem(out AffixGlobalItem gp) ? gp.PowerScore : 0;

                case "RollAffix":
                    if (args.Length >= 3 && args[1] is Item ra && args[2] is int rv && rv >= 0 && rv <= 4
                        && ra.TryGetGlobalItem(out AffixGlobalItem ga))
                    {
                        AffixRoller.Roll(ra, ga, (LootRarity)rv);
                        return true;
                    }
                    return false;

                case "ClearAffix":
                    if (args.Length >= 2 && args[1] is Item ca && ca.TryGetGlobalItem(out AffixGlobalItem gc))
                    {
                        AffixRoller.Clear(ca, gc); // M2：清词缀并还原售价
                        return true;
                    }
                    return false;

                case "AddCurrency":
                    if (args.Length >= 4 && args[1] is Player pl && args[2] is int sh && args[3] is int du)
                    {
                        if (sh < 0 || du < 0) return false; // L12：负数金额拒绝（契约：负数=非法输入）
                        var lp = pl.GetModPlayer<LooteriaPlayer>();
                        lp.AddBloodShards(sh);
                        lp.AddDust(du);
                        return true;
                    }
                    return false;
            }
        }
        catch
        {
            // 防御：任何异常都不外抛
        }
        return null!;
    }

    /// <summary>
    /// 网络消息接收（多人服务端权威协议，见 MULTIPLAYER-DESIGN.md §2.3）。
    /// ⚠️ 不设顶层 whoAmI 守卫：客户端收到服务端下行的 whoAmI 是哨兵值（≥255），
    /// 原守卫会把全部下行包丢弃（H5a）。身份规则：
    ///   上行包（客户端→服务端）：服务端以 whoAmI 为准（防冒名）；
    ///   下行包（服务端→客户端）：target/owner 一律取自包体。
    /// 整包 try/catch 防截断/畸形包（L13）；未知 type 记日志。
    /// </summary>
    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        try
        {
            var type = (LootMsg)reader.ReadByte();
            switch (type)
            {
                #region 上行：仅服务端处理
                case LootMsg.PlayerFullSync:
                {
                    int owner = reader.ReadByte();
                    int shards = reader.ReadInt32();
                    int dust = reader.ReadInt32();
                    int gearPower = reader.ReadInt32();
                    if (Main.netMode == NetmodeID.Server)
                    {
                        owner = whoAmI; // 鉴权：覆盖为真实连接来源（防冒名）
                        if (!TryGetLooteriaPlayer(owner, out var lp)) return;
                        // 货币是服务端权威：忽略包体金额，只采纳钳制后的 GearPower
                        lp.GearPower = Math.Clamp(gearPower, 0, NetCaps.MaxGearPower);
                        // R1：采纳客户端上传的容器（仅当服务端副本为空——非 SSC 服务器容器从空开始；
                        // SSC 服务器已有 LoadData 容器则保留服务端版本）
                        ReadContainerUpload(reader, lp);
                        SendCurrencyTo(owner);          // 立即纠偏：权威货币推回
                        lp.SyncPlayer(-1, owner, false); // 中继给其余客户端（官方范式，不带容器）
                    }
                    else
                    {
                        // 客户端消费中继：owner 来自包体（中继包不带容器字段）
                        if (!TryGetLooteriaPlayer(owner, out var lp)) return;
                        lp.BloodShards = Math.Clamp(shards, 0, NetCaps.MaxCurrency);
                        lp.Dust = Math.Clamp(dust, 0, NetCaps.MaxCurrency);
                        lp.GearPower = Math.Clamp(gearPower, 0, NetCaps.MaxGearPower);
                    }
                    break;
                }
                case LootMsg.LegacyClientChanges:
                    // H5c：旧货币上行通道废弃——收到即忽略（客户端旧值不得反向覆盖服务端记账）
                    Logger.Warn("[Looteria] ignored deprecated currency upload (type=1).");
                    return;

                case LootMsg.RiftStartRequest:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    int level = reader.ReadByte();
                    Common.Systems.RiftSystem.TryStartRiftServer(whoAmI, level);
                    break;
                }
                case LootMsg.GearPowerUpdate:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    int gp = reader.ReadInt32();
                    if (!TryGetLooteriaPlayer(whoAmI, out var lp)) return;
                    lp.GearPower = Math.Clamp(gp, 0, NetCaps.MaxGearPower);
                    break;
                }
                case LootMsg.HitEffectRequest:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    byte effectId = reader.ReadByte();
                    ushort npcIndex = reader.ReadUInt16();
                    int damageDone = reader.ReadInt32();
                    Common.Effects.LegendaryPowerHandler.HandleEffectRequest(whoAmI, effectId, npcIndex, damageDone);
                    break;
                }
                case LootMsg.GambleRequest:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    byte tier = reader.ReadByte();
                    byte count = reader.ReadByte();
                    Common.Roll.GambleService.HandleRequest(whoAmI, tier, count);
                    break;
                }
                case LootMsg.ContainerOpRequest:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    byte op = reader.ReadByte();
                    ushort slot = reader.ReadUInt16();
                    Common.Roll.GambleService.HandleContainerOp(whoAmI, op, slot);
                    break;
                }
                case LootMsg.RiftAbortRequest:
                {
                    if (Main.netMode != NetmodeID.Server) return;
                    // 中止请求：服务端权威清场（避免客户端本地清场双端不同步）；R6：校验发起者/参战者
                    Common.Systems.RiftSystem.AbortRift(whoAmI);
                    break;
                }
                #endregion

                #region 下行：仅客户端消费
                case LootMsg.CurrencyPush:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    int target = reader.ReadByte(); // target 来自包体（修复 H5a）
                    int shards = reader.ReadInt32();
                    int dust = reader.ReadInt32();
                    if (!TryGetLooteriaPlayer(target, out var lp)) return;
                    lp.BloodShards = Math.Clamp(shards, 0, NetCaps.MaxCurrency);
                    lp.Dust = Math.Clamp(dust, 0, NetCaps.MaxCurrency);
                    break;
                }
                case LootMsg.KillNotify:
                {
                    // R14（复审修正）：主机（listen server，netMode=Server 且 myPlayer 有效）也要显示飘字；
                    // 专用服 myPlayer=哨兵(≥maxPlayers) 无本地玩家可渲染 → 拒绝。
                    if (Main.netMode != NetmodeID.MultiplayerClient && Main.myPlayer >= Main.maxPlayers) return;
                    int killer = reader.ReadByte();
                    int shards = Math.Max(0, reader.ReadInt32());
                    int dust = Math.Max(0, reader.ReadInt32());
                    if (killer != Main.myPlayer) return; // 只有击杀者本人渲染（修复 L10）
                    ShowKillText(shards, dust);
                    break;
                }
                case LootMsg.RiftStartAck:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    bool ok = reader.ReadBoolean();
                    byte reason = reader.ReadByte();
                    byte level = reader.ReadByte();
                    int timer = reader.ReadInt32();
                    byte initiator = reader.ReadByte();
                    Common.Systems.RiftSystem.ApplyStartAck(ok, reason, level, timer, initiator);
                    break;
                }
                case LootMsg.RiftProgressPush:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    byte level = reader.ReadByte();
                    int progress = reader.ReadInt32();
                    int timer = reader.ReadInt32();
                    Common.Systems.RiftSystem.ApplyProgressMirror(Math.Clamp((int)level, 0, NetCaps.MaxRiftLevel),
                        Math.Max(0, progress), Math.Max(0, timer));
                    break;
                }
                case LootMsg.RiftBossNotify:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    short npcType = reader.ReadInt16();
                    if (npcType > 0)
                        Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftBoss"),
                            new Microsoft.Xna.Framework.Color(255, 130, 0));
                    break;
                }
                case LootMsg.RiftEndNotify:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    bool win = reader.ReadBoolean();
                    byte level = reader.ReadByte();
                    int best = Math.Max(0, reader.ReadInt32());
                    int shards = Math.Max(0, reader.ReadInt32());
                    int dust = Math.Max(0, reader.ReadInt32());
                    Common.Systems.RiftSystem.ApplyEndMirror(win, level, best, shards, dust);
                    break;
                }
                case LootMsg.ContainerPush:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    Common.Roll.GambleService.ReceiveContainer(reader);
                    break;
                }
                case LootMsg.ContainerOpResult:
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient) return;
                    byte op = reader.ReadByte();
                    bool ok = reader.ReadBoolean();
                    string? key = reader.ReadString();
                    Common.Roll.GambleService.ApplyOpResult(op, ok, key);
                    break;
                }
                #endregion

                default:
                    Logger.Warn($"[Looteria] unknown packet type {type}, dropped.");
                    break;
            }
        }
        catch (Exception e)
        {
            // tML 外层已有 read-underflow 校验并吞异常（ModNet.cs:643-650）；
            // 这里 catch 主要为了留下协议错配的排查日志（L13），绝不让异常上抛。
            Logger.Error($"[Looteria] HandlePacket failed (whoAmI={whoAmI}, netMode={Main.netMode}): {e}");
        }
    }

    /// <summary>按下标安全取玩家的 LooteriaPlayer（越界/未激活返回 false）。</summary>
    internal static bool TryGetLooteriaPlayer(int index, out Common.Players.LooteriaPlayer lp)
    {
        lp = null!;
        if (index < 0 || index >= Main.maxPlayers) return false;
        var p = Main.player[index];
        if (p == null || !p.active) return false;
        lp = p.GetModPlayer<Common.Players.LooteriaPlayer>();
        return lp != null;
    }

    /// <summary>R1：读取客户端加入时上传的掠夺容器（count ≤ 255，ItemIO 序列化）。
    /// 仅当服务端副本为空时采纳（非 SSC 服务器需要客户端容器；SSC 服务器以 LoadData 为准）。
    /// 无论是否采纳都必须读完包体（保持流对齐）。</summary>
    private static void ReadContainerUpload(BinaryReader reader, Common.Players.LooteriaPlayer lp)
    {
        try
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
                catch { /* 单件损坏：跳过该件，保持流对齐靠 ItemIO.Receive 自身异常被外层 catch */ }
            }
            if (lp.GambleContainer.Count == 0)
                lp.GambleContainer = list; // 服务端为空才采纳
        }
        catch (Exception e)
        {
            Instance?.Logger.Warn($"[Looteria] container upload read failed: {e}");
        }
    }

    // ===== 服务端发送助手 =====

    /// <summary>服务端：把 target 玩家的权威货币推给它自己的客户端（定向）。修复 H5a/H5c/L13。</summary>
    internal static void SendCurrencyTo(int target)
    {
        if (Main.netMode != NetmodeID.Server) return;
        if (!TryGetLooteriaPlayer(target, out var lp)) return;
        var p = Instance.GetPacket();
        p.Write((byte)LootMsg.CurrencyPush);
        p.Write((byte)target);
        p.Write(Math.Clamp(lp.BloodShards, 0, NetCaps.MaxCurrency));
        p.Write(Math.Clamp(lp.Dust, 0, NetCaps.MaxCurrency));
        p.Send(toClient: target); // 定向：ModPacket.Send(toClient,…)
    }

    /// <summary>服务端：击杀提示定向包（修复 L10：CombatText 纯客户端渲染，服务端画没人看得到）。</summary>
    internal static void SendKillNotify(int killer, int shards, int dust)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var p = Instance.GetPacket();
        p.Write((byte)LootMsg.KillNotify);
        p.Write((byte)killer);
        p.Write(Math.Max(0, shards));
        p.Write(Math.Max(0, dust));
        p.Send(toClient: killer);
    }

    /// <summary>客户端：击杀货币飘字（原 LootGlobalNPC 服务端 myPlayer 判断的客户端版）。</summary>
    private static void ShowKillText(int shards, int dust)
    {
        var rect = Main.LocalPlayer.getRect();
        if (shards > 0)
            Terraria.CombatText.NewText(rect, new Microsoft.Xna.Framework.Color(180, 40, 120),
                Language.GetTextValue("Mods.Looteria.Messages.LootGained", shards));
        if (dust > 0)
            Terraria.CombatText.NewText(rect, new Microsoft.Xna.Framework.Color(150, 150, 150),
                Language.GetTextValue("Mods.Looteria.Messages.DustGained", dust));
    }
}

/// <summary>Looteria 自定义消息类型。读写顺序必须与协议表（MULTIPLAYER-DESIGN.md §2.3）严格一致。</summary>
internal enum LootMsg : byte
{
    PlayerFullSync      = 0,  // 双向：加入全量/服务端中继
    LegacyClientChanges = 1,  // 废弃：旧货币上行通道，永不再发
    CurrencyPush        = 2,  // ↓ 货币全量推送（target 在包体）
    KillNotify          = 3,  // ↓ 击杀提示（定向击杀者）
    RiftStartRequest    = 4,  // ↑ 开局请求
    RiftStartAck        = 5,  // ↓ 开局确认（成功广播/失败定向）
    RiftProgressPush    = 6,  // ↓ 进度推送（节流广播）
    RiftBossNotify      = 7,  // ↓ Boss 出现通知（广播）
    RiftEndNotify       = 8,  // ↓ 结算通知（广播）
    GearPowerUpdate     = 9,  // ↑ 力量等级变更
    HitEffectRequest    = 10, // ↑ 命中效果服务端执行请求
    GambleRequest       = 11, // ↑ 抽奖请求（阶段 3）
    ContainerPush       = 12, // ↓ 容器全量同步（定向，阶段 3）
    ContainerOpRequest  = 13, // ↑ 领取/分解请求（阶段 3）
    ContainerOpResult   = 14, // ↓ 操作回执（定向，阶段 3）
    RiftAbortRequest    = 15, // ↑ 中止秘境请求（服务端权威清场，防客户端本地清场造成双端不同步）
}

/// <summary>合法性上限常量（钳制用）。</summary>
internal static class NetCaps
{
    public const int MaxGearPower = 1_000_000;
    public const int MaxCurrency  = 2_000_000_000; // int 上界内，防溢出的语义上限
    public const int MaxRiftLevel = 99;
}
