using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.Effects;

/// <summary>
/// 传说之力运行时派发（10 条）。零新增内容：直接 StrikeNPC + 粒子/音效。
/// 近战走 AffixGlobalItem.OnHitNPC；弹幕走 AffixGlobalProjectile.OnHitNPC（M10 缓存 id）。
/// M8（审计 r3 模型）：OnHitNPC 只在"命中的客户端"触发（GlobalItem.cs:579 / GlobalProjectile.cs:243），
/// 传说伤害在攻击者客户端结算后配对 NetMessage.SendStrikeNPC 同步给服务端（客户端权威、与原版武器同通道），
/// 不存在双端重复执行；服务端只处理 M9 天雷 / Despawn 清场的 SimpleStrikeNPC 广播路径。
/// </summary>
public static class LegendaryPowerHandler
{
    /// <summary>R5：命中效果请求限速（60 tick/玩家）。防止恶意客户端刷 HitEffectRequest 获得无冷却 AoE。</summary>
    private static readonly Dictionary<int, int> _lastEffectTick = new();

    public static void OnMeleeHit(Player player, NPC target, AffixGlobalItem g, int damageDone)
        => Dispatch(player, target, null, (LegendaryPowerId)g.LegendaryPowerId, damageDone);

    public static void OnProjectileHit(Player player, NPC target, Projectile proj, AffixGlobalItem g, int damageDone)
        => Dispatch(player, target, proj, (LegendaryPowerId)g.LegendaryPowerId, damageDone);

    /// <summary>弹幕侧按缓存 id 分发（M10）。</summary>
    public static void OnProjectileHitById(Player player, NPC target, Projectile proj, int powerId, int damageDone)
        => Dispatch(player, target, proj, (LegendaryPowerId)powerId, damageDone);

    private static void Dispatch(Player player, NPC target, Projectile? proj, LegendaryPowerId id, int damageDone)
    {
        switch (id)
        {
            case LegendaryPowerId.ChainLightning:
                if (Main.rand.NextFloat() < 0.3f) ChainLightning(player, target, damageDone);
                break;
            case LegendaryPowerId.Burn:
                target.AddBuff(BuffID.OnFire, 240);
                break;
            case LegendaryPowerId.LifeSteal:
                HealPct(player, damageDone, 0.03f);
                break;
            case LegendaryPowerId.Execution:
                if (target.life <= 0) Explode(player, target, damageDone);
                break;
            case LegendaryPowerId.Cleave:
                Splash(player, target, damageDone, 120f, 0.75f);
                break;
            case LegendaryPowerId.Split:
                if (proj != null && Main.rand.NextFloat() < 0.2f) Split(player, proj, target, damageDone);
                break;
        }
    }

    // ===== 各传说之力实现 =====

    /// <summary>1 连锁闪电：向最近 2 敌各 50% 伤害。</summary>
    private static void ChainLightning(Player player, NPC origin, int dmg)
    {
        var targets = NearestEnemies(origin.Center, 600f, 2, origin);
        int dmg2 = Math.Max(1, (int)(dmg * 0.5f));
        foreach (var t in targets) Strike(player, t, dmg2, 0f);
        SoundEngine.PlaySound(SoundID.Item93, origin.Center);
        foreach (var t in targets)
            for (int i = 0; i < 4; i++)
                Dust.NewDustPerfect(Vector2.Lerp(origin.Center, t.Center, Main.rand.NextFloat()), DustID.Electric,
                    Vector2.Zero, 100, default, 1.2f);
    }

    /// <summary>4 处刑：击杀时 160px 内 100% 爆炸伤害。</summary>
    private static void Explode(Player player, NPC origin, int dmg)
    {
        foreach (var t in NearestEnemies(origin.Center, 160f, 8, origin))
            Strike(player, t, dmg, 2f);
        SoundEngine.PlaySound(SoundID.Item14, origin.Center);
        for (int i = 0; i < 16; i++)
            Dust.NewDust(origin.position, origin.width, origin.height, DustID.Torch,
                Main.rand.NextFloat(-4, 4), Main.rand.NextFloat(-4, 4));
    }

    /// <summary>9 顺劈：120px 内 75% 溅射。</summary>
    private static void Splash(Player player, NPC origin, int dmg, float radius, float factor)
    {
        foreach (var t in NearestEnemies(origin.Center, radius, 6, origin))
            Strike(player, t, Math.Max(1, (int)(dmg * factor)), 1f);
        for (int i = 0; i < 6; i++)
            Dust.NewDust(origin.position, origin.width, origin.height, DustID.Smoke,
                Main.rand.NextFloat(-3, 3), Main.rand.NextFloat(-3, 3));
    }

    /// <summary>6 分裂：20% 从命中点向最近 2 敌各射 1 枚同款弹幕（60% 伤害）。</summary>
    private static void Split(Player player, Projectile proj, NPC target, int dmg)
    {
        foreach (var t in NearestEnemies(target.Center, 500f, 2, target))
        {
            var dir = t.Center - target.Center;
            if (dir == Vector2.Zero) dir = new Vector2(Main.rand.NextFloat(-1, 1), Main.rand.NextFloat(-1, 1));
            dir.Normalize();
            Projectile.NewProjectile(player.GetSource_FromThis(), target.Center, dir * 8f,
                proj.type, Math.Max(1, (int)(dmg * 0.6f)), proj.knockBack * 0.5f, proj.owner);
        }
    }

    private static void HealPct(Player player, int damageDone, float pct)
    {
        if (damageDone <= 0) return;
        int heal = (int)(damageDone * pct);
        if (heal > 0)
        {
            player.statLife += heal;
            player.HealEffect(heal);
        }
    }

    /// <summary>公开：直接对 NPC 造成伤害（客户端权威：Strike + SendStrikeNPC 同步）。</summary>
    public static void DealDamage(Player player, NPC target, int damage, float knockback = 0f)
        => Strike(player, target, damage, knockback);

    /// <summary>公开：天雷（7 天雷）。客户端触发走 Strike（+SendStrikeNPC 同步）；服务端 HitEffectRequest 走 ServerStrike。</summary>
    public static void LightningStrike(Player player, NPC target, int damage)
    {
        if (Main.netMode == NetmodeID.Server)
            ServerStrike(player, target, Math.Max(1, damage), 1f);
        else
            Strike(player, target, Math.Max(1, damage), 1f);
        SoundEngine.PlaySound(SoundID.Thunder, target.Center);
        for (int i = 0; i < 10; i++)
            Dust.NewDustPerfect(target.Center + new Vector2(Main.rand.NextFloat(-20, 20), Main.rand.NextFloat(-60, -10)),
                DustID.Electric, Vector2.Zero, 100, default, 1.4f);
    }

    /// <summary>公开：以 center 为圆心找最近敌对 NPC。</summary>
    public static NPC? FindNearestEnemy(Vector2 center, float radius, NPC? exclude = null)
    {
        NPC? best = null;
        float bestD = radius * radius;
        for (int k = 0; k < Main.npc.Length; k++)
        {
            var n = Main.npc[k];
            if (n == null || !n.active || n.friendly || n == exclude || n.immortal) continue;
            float d = Vector2.DistanceSquared(center, n.Center);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    /// <summary>M9：服务端命中效果请求（客户端提名 → 服务端复选目标 → 服务端落雷/打击）。</summary>
    public static void HandleEffectRequest(int requester, byte effectId, ushort npcIndex, int damageDone)
    {
        if (Main.netMode != NetmodeID.Server) return;
        if (!Looteria.TryGetLooteriaPlayer(requester, out var lp)) return;
        // R5：限速 60 tick/玩家，超频静默拒绝并计数日志
        int now = (int)(Main.GameUpdateCount % int.MaxValue);
        if (_lastEffectTick.TryGetValue(requester, out int last) && now - last < 60)
        {
            Looteria.Instance?.Logger.Info($"[Looteria] HitEffectRequest rate-limited (player {requester}).");
            return;
        }
        _lastEffectTick[requester] = now;
        var player = lp.Player;

        switch ((LegendaryPowerId)effectId)
        {
            case LegendaryPowerId.SkyThunder:
            {
                // 目标由服务端以"请求者"为圆心复选（原 TrySkyThunder 读 MouseWorld，服务端无鼠标）
                var target = FindNearestEnemy(player.Center, 500f);
                if (target != null)
                {
                    int dmg = (int)player.GetTotalDamage(DamageClass.Generic).ApplyTo(50f);
                    LightningStrike(player, target, dmg);
                }
                break;
            }
            default:
            {
                if (npcIndex < Main.npc.Length && Main.npc[npcIndex] is { active: true, friendly: false } n
                    && Vector2.DistanceSquared(player.Center, n.Center) <= 800f * 800f
                    && damageDone is > 0 and <= 100000)
                {
                    ServerStrike(player, n, damageDone, 0f); // 服务端执行 → SimpleStrikeNPC 广播
                }
                break;
            }
        }
    }

    /// <summary>
    /// 统一打击出口（M8，审计 r3 方案）：
    /// OnHitNPC 只在"命中的客户端"触发（GlobalItem.cs:579 / GlobalProjectile.cs:243），
    /// 所以传说伤害在攻击者客户端结算后，必须配对 NetMessage.SendStrikeNPC 把本次命中同步给服务端
    /// （StrikeNPC(HitInfo) 重载不自动发包，NPC.cs.patch:2576-2582；注释"内部同步"是错误认知）。
    /// 服务端直接执行的路径（M9 天雷 / DespawnRiftNpcs 清场）用 SimpleStrikeNPC（自动广播）。
    /// </summary>
    private static void Strike(Player player, NPC target, int damage, float knockback)
    {
        if (target == null || !target.active || target.friendly || target.immortal) return;
        var info = new NPC.HitInfo
        {
            Damage = Math.Max(1, damage),
            Knockback = knockback,
            HitDirection = target.Center.X > player.Center.X ? 1 : -1,
            Crit = false,
            DamageType = DamageClass.Generic,
            SourceDamage = Math.Max(1, damage)
        };
        target.StrikeNPC(info);
        if (Main.netMode != NetmodeID.SinglePlayer)
            NetMessage.SendStrikeNPC(target, in info); // 把本次命中同步给服务端（M8）
    }

    /// <summary>服务端统一打击出口（仅服务端调用：M9 天雷 / Despawn 清场）：
    /// SimpleStrikeNPC 自动广播伤害数字（NPC.cs.patch:2549-2554）。</summary>
    private static void ServerStrike(Player player, NPC target, int damage, float knockback)
    {
        if (target == null || !target.active || target.friendly || target.immortal) return;
        target.SimpleStrikeNPC(Math.Max(1, damage),
            target.Center.X > player.Center.X ? 1 : -1,
            crit: false, knockBack: knockback, damageType: DamageClass.Generic);
    }

    /// <summary>以 center 为圆心取最近 max 个敌对 NPC（排除 exclude）。</summary>
    public static List<NPC> NearestEnemies(Vector2 center, float radius, int max, NPC? exclude)
    {
        var found = new List<NPC>();
        float r2 = radius * radius;
        for (int k = 0; k < Main.npc.Length; k++)
        {
            var n = Main.npc[k];
            if (n == null || !n.active || n.friendly || n == exclude || n.immortal) continue;
            if (Vector2.DistanceSquared(center, n.Center) > r2) continue;
            found.Add(n);
        }
        found.Sort((a, b) => Vector2.DistanceSquared(center, a.Center).CompareTo(Vector2.DistanceSquared(center, b.Center)));
        if (found.Count > max) found.RemoveRange(max, found.Count - max);
        return found;
    }
}
