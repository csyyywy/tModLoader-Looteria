using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using global::Looteria.Common.Data;
using global::Looteria.Common.Effects;
using global::Looteria.Common.Globals;

namespace Looteria.Common.Players;

/// <summary>
/// 玩家扩展：货币（血岩/重铸之尘）、宝石库存、被动词缀汇总、力量等级缓存。
/// 持久化：ModPlayer.SaveData/LoadData；多人：SyncPlayer/SendClientChanges（Phase 5 完善）。
/// </summary>
public class LooteriaPlayer : ModPlayer
{
    public int BloodShards;
    public int Dust;

    /// <summary>抽奖存储容器（最多 200 格，物品形式，随存档持久化）。</summary>
    public List<Item> GambleContainer = new();

    // —— 被动汇总（每 tick 由 UpdateEquip 累加，ResetEffects 清零）——
    public float PassiveLifeOnHit;
    public int PassiveManaOnHit;
    public float PassiveCritDamage;

    /// <summary>掠夺之力（装备力量等级合计）。</summary>
    public int GearPower;

    private int _thunderTimer;

    public override void ResetEffects()
    {
        PassiveLifeOnHit = 0;
        PassiveManaOnHit = 0;
        PassiveCritDamage = 0;
        GearPower = 0;
    }

    /// <summary>装备结算后：套装加成 + 力量等级 + 天雷计时。</summary>
    public override void PostUpdateEquips()
    {
        SetBonusHandler.Apply(Player);
        ComputeGearPower();
        // L14：首帧预构建赌博候选池（避免首次赌博顿卡）；幂等
        Common.Roll.LootGenerator.WarmCache();

        _thunderTimer++;
        if (_thunderTimer >= 180) // 每 3 秒
        {
            _thunderTimer = 0;
            TrySkyThunder();
        }
    }

    /// <summary>7 天雷：持有传说武器时，客户端计时到点 → 发 HitEffectRequest 让服务端复选目标落雷（M9：服务端不读鼠标）。</summary>
    private void TrySkyThunder()
    {
        if (Player.whoAmI != Main.myPlayer) return; // R3：PostUpdateEquips 对每台机器的每个玩家实例都跑——只让本机玩家触发
        if (!Player.HeldItem.TryGetGlobalItem(out AffixGlobalItem g)) return;
        if ((LegendaryPowerId)g.LegendaryPowerId != LegendaryPowerId.SkyThunder) return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            // 多人：客户端只提名，服务端执行（避免本地落雷 + 服务端落雷双劈）
            var p = Mod.GetPacket();
            p.Write((byte)LootMsg.HitEffectRequest);
            p.Write((byte)LegendaryPowerId.SkyThunder);
            p.Write((ushort)0xFFFF); // 服务端就近选目标
            p.Write(0);
            p.Send();
            return;
        }
        // R3（复审修正）：主机（listen server，netMode==Server 且是本机玩家）也允许本地落雷——
        // LightningStrike 内部已有 netMode==Server → ServerStrike(SimpleStrikeNPC 自动广播) 分支，权威且不双劈。
        bool isLocalHost = Main.netMode == NetmodeID.Server && Player.whoAmI == Main.myPlayer;
        if (Main.netMode != NetmodeID.SinglePlayer && !isLocalHost) return; // 服务端远端玩家实例不直执
        var target = LegendaryPowerHandler.FindNearestEnemy(Player.Center, 500f);
        if (target == null) return;
        int dmg = (int)Player.GetTotalDamage(DamageClass.Generic).ApplyTo(50f);
        LegendaryPowerHandler.LightningStrike(Player, target, dmg);
    }

    /// <summary>8 荆棘：受击时反弹 50% 伤害给周围敌人。</summary>
    public override void OnHurt(Player.HurtInfo info)
    {
        bool hasThorns = false;
        // M7：只扫描真实装备槽（0..9），时装位触发荆棘反伤属作弊
        for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < Player.armor.Length; i++)
        {
            var item = Player.armor[i];
            if (item.TryGetGlobalItem(out AffixGlobalItem g)
                && (LegendaryPowerId)g.LegendaryPowerId == LegendaryPowerId.Thorns) { hasThorns = true; break; }
        }
        if (!hasThorns) return;
        int reflect = (int)(info.Damage * 0.5f);
        foreach (var n in LegendaryPowerHandler.NearestEnemies(Player.Center, 120f, 8, null))
            LegendaryPowerHandler.DealDamage(Player, n, reflect);
        SoundEngine.PlaySound(SoundID.NPCDeath1, Player.Center);
    }

    /// <summary>计算掠夺之力（护甲+饰品+手持武器）。</summary>
    private void ComputeGearPower()
    {
        int sum = 0;
        // M7：只扫描真实装备槽（0..9），时装位不算 GearPower（否则可白嫖秘境门槛）
        for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < Player.armor.Length; i++)
        {
            if (Player.armor[i].TryGetGlobalItem(out AffixGlobalItem g))
                sum += g.PowerScore;
        }
        if (Player.HeldItem.TryGetGlobalItem(out AffixGlobalItem held))
            sum += held.PowerScore;
        GearPower = sum;
    }

    /// <summary>热键：开/关掠夺面板 + 角色属性面板。</summary>
    public override void ProcessTriggers(TriggersSet triggersSet)
    {
        if (Common.Systems.UISystem.PanelKeybind.JustPressed)
        {
            if (Common.Systems.UISystem.PanelOpen)
            {
                IngameFancyUI.Close();
                Common.Systems.UISystem.PanelOpen = false;
            }
            else
            {
                // 互斥：打开掠夺面板前关闭角色面板
                if (Common.Systems.UISystem.CharSheetOpen)
                {
                    IngameFancyUI.Close();
                    Common.Systems.UISystem.CharSheetOpen = false;
                }
                IngameFancyUI.OpenUIState(Common.UI.LooteriaUIState.Instance);
                Common.Systems.UISystem.PanelOpen = true;
            }
        }
        else if (Common.Systems.UISystem.CharSheetKeybind.JustPressed)
        {
            if (Common.Systems.UISystem.CharSheetOpen)
            {
                IngameFancyUI.Close();
                Common.Systems.UISystem.CharSheetOpen = false;
            }
            else
            {
                // 互斥：打开角色面板前关闭掠夺面板
                if (Common.Systems.UISystem.PanelOpen)
                {
                    IngameFancyUI.Close();
                    Common.Systems.UISystem.PanelOpen = false;
                }
                IngameFancyUI.OpenUIState(Common.UI.CharacterSheetUI.Instance);
                Common.Systems.UISystem.CharSheetOpen = true;
            }
        }
    }

    public override void SaveData(TagCompound tag)
    {
        tag["shards"] = BloodShards;
        tag["dust"] = Dust;
        if (GambleContainer.Count > 0)
        {
            var list = new List<TagCompound>();
            foreach (var it in GambleContainer)
            {
                if (it != null && !it.IsAir) list.Add(ItemIO.Save(it));
            }
            if (list.Count > 0) tag["gc"] = list;
        }
    }

    public override void LoadData(TagCompound tag)
    {
        BloodShards = tag.GetInt("shards");
        Dust = tag.GetInt("dust");
        GambleContainer = new List<Item>();
        var gl = tag.GetList<TagCompound>("gc");
        if (gl != null)
        {
            foreach (var t in gl)
            {
                var it = ItemIO.Load(t);
                if (!it.IsAir) GambleContainer.Add(it);
            }
        }
    }

    public override void SyncPlayer(int toWho, int fromWho, bool newPlayer)
    {
        var p = Mod.GetPacket();
        p.Write((byte)LootMsg.PlayerFullSync);
        p.Write((byte)Player.whoAmI); // 身份必须入包体：中继到其他客户端后 whoAmI 已不可用（Mod.Hooks.cs:88）
        WritePlayerData(p);
        // R1：加入时（newPlayer=true）把掠夺容器一并上行（非 SSC 服务器容器从空开始，需采纳客户端本地容器，
        // 否则第一次容器操作会把已有物品整体替换掉）。中继（newPlayer=false）不带容器：其余客户端不需要别人的容器。
        if (newPlayer)
        {
            int n = Math.Min(GambleContainer.Count, 255);
            p.Write((byte)n);
            for (int i = 0; i < n; i++)
                if (GambleContainer[i] != null && !GambleContainer[i].IsAir)
                    ItemIO.Send(GambleContainer[i], p, writeStack: true);
                else
                    ItemIO.Send(new Item(), p, writeStack: true); // 占位，保持流对齐
        }
        p.Send(toWho, fromWho);       // 参数按原样透传
    }

    public override void CopyClientState(ModPlayer targetCopy)
    {
        var t = (LooteriaPlayer)targetCopy;
        t.BloodShards = BloodShards;
        t.Dust = Dust;
        t.GearPower = GearPower; // 快照仍含货币：用于发现"本地被非法改动"，但绝不上行货币
    }

    public override void SendClientChanges(ModPlayer clientPlayer)
    {
        var old = (LooteriaPlayer)clientPlayer;
        // ⚠️ 修复 H5c：只上行 GearPower（装备派生值）。货币是服务端权威，永远不上行，
        //    否则客户端旧值会反向覆盖服务端命令/击杀记账（SendClientChanges 触发即回滚）。
        if (old.GearPower == GearPower) return;
        var p = Mod.GetPacket();
        p.Write((byte)LootMsg.GearPowerUpdate);
        p.Write(Math.Clamp(GearPower, 0, NetCaps.MaxGearPower));
        p.Send();
    }

    private void WritePlayerData(BinaryWriter w)
    {
        w.Write(BloodShards);
        w.Write(Dust);
        w.Write(GearPower);
    }

    /// <summary>血岩入账（已含倍率，调用方负责计算）。</summary>
    public void AddBloodShards(int amount)
    {
        if (amount <= 0) return;
        BloodShards += amount;
    }

    /// <summary>重铸之尘入账。</summary>
    public void AddDust(int amount)
    {
        if (amount <= 0) return;
        Dust += amount;
    }
}
