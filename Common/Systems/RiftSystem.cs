using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria.Common.Systems;

/// <summary>
/// 秘境 = 入侵事件（自绘顶栏进度条，不占用原版入侵字段，避免与哥布林入侵挂钩）：
/// - 开启消耗血岩（50×层），掠夺之力门槛。
/// - 场上敌人数 ≥ 目标（至少 10，每层 +100，受 NPC 上限约束）；敌人死后持续快速补充 → 刷怪海。
/// - 时长 9 分钟；击杀小怪 +1、关底 Boss +40、每 10 秒自动 +1 进度；满 100 通关。
/// - 敌人来自全部已装模组 NPC（含模组怪）按生命分档；关底 Boss 进度≥70 或剩≤2 分钟出现。
/// </summary>
public class RiftSystem : ModSystem
{
    /// <summary>秘境时长（秒，可配置；默认霜月 9 分钟）。</summary>
    public static int RiftDurationTicks => (int)((LooteriaConfig.Instance?.RiftDurationMinutes ?? 9f) * 60 * 60);
    /// <summary>通关需要的总进度（要击败的敌人总量）：首层 100，之后每层 +25（100, 125, 150, …），即 100 + 25×(层-1)。</summary>
    public static int ProgressMax => 100 + 25 * Math.Max(0, CurrentLevel - 1);

    public static int CurrentLevel;
    public static int BestLevel;
    public static int Progress;
    public static int TimerTicks;
    public static int WaveIndex;
    private static int _spawnTimer;
    private static int _timeTick;
    private static int _bossType;
    private static readonly List<int> _minionPool = new();
    private static readonly Dictionary<int, int> _spawnWeights = new(); // 刷怪轮换权重：type → 本次入侵内已生成次数（生成时优先选最低）
    private static bool _bossSpawned;
    private static bool _completed;
    private static List<(int Type, int LifeMax, int Damage, int Defense, bool Boss)>? _npcCache;

    // ===== 多人（服务端权威，见 MULTIPLAYER-DESIGN.md 阶段 2）=====
    private static int _initiator = -1;                          // 发起者玩家下标（付费者）
    private static readonly HashSet<int> _participants = new();  // 参战玩家下标集合
    private static int _pushCooldown;                            // 进度推送节流计时（tick）
    private static int _lastPushedProgress = int.MinValue;       // 上次已推送进度（±5 触发）
    private static int _lastPushedTimer = int.MinValue;          // R12：上次已推送剩余时间（变化也触发）

    public static bool RiftActive => CurrentLevel > 0;
    /// <summary>
    /// 秘境开启力量门槛 = 层×25（**固定，不随难度/种子变化**）。
    /// </summary>
    public static int RiftRequirement(int level) => level * 25;
    public static int RiftCost(int level) => level * (LooteriaConfig.Instance?.RiftCostPerLevel ?? 50);

    /// <summary>层数是否已解锁：第 1 层始终可进，之后的层需通关上一关（可进 1 ~ BestLevel+1）。</summary>
    public static bool IsLevelUnlocked(int level) => level >= 1 && level <= BestLevel + 1;

    /// <summary>场上目标敌人数：至少 10，每层 +1（受 NPC 上限约束）。</summary>
    public static int FieldTarget(int level)
        => Math.Max(10, Math.Min(10 + (level - 1), Main.maxNPCs - 30));

    /// <summary>
    /// 客户端入口：组 RiftStartRequest 上行；单机（netMode==0）直接走服务端路径。
    /// 替代 UI 直接调 StartRift。多人下由服务端校验/扣费/初始化后广播 Ack。
    /// </summary>
    public static bool RequestStartRift(int level)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var p = Looteria.Instance.GetPacket();
            p.Write((byte)LootMsg.RiftStartRequest);
            p.Write((byte)level);
            p.Send();
            return true; // 结果由 Ack 异步通知
        }
        // 单机/服务端：直接走权威路径（服务端或单机本机）
        return TryStartRiftServer(Main.myPlayer, level);
    }

    /// <summary>
    /// 服务端受理开局：校验 !RiftActive、level∈[1,99]、发起者 GearPower≥门槛、
    /// BloodShards≥费用（读服务端账户）→ 扣费 → 初始化 → 首波铺怪 → 广播/定向 RiftStartAck。
    /// 原 StartRift 主体（单人路径）改名 InitRiftState 复用。
    /// </summary>
    public static bool TryStartRiftServer(int requester, int level)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return false; // 客户端绝不本地开局
        if (RiftActive || level < 1 || level > NetCaps.MaxRiftLevel) return false;
        if (!Looteria.TryGetLooteriaPlayer(requester, out var lp)) return false;
        if (lp.GearPower < RiftRequirement(level)) { SendStartAck(requester, ok: false, reason: 1, level); return false; }
        int cost = RiftCost(level);
        if (lp.BloodShards < cost) { SendStartAck(requester, ok: false, reason: 2, level); return false; }
        lp.BloodShards -= cost;

        InitRiftState(level);

        // 参战者 = 开局全员（服务端视角在线玩家）
        _participants.Clear();
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i] is { active: true }) _participants.Add(i);
        _initiator = requester;
        _pushCooldown = 0;
        _lastPushedProgress = int.MinValue;
        _lastPushedTimer = int.MinValue; // 复审补正：新一局重置推送基准，避免首推延迟

        // 立即铺一批（首波翻倍：目标数×2，上限 80）
        int first = Math.Min(FieldTarget(level) * 2, 80);
        for (int i = 0; i < first; i++) SpawnRiftMinion();

        // 服务端文本自动以聊天广播（原版行为）；另发 Ack 同步镜像字段
        Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftStarted", level, cost), new Color(120, 200, 255));
        BroadcastStartAck(ok: true, reason: 0, level, TimerTicks, requester);

        // 地牢守卫提醒（原版机制怪，非秘境怪）
        if (!NPC.downedBoss2 && lp.Player.ZoneDungeon)
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftDungeonWarn"), new Color(255, 150, 60));

        EnsureCache();
        Looteria.Instance?.Logger.Info(
            $"Rift started: level={level}, initiator={requester}, minionPool={_minionPool.Count} types, cacheNpcs={_npcCache?.Count}, bossType={_bossType}");
        return true;
    }

    /// <summary>单人路径主体（原 StartRift 的初始化部分，不含扣费/校验/网络）。</summary>
    private static void InitRiftState(int level)
    {
        CurrentLevel = level;
        Progress = 0;
        TimerTicks = RiftDurationTicks;
        WaveIndex = 0;
        _spawnTimer = 0;
        _timeTick = 0;
        _bossType = PickBossForLevel(level);
        _minionPool.Clear();
        _minionPool.AddRange(PickMinionsForLevel(level));
        // 每次入侵开始时给池里每个类型一个权重（都从 0 起）
        _spawnWeights.Clear();
        foreach (var t in _minionPool) _spawnWeights[t] = 0;
        _bossSpawned = false;
        _completed = false;
    }

    /// <summary>客户端：应用 RiftStartAck → 写镜像字段。失败时显示原因（R11）。</summary>
    public static void ApplyStartAck(bool ok, byte reason, byte level, int timer, byte initiator)
    {
        if (!ok)
        {
            CurrentLevel = 0;
            // reason: 1=力量不足 2=血岩不足 0=其他（用发起者视角的数值差填充占位符）
            if (reason == 1)
            {
                var lp = Main.LocalPlayer.GetModPlayer<LooteriaPlayer>();
                int deficit = Math.Max(0, RiftRequirement(level) - lp.GearPower);
                Main.NewText(Language.GetTextValue("Mods.Looteria.UI.RiftNeedPower", deficit), new Color(255, 150, 60));
            }
            else if (reason == 2)
            {
                var lp = Main.LocalPlayer.GetModPlayer<LooteriaPlayer>();
                int deficit = Math.Max(0, RiftCost(level) - lp.BloodShards);
                Main.NewText(Language.GetTextValue("Mods.Looteria.UI.RiftNeedShards", deficit), new Color(255, 150, 60));
            }
            else
            {
                Main.NewText(Language.GetTextValue("Mods.Looteria.UI.RiftStartFail"), new Color(255, 150, 60));
            }
            return;
        }
        CurrentLevel = Math.Clamp((int)level, 0, NetCaps.MaxRiftLevel);
        TimerTicks = Math.Max(0, timer);
        Progress = 0;
        _initiator = initiator;
        _completed = false;
    }

    /// <summary>客户端：应用进度推送镜像（ProgressPush/EndNotify 共用底层）。</summary>
    public static void ApplyProgressMirror(int level, int progress, int timer)
    {
        CurrentLevel = level;
        Progress = Math.Max(0, progress);
        TimerTicks = Math.Max(0, timer);
    }

    /// <summary>客户端：应用结算通知镜像。</summary>
    public static void ApplyEndMirror(bool win, int level, int best, int shards, int dust)
    {
        BestLevel = Math.Max(0, Math.Min(best, NetCaps.MaxRiftLevel));
        CurrentLevel = 0;
        Progress = 0;
        if (win)
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftCompleted", level, shards, dust), new Color(0, 255, 120));
        else
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftFailed"), new Color(255, 90, 90));
    }

    private static void SendStartAck(int toClient, bool ok, byte reason, int level)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.RiftStartAck);
        p.Write(ok);
        p.Write(reason);
        p.Write((byte)level);
        p.Write(0);
        p.Write((byte)0);
        p.Send(toClient: toClient);
    }

    private static void BroadcastStartAck(bool ok, byte reason, int level, int timer, int initiator)
    {
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.RiftStartAck);
        p.Write(ok);
        p.Write(reason);
        p.Write((byte)level);
        p.Write(timer);
        p.Write((byte)initiator);
        p.Send();
    }

    /// <summary>参战登记：开局全员加入；击杀秘境怪时把击杀者补进集合。</summary>
    public static void AddParticipant(int who)
    {
        if (who >= 0 && who < Main.maxPlayers) _participants.Add(who);
    }

    /// <summary>★替代 Main.LocalPlayer 的锚点：参战者中随机选一名在线玩家；无参战者退化为任意在线玩家。</summary>
    private static Player? PickAnchor()
    {
        var pool = new List<Player>();
        foreach (var idx in _participants)
            if (idx >= 0 && idx < Main.maxPlayers && Main.player[idx] is { active: true } pl)
                pool.Add(pl);
        if (pool.Count == 0)
            for (int i = 0; i < Main.maxPlayers; i++)
                if (Main.player[i] is { active: true } pl) pool.Add(pl);
        return pool.Count == 0 ? null : pool[Main.rand.Next(pool.Count)];
    }

    /// <summary>供敌人词缀系统（风暴之眼）选服务端锚点：参战者优先，退化为任意在线玩家（无则 null）。</summary>
    public static Player? PickAnchorForProjectile()
        => RiftActive ? PickAnchor() : AnyOnlinePlayer();

    private static Player? AnyOnlinePlayer()
    {
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i] is { active: true } pl) return pl;
        return null;
    }

    /// <summary>进度推送节流：至少间隔 60 tick 且（|Δprogress|≥5 或 剩余时间变化 ≥60 tick）；通关/失败前 force=true 强制。</summary>
    private static void PushProgressIfDirty(bool force)
    {
        if (Main.netMode != NetmodeID.Server) return;
        bool tick = ++_pushCooldown >= 60;
        bool delta = Math.Abs(Progress - _lastPushedProgress) >= 5
                  || Math.Abs(TimerTicks - _lastPushedTimer) >= 60; // R12：倒计时也会触发，客户端不滞后
        if (!force && !(tick && delta)) return;
        _pushCooldown = 0;
        _lastPushedProgress = Progress;
        _lastPushedTimer = TimerTicks;
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.RiftProgressPush);
        p.Write((byte)CurrentLevel);
        p.Write(Progress);
        p.Write(TimerTicks);
        p.Send(); // 广播：秘境是全服事件
    }

    /// <summary>客户端入口：中止秘境（多人发请求包，服务端权威清场；单机直通）。</summary>
    public static void RequestAbortRift()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var p = Looteria.Instance.GetPacket();
            p.Write((byte)LootMsg.RiftAbortRequest);
            p.Send();
            return;
        }
        AbortRift(Main.myPlayer);
    }

    /// <summary>服务端：中止秘境。R6：仅发起者/参战者有权中止（防任意玩家骚扰他人秘境）。</summary>
    public static void AbortRift(int requester = -1)
    {
        if (!RiftActive) return;
        // 服务端收到的中止请求需鉴权；单机/无请求者（内部调用）放行
        if (Main.netMode == NetmodeID.Server && requester >= 0
            && requester != _initiator && !_participants.Contains(requester))
        {
            Looteria.Instance?.Logger.Info($"[Looteria] RiftAbortRequest denied (player {requester} not initiator/participant).");
            return;
        }
        int level = CurrentLevel;
        CurrentLevel = 0;
        DespawnRiftNpcs();
        PushProgressIfDirty(force: true);
        // 多人：广播结算通知（客户端清镜像/关闭顶栏）
        BroadcastRiftEnd(win: false, level);
        _participants.Clear();
        _initiator = -1;
    }

    public override void PostUpdateWorld()
    {
        // 多人：客户端绝不推进逻辑（镜像靠收包）；单机/服务端推进
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        Update();
    }

    public static void Update()
    {
        if (!RiftActive) return;

        if (TimerTicks > 0) TimerTicks--;
        if (TimerTicks <= 0 && !_completed) { FailRift(); return; }

        // 时间兜底：每 10 秒 +1 进度
        if (!_completed && ++_timeTick >= 600) { _timeTick = 0; AddProgress(1); }

        // 维持场上敌人数
        if (!_completed)
        {
            int active = CountRiftEnemies();
            int target = FieldTarget(CurrentLevel);
            if (active < target)
            {
                _spawnTimer++;
                if (_spawnTimer >= 5)
                {
                    _spawnTimer = 0;
                    int deficit = target - active;
                    int toSpawn = Math.Min(deficit, deficit >= 20 ? 20 : 12);
                    for (int i = 0; i < toSpawn; i++) SpawnRiftMinion();
                    if (toSpawn > 0) WaveIndex++;
                }
            }
        }

        // 关底 Boss
        if (!_bossSpawned && (Progress >= 70 || TimerTicks <= 60 * 60 * 2)) SpawnBoss();

        // 进度推送（服务端 → 全员镜像，节流）
        if (Main.netMode == NetmodeID.Server) PushProgressIfDirty(force: false);
    }

    /// <summary>秘境怪击杀回调（只计秘境刷的敌人）。</summary>
    public static void OnEnemyKilled(NPC npc)
    {
        if (!RiftActive || _completed) return;
        var g = npc.GetGlobalNPC<LootGlobalNPC>();
        if (!g.IsRiftSpawned) return;
        // 参战登记：打过秘境怪就算参战（发奖集合）
        AddParticipant(npc.lastInteraction);
        AddProgress(g.IsRiftBoss ? 40 : 1);
    }

    private static void AddProgress(int n)
    {
        Progress += n;
        if (Progress >= ProgressMax) CompleteRift();
    }

    private static void SpawnRiftMinion()
    {
        if (_minionPool.Count == 0) return;
        var player = PickAnchor(); // 多人：参战者锚点，替代 Main.LocalPlayer
        if (player == null) return;
        int type = PickMinionType(); // 优先选权重最低的类型（并列随机）→ 轮换出怪
        if (SpawnRiftNpc(player, type, false)) // 刷出成功才累加该类型权重
            _spawnWeights[type] = (_spawnWeights.TryGetValue(type, out int w) ? w : 0) + 1;
    }

    /// <summary>
    /// 优先选权重最低的敌人类型（并列则随机）——保证不同敌人轮换出现，而不是总刷同 1~2 种。
    /// 权重 = 本次入侵内该类型已成功生成的次数（每次入侵开始清零、结束时清理）。
    /// </summary>
    private static int PickMinionType()
    {
        int best = int.MaxValue;
        var ties = new List<int>();
        foreach (var t in _minionPool)
        {
            int w = _spawnWeights.TryGetValue(t, out int v) ? v : 0;
            if (w < best) { best = w; ties.Clear(); ties.Add(t); }
            else if (w == best) ties.Add(t);
        }
        if (ties.Count == 0) return _minionPool[Main.rand.Next(_minionPool.Count)];
        return ties[Main.rand.Next(ties.Count)];
    }

    private static void SpawnBoss()
    {
        if (_bossType <= 0) { _bossSpawned = true; return; } // 本层无合格关底：标记已处理
        var player = PickAnchor(); // 多人：参战者锚点
        if (player == null) return;
        _bossSpawned = true;
        if (SpawnRiftNpc(player, _bossType, true)) // 实测超上限会拦截
        {
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftBoss"), new Color(255, 130, 0));
            // 多人：广播 Boss 出现通知（客户端播放文本/音效）
            if (Main.netMode == NetmodeID.Server)
            {
                var p = Looteria.Instance.GetPacket();
                p.Write((byte)LootMsg.RiftBossNotify);
                p.Write((short)_bossType);
                p.Send();
            }
        }
    }

    /// <summary>刷出一只秘境怪。返回是否成功刷出（超上限的请求会返回 false）。</summary>
    private static bool SpawnRiftNpc(Player player, int type, bool isBoss)
    {
        // 故障隔离：单个 NPC 类型异常（模组怪 NewNPC/AI 抛错、虫体分段怪等）绝不能中断整批刷怪或拖死秘境，
        // 一律捕获记日志并返回失败，其余正常类型继续刷 → 入侵进度不会因此归零。
        try
        {
            // 屏幕外生成：随机左右/上下方向，敌人从场外涌进来；并校验生成点在空气/地面上，不卡墙
            if (!TryGetSpawnPos(player, isBoss, out int x, out int y)) return false;

            int id = NPC.NewNPC(new EntitySource_SpawnNPC(), x, y, type);
            if (id < 0 || id >= Main.npc.Length) return false;
            var npc = Main.npc[id];
            if (npc == null) return false;
            // 实测校验（防 mod 生成时把属性暴缩成超大，如 400 万血最终 Boss）：
            // 用「生成后实际值 / 预期值」的比值判断。预期值 = 缓存 SetDefaults 值 × 层缩放 × 词缀倍率
            //（EnemyAffixGlobalNPC 在 OnSpawn 记录 LifeMult/DamageMult）。
            // 合法叠加（词缀 + 层缩放 + 大师/FTW 模式缩放）比值天然 ~1~6；
            // 阈值 100 只抓 mod 在生成时把属性改到完全离谱（400 万血 → 比值上万）这种极端，绝不误杀。
            var cacheEntry = _npcCache!.FirstOrDefault(e => e.Type == type);
            if (cacheEntry.Type != 0)
            {
                npc.TryGetGlobalNPC(out EnemyAffixGlobalNPC eag);
                double lifeExpected = cacheEntry.LifeMax * (1f + 0.15f * CurrentLevel) * Math.Max(1f, eag?.LifeMult ?? 1f);
                double dmgExpected = cacheEntry.Damage * (1f + 0.10f * CurrentLevel) * Math.Max(1f, eag?.DamageMult ?? 1f);
                double lifeRatio = npc.lifeMax / Math.Max(1.0, lifeExpected);
                double dmgRatio = npc.damage / Math.Max(1.0, dmgExpected);
                if (lifeRatio > 100.0 || dmgRatio > 100.0)
                {
                    global::Looteria.Looteria.Instance?.Logger.Info(
                        $"Rift despawned over-scaled npc: type={type}, life={npc.lifeMax}(expected {lifeExpected:0}), dmg={npc.damage}(expected {dmgExpected:0}), def={npc.defense}, lifeRatio={lifeRatio:0.#}, dmgRatio={dmgRatio:0.#}, level={CurrentLevel}");
                    npc.active = false;
                    return false;
                }
            }
            var g = npc.GetGlobalNPC<LootGlobalNPC>();
            g.IsRiftSpawned = true;
            if (isBoss) g.IsRiftBoss = true;
            return true;
        }
        catch (Exception e)
        {
            global::Looteria.Looteria.Instance?.Logger.Error($"Rift spawn failed: type={type}, isBoss={isBoss}, {e}");
            return false;
        }
    }

    private static int CountRiftEnemies()
    {
        int n = 0;
        for (int i = 0; i < Main.npc.Length; i++)
        {
            var npc = Main.npc[i];
            if (npc != null && npc.active && npc.TryGetGlobalNPC(out LootGlobalNPC g) && g.IsRiftSpawned) n++;
        }
        return n;
    }

    /// <summary>
    /// 找一个"不卡墙"的屏幕外生成点：
    /// 随机左右/上下方向的可见区外候选点 → 在 ±3 格宽内找一列"候选行是空气"的列
    /// → 沿该列向下找最近的实心/平台地面，生成在其上方 1 格（走地怪能站住，飞行怪无所谓）
    /// → 若 60 格内无地面（开阔天空/巨大洞穴），直接在空气格生成。
    /// 找不到（如被密封墙围死）返回 false，由调用方跳过本只、下波再补。
    /// </summary>
    private static bool TryGetSpawnPos(Player player, bool isBoss, out int sx, out int sy)
    {
        int edge = Main.screenWidth / 2 + 60;
        int vEdge = Main.screenHeight / 2 + 60;
        int range = isBoss ? 260 : 180; // Boss 生成得稍远些，登场感更强
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int x, y;
            if (Main.rand.NextBool())
            {
                x = (int)player.Center.X + (Main.rand.NextBool() ? -1 : 1) * Main.rand.Next(edge, edge + range);
                y = (int)player.Center.Y + Main.rand.Next(-vEdge, vEdge + 1);
            }
            else
            {
                x = (int)player.Center.X + Main.rand.Next(-edge, edge + 1);
                y = (int)player.Center.Y + (Main.rand.NextBool() ? -1 : 1) * Main.rand.Next(vEdge, vEdge + range);
            }
            x = Math.Clamp(x, 60, Main.maxTilesX * 16 - 60); // 世界边界夹取
            y = Math.Clamp(y, 60, Main.maxTilesY * 16 - 60);

            int candTileX = x / 16;
            int candTileY = y / 16;
            // 在 ±3 格宽内找"候选行是空气"的一列（避开卡进墙里）
            for (int dx = -3; dx <= 3; dx++)
            {
                int tileX = candTileX + dx;
                if (tileX < 1 || tileX >= Main.maxTilesX - 1) continue;
                if (!IsAir(tileX, candTileY)) continue; // 这列当前行在墙里 → 换列

                int maxScan = Math.Min(Main.maxTilesY - 3, candTileY + 60);
                for (int ty = candTileY; ty < maxScan; ty++)
                {
                    if (IsSolidFloor(tileX, ty))
                    {
                        // 地面在 ty 行，怪站在其上方一格（中心 y = ty*16 - 8）
                        sx = tileX * 16 + 8;
                        sy = Math.Max(8, ty * 16 - 8);
                        return true;
                    }
                }
                // 60 格内无地面（开阔天空/巨大洞穴）→ 直接在空气格生成
                sx = tileX * 16 + 8;
                sy = Math.Max(8, candTileY * 16);
                return true;
            }
        }
        sx = 0;
        sy = 0;
        return false;
    }

    /// <summary>该格是"实心地/平台"（可作为地面站脚）。</summary>
    private static bool IsSolidFloor(int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY) return false;
        var t = Main.tile[tileX, tileY];
        if (t == null || !t.HasTile) return false;
        ushort type = t.TileType;
        return Main.tileSolid[type] || Main.tileSolidTop[type];
    }

    /// <summary>该格不是实心方块（空气/平台/液体都算"可站"）。</summary>
    private static bool IsAir(int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY) return false;
        var t = Main.tile[tileX, tileY];
        if (t == null || !t.HasTile) return true;
        return !Main.tileSolid[t.TileType];
    }

    private static void CompleteRift()
    {
        _completed = true;
        int level = CurrentLevel;
        BestLevel = Math.Max(BestLevel, level); // 服务端更新（持久化见下）
        CurrentLevel = 0;
        DespawnRiftNpcs();
        PushProgressIfDirty(force: true); // R12：清层后推一个 level=0 的包（随后 EndNotify 覆盖，无害）
        BroadcastRiftEnd(win: true, level);
        AwardRewards(level);
    }

    private static void FailRift()
    {
        int level = CurrentLevel;
        CurrentLevel = 0;
        DespawnRiftNpcs();
        PushProgressIfDirty(force: true);
        if (Main.netMode != NetmodeID.MultiplayerClient)
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftFailed"), new Color(255, 90, 90));
        BroadcastRiftEnd(win: false, level);
        _participants.Clear(); // R14：失败也要清参战集合/发起者（卫生）
        _initiator = -1;
    }

    private static void BroadcastRiftEnd(bool win, int level)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var cfg = LooteriaConfig.Instance;
        int shards = (int)(100 * level * (cfg?.BloodShardRate ?? 1f));
        int dust = (int)(30 * level * (cfg?.DustRate ?? 1f));
        var p = Looteria.Instance.GetPacket();
        p.Write((byte)LootMsg.RiftEndNotify);
        p.Write(win);
        p.Write((byte)level);
        p.Write(BestLevel);
        p.Write(shards);
        p.Write(dust);
        p.Send(); // 广播：全员更新 BestLevel 镜像与结算文本
    }

    private static void DespawnRiftNpcs()
    {
        for (int i = 0; i < Main.npc.Length; i++)
        {
            var n = Main.npc[i];
            if (n == null || !n.active) continue;
            var g = n.GetGlobalNPC<LootGlobalNPC>();
            if (!g.IsRiftSpawned) continue;
            if (Main.netMode == NetmodeID.Server)
            {
                // 多人清场双端一致：先广播"即死打击"让客户端本地消散（不掉落/不触发 OnKill）
                // 服务端这里【不走】StrikeNPC → 不触发 OnKill/掉落/进度误计，只是净清场。
                // 注：SendStrikeNPC 会把伤害同步到客户端，客户端本地消散。
                NetMessage.SendStrikeNPC(n, new NPC.HitInfo
                {
                    InstantKill = true,
                    HideCombatText = true,
                });
            }
            n.active = false;
        }
        _spawnWeights.Clear(); // 每次入侵结束/中止：清理本轮刷怪权重
    }

    /// <summary>结算发奖：逐参战者（多人）；单人 = 仅本机。服务端唯一执行。</summary>
    private static void AwardRewards(int level)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return; // 服务端发奖
        var cfg = LooteriaConfig.Instance;
        int shards = (int)(100 * level * (cfg?.BloodShardRate ?? 1f));
        int dust = (int)(30 * level * (cfg?.DustRate ?? 1f));

        foreach (var idx in _participants.ToArray())
        {
            if (!Looteria.TryGetLooteriaPlayer(idx, out var lp)) continue;
            lp.AddBloodShards(shards);
            lp.AddDust(dust);

            // 服务端掷词缀 → 克隆发放（QuickSpawnItem 克隆重载、stack 覆盖克隆堆叠、
            // 多人自动 SyncItem 携带词缀数据）
            ItemCategory[] cats = { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory };
            var cat = cats[Main.rand.Next(cats.Length)];
            var item = RollRewardItem(cat, level);
            if (item != null)
                lp.Player.QuickSpawnItem(lp.Player.GetSource_FromThis(), item, 1);

            Looteria.SendCurrencyTo(idx); // 个人货币定向回推（权威值）
        }
        _participants.Clear();
        _initiator = -1;

        // 单机/服务端本机文本（多人下客户端由 RiftEndNotify 显示）
        if (Main.netMode == NetmodeID.SinglePlayer)
            Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.RiftCompleted", level, shards, dust), new Color(0, 255, 120));
    }

    private static Item? RollRewardItem(ItemCategory cat, int level)
    {
        int tries = 0;
        while (tries++ < 20)
        {
            var pool = LootGenerator.GetCandidatePool(cat);
            if (pool == null || pool.Count == 0) return null;
            var pick = pool[Main.rand.Next(pool.Count)];
            var item = new Item(pick.Type);
            if (!ItemClassifier.IsEligible(item) || !item.TryGetGlobalItem(out AffixGlobalItem g)) continue;
            LootRarity rarity = DropTable.RollRarity(DropSource.Rift, 1f, level);
            if (rarity == LootRarity.None) rarity = LootRarity.Rare;
            AffixRoller.Roll(item, g, rarity);
            return item;
        }
        return null;
    }

    // ===== 敌人池（全部已装模组 NPC，支持模组敌人） =====

    private static void EnsureCache()
    {
        if (_npcCache != null) return;
        var list = new List<(int, int, int, int, bool)>();
        for (int i = 1; i < NPCLoader.NPCCount; i++)
        {
            try
            {
                // 硬排除：地牢守卫（原版"未击败骷髅王进入地牢区"时自行刷出的守卫）绝不进秘境池，
                // 即使强度计算/模式修正有任何偏差也保证选不中它（它是原版机制怪，不是秘境怪）。
                if (i == NPCID.DungeonGuardian) continue;
                var npc = new NPC();
                npc.SetDefaults(i);
                if (npc.type != i) continue;
                // **缓存原样存 SetDefaults 值，不再归一化**：SetDefaults 是否套模式缩放取决于调用时的
                // netMode/expertMode（客户端与服务端上下文可能不同），任何"回除"都会把缓存搞成不稳定的
                // 错误值（实测曾把 150 血怪算成 12、比值防线 28x 全误杀）。统一原样存，层上限自适应
                //（见 MaxEnemyPower），保证池筛选与刷出在同一空间、互不矛盾。
                int life = npc.lifeMax;
                int dmg = npc.damage;
                int def = npc.defense;
                // 排除：友好 / 纯弹幕召唤（damage<=0）/ 投射物实体（生命<10）
                if (npc.friendly || dmg <= 0 || life < 10) continue;
                // **过滤稀有/彩蛋生物**：rarity > 0 = 稀有/彩蛋档（金色史莱姆、Tim、冥王等），
                // 不是正经战斗怪，不进秘境池（用户要求）。
                if (npc.rarity > 0) continue;
                // 弹幕/召唤物实体（关键）：会穿墙（noTileCollide）且伤害高于自身生命（一击即碎、不是正经怪），
                // 如克苏鲁之仆（基础 5 血 12 攻）。此判定与模式缩放无关，全模式生效；
                // 正经怪都是生命远大于伤害（史莱姆 15/6、恶魔眼 46/14、陨石头 60/30、幻灵 150/40）。
                if (npc.noTileCollide && dmg > life) continue;
                // 蠕虫分段怪（世界吞噬怪 14/15、双足翼龙、毁灭者等）：realLife 在 SetDefaults 时恒为 -1
                //（运行时才指向头节），旧的 realLife 过滤是死代码；单独刷出分段会立刻消失/异常 →
                // 用原版蠕虫 AI 排除。
                if (npc.aiStyle == NPCAIStyleID.Worm) continue;
                list.Add((i, life, dmg, def, npc.boss));
            }
            catch { }
        }
        _npcCache = list;
    }

    /// <summary>
    /// 敌人强度分（乘积对数，用户选定 B 方案）：`ln(生命) × ln(伤害 × max(1, 防御))`。
    /// 用基础（未缩放）属性计算——与模式/种子无关 → **各难度同一层选同一批敌人**（用户既定目标），
    /// 实际难度由游戏本身的模式缩放提供。数值范围 6.7~129（400 万血 Boss 也仅 ~129），不膨胀。
    /// </summary>
    private static double EnemyPower(int lifeMax, int damage, int defense)
    {
        double lnLife = Math.Log(Math.Max(1, lifeMax));
        double lnAtkDef = Math.Log(Math.Max(1, damage) * Math.Max(1, defense));
        return lnLife * lnAtkDef;
    }

    /// <summary>自适应首层上限（= 缓存中第 15 弱非 Boss 怪的强度分；首层池更大、含弱怪）。</summary>
    private static double _capBase = -1;

    /// <summary>
    /// 层数允许的最大敌人强度（仅上限约束、不设下限；指数增长）。
    /// 首层上限**自适应当前缓存**：取第 15 弱非 Boss 怪的强度分（`_capBase`，下限 6），
    /// 之后每层 ×1.068。无论缓存放的是基础值还是 SetDefaults 缩放值，首层都包含最弱的十几种 →
    /// 史莱姆等弱怪必进，池筛选与刷出在同一缓存空间。约 33 层 ≈ 首层分的 9.2 倍强度。
    /// </summary>
    private static double MaxEnemyPower(int level)
    {
        if (_capBase < 0) InitCapBase();
        return _capBase * Math.Pow(1.068, level - 1);
    }

    private static void InitCapBase()
    {
        _capBase = 10; // 兜底
        if (_npcCache is { Count: > 0 })
        {
            var powers = _npcCache.Where(c => !c.Boss)
                .Select(c => EnemyPower(c.LifeMax, c.Damage, c.Defense))
                .OrderBy(p => p).ToList();
            if (powers.Count > 0)
            {
                int idx = Math.Min(14, powers.Count - 1); // 第 15 弱（index 14）
                _capBase = Math.Max(6, powers[idx]);
            }
        }
    }

    private static int PickBossForLevel(int level)
    {
        EnsureCache();
        double cap = MaxEnemyPower(level);
        // M11 方案 A：选池口径统一——用"生成后实际强度"筛选（生命 ×(1+0.15L)、伤害 ×(1+0.10L)），
        // 与 LootGlobalNPC.OnSpawn 的缩放一致，避免"池里合格、刷出即超限被清"的刷-清循环。
        double scaledPower(int life, int dmg, int def) =>
            EnemyPower((int)(life * (1f + 0.15f * level)), (int)(dmg * (1f + 0.10f * level)), def);
        // 只从「强度分 ≤ 当前层上限」的 Boss 里选；无符合的 Boss → 本层不刷关底（返回 0，进度靠小怪）
        var bosses = _npcCache!.FindAll(c => c.Boss && scaledPower(c.LifeMax, c.Damage, c.Defense) <= cap);
        if (bosses.Count == 0)
        {
            global::Looteria.Looteria.Instance?.Logger.Info($"Rift boss skipped: level={level}, cap={cap:0.##}, no eligible boss");
            return 0;
        }
        return bosses[Main.rand.Next(bosses.Count)].Type;
    }

    private static List<int> PickMinionsForLevel(int level)
    {
        EnsureCache();
        double cap = MaxEnemyPower(level);
        // M11 方案 A：用缩放后强度筛选（与刷出后实际属性同口径）
        double scaledPower(int life, int dmg, int def) =>
            EnemyPower((int)(life * (1f + 0.15f * level)), (int)(dmg * (1f + 0.10f * level)), def);
        // 只按上限筛选（用户确认：只设上限、不设下限）
        var all = _npcCache!.FindAll(c => !c.Boss && scaledPower(c.LifeMax, c.Damage, c.Defense) <= cap);
        // 原版怪优先占满，mod 怪补缺
        var vanilla = all.Where(c => c.Type < NPCID.Count).ToList();
        var modded = all.Where(c => c.Type >= NPCID.Count).ToList();
        Shuffle(vanilla);
        Shuffle(modded);
        var list = new List<int>();
        // 池上限 32
        foreach (var e in vanilla) { if (list.Count >= 32) break; if (!list.Contains(e.Type)) list.Add(e.Type); }
        foreach (var e in modded) { if (list.Count >= 32) break; if (!list.Contains(e.Type)) list.Add(e.Type); }
        // 极端兜底：万一整池为空，塞最弱的非 Boss 怪避免秘境空刷
        if (list.Count == 0 && _npcCache!.Count > 0)
        {
            var weakest = _npcCache!.Where(c => !c.Boss)
                .OrderBy(c => EnemyPower(c.LifeMax, c.Damage, c.Defense)).Take(8).ToList();
            foreach (var e in weakest) list.Add(e.Type);
        }
        return list;
    }

    /// <summary>Fisher–Yates 洗牌（随机化刷怪池顺序）。</summary>
    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Main.rand.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// 调试输出（/loot riftinfo）：当前层上限、缓存规模、刷怪池内容（类型 ID + 名称 + 基础值 + 对数强度分 + 当前权重）、
    /// 关底 Boss —— 用于和游戏内实际数据对比，排查"刷怪异常/超级 Boss 混入"等问题。
    /// 数值读自缓存（基础空间），与池筛选/刷怪实测完全同空间。
    /// </summary>
    public static string DebugDump()
    {
        EnsureCache();
        var sb = new System.Text.StringBuilder();
        int lvl = Math.Max(1, CurrentLevel);
        sb.AppendLine($"Rift: level={CurrentLevel}, cap={MaxEnemyPower(lvl):0.##}, cacheNpcs={_npcCache!.Count}");
        sb.AppendLine($"MinionPool({_minionPool.Count}):");
        foreach (var t in _minionPool)
        {
            var c = _npcCache!.FirstOrDefault(e => e.Type == t);
            if (c.Type == 0) { sb.AppendLine($"  {t}:{Lang.GetNPCNameValue(t)} (not in cache)"); continue; }
            double p = EnemyPower(c.LifeMax, c.Damage, c.Defense);
            sb.AppendLine($"  {t}:{Lang.GetNPCNameValue(t)} base(life={c.LifeMax},dmg={c.Damage},def={c.Defense},boss={c.Boss}) power={p:0.##} w={(_spawnWeights.TryGetValue(t, out int wv) ? wv : 0)}");
        }
        if (_bossType > 0)
        {
            var c = _npcCache!.FirstOrDefault(e => e.Type == _bossType);
            if (c.Type != 0)
                sb.AppendLine($"Boss: {_bossType}:{Lang.GetNPCNameValue(_bossType)} base(life={c.LifeMax},dmg={c.Damage},def={c.Defense}) power={EnemyPower(c.LifeMax, c.Damage, c.Defense):0.##}");
            else
                sb.AppendLine($"Boss: {_bossType}:{Lang.GetNPCNameValue(_bossType)} (not in cache)");
        }
        else
        {
            sb.AppendLine("Boss: none (本层无符合强度的关底)");
        }
        // 诊断：缓存中最弱的 10 只（看弱怪是否在缓存、强度分多少），以及绿史莱姆(type 1)是否在缓存
        sb.AppendLine("WeakestCache(10):");
        var weakest = _npcCache!.Where(c => !c.Boss)
            .OrderBy(c => EnemyPower(c.LifeMax, c.Damage, c.Defense)).Take(10).ToList();
        foreach (var c in weakest)
            sb.AppendLine($"  {c.Type}:{Lang.GetNPCNameValue(c.Type)} (life={c.LifeMax},dmg={c.Damage},def={c.Defense}) power={EnemyPower(c.LifeMax, c.Damage, c.Defense):0.##}");
        sb.AppendLine("GreenSlime(type1) in cache: " + _npcCache!.Any(c => c.Type == 1));
        return sb.ToString();
    }

    // ===== 顶栏进度条（自绘，不占原版入侵字段，避免与哥布林入侵挂钩）=====

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int idx = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
        if (idx < 0) idx = layers.Count - 1;
        layers.Insert(idx, new LegacyGameInterfaceLayer("Looteria: Rift Bar", () =>
        {
            if (RiftActive) DrawRiftBar();
            return true;
        }, InterfaceScaleType.UI));
    }

    private static void DrawRiftBar()
    {
        var sb = Main.spriteBatch;
        var px = TextureAssets.MagicPixel.Value;
        float w = 420f, h = 16f;
        float x = Main.screenWidth / 2f - w / 2f;
        float y = 56f;
        sb.Draw(px, new Rectangle((int)x, (int)y, (int)w, (int)h), new Color(10, 12, 20) * 0.9f);
        float f = Math.Clamp(Progress / (float)ProgressMax, 0f, 1f);
        if (f > 0)
            sb.Draw(px, new Rectangle((int)x, (int)y, (int)(w * f), (int)h), new Color(80, 200, 255));
        sb.Draw(px, new Rectangle((int)x, (int)y, (int)w, 1), Color.White * 0.6f);

        int secs = Math.Max(0, TimerTicks) / 60;
        // 不显示波次（刷怪海下波数飞增，看着像异常；WaveIndex 仍保留用于内部逻辑/网络同步）
        string txt = Language.GetTextValue("Mods.Looteria.UI.RiftBarText", CurrentLevel, Progress, ProgressMax, secs / 60, secs % 60);
        var font = FontAssets.MouseText.Value;
        var sz = font.MeasureString(txt);
        Utils.DrawBorderStringFourWay(sb, font, txt, x + w / 2f - sz.X / 2f, y - 22f, Color.White, Color.Black, Vector2.Zero);
    }

    // ===== 网络 / 生命周期 =====

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write(CurrentLevel);
        writer.Write(BestLevel);
        writer.Write(Progress);
        writer.Write(TimerTicks);
        writer.Write(WaveIndex);
        writer.Write(_bossType);
    }

    public override void NetReceive(BinaryReader reader)
    {
        // 读取钳制（防御畸形包）
        CurrentLevel = Math.Clamp(reader.ReadInt32(), 0, NetCaps.MaxRiftLevel);
        BestLevel = Math.Clamp(reader.ReadInt32(), 0, NetCaps.MaxRiftLevel);
        Progress = Math.Max(0, reader.ReadInt32());
        TimerTicks = Math.Max(0, reader.ReadInt32());
        WaveIndex = Math.Max(0, reader.ReadInt32());
        _bossType = Math.Max(0, reader.ReadInt32());
    }

    public override void SaveWorldData(TagCompound tag)
    {
        if (BestLevel > 0) tag["riftBestLevel"] = BestLevel;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        // 通过层数持久化：换世界/重进游戏也保留
        BestLevel = Math.Clamp(tag.GetInt("riftBestLevel"), 0, NetCaps.MaxRiftLevel);
    }

    public override void OnWorldUnload()
    {
        CurrentLevel = 0;
        BestLevel = 0;
        Progress = 0;
        TimerTicks = 0;
        _bossSpawned = false;
        _completed = false;
        _minionPool.Clear();
        _spawnWeights.Clear();
        _npcCache = null;
        _capBase = -1;
        _participants.Clear();
        _initiator = -1;
        _pushCooldown = 0;
        _lastPushedProgress = int.MinValue;
        _lastPushedTimer = int.MinValue;
    }

    /// <summary>Mod Reload 时清易变静态状态（缓存/池/权重/进行中的秘境）。
    /// BestLevel 不在此清（世界未卸载，且已由 SaveWorldData 持久化）。</summary>
    public static void ResetStaticData()
    {
        CurrentLevel = 0;
        Progress = 0;
        TimerTicks = 0;
        _bossSpawned = false;
        _completed = false;
        _minionPool.Clear();
        _spawnWeights.Clear();
        _npcCache = null;
        _capBase = -1;
        _participants.Clear();
        _initiator = -1;
        _pushCooldown = 0;
        _lastPushedProgress = int.MinValue;
        _lastPushedTimer = int.MinValue;
    }
}
