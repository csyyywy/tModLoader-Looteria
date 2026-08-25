using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Systems;

namespace Looteria.Common.Globals;

/// <summary>
/// 敌人词缀系统（类 D2/POE 精英词缀）：
/// - 词缀在**刷出后**（OnSpawn）由服务端掷取——不在秘境强度分筛选内（用户决策：惊喜感，精英照常进池）。
/// - 野外 + 秘境怪都吃词缀（秘境层缩放之上叠加）。
/// - Boss 吃普通词缀 + 大量 Boss 专属强化词缀。
/// - 属性在服务端 OnSpawn 应用并记录倍率（LifeMult/DamageMult），随 SyncNPC 下行；
///   客户端经 NetSend/NetReceive 收词缀列表用于名字前缀/染色/减伤计算。
/// - 所有伤害/特效/生成钩子带 `if (Main.netMode == MultiplayerClient) return;` 服务端守卫（6.5 铁律）。
/// </summary>
public class EnemyAffixGlobalNPC : GlobalNPC
{
    /// <summary>词缀稀有度（None=无词缀）。</summary>
    public EnemyAffixRarity Rarity;
    /// <summary>已掷取的词缀列表（服务端掷取，NetSend 下行）。</summary>
    public List<EnemyAffixId> Affixes = new();

    /// <summary>词缀引起的生命乘数（供秘境防线还原预期值）。</summary>
    public float LifeMult = 1f;
    /// <summary>词缀引起的伤害乘数（供秘境防线还原预期值）。</summary>
    public float DamageMult = 1f;

    /// <summary>SetDefaults 空间的基础生命（供死亡分裂/召唤比例计算）。</summary>
    public int BaseLifeMax;
    /// <summary>SetDefaults 空间的基础伤害（供死亡分裂比例计算）。</summary>
    public int BaseDamage;

    private int _stormTimer;
    private int _regenTick;
    private bool _furyActive;

    public override bool InstancePerEntity => true;

    public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        => entity != null && !entity.friendly && entity.damage > 0;

    public bool HasAffixes => Affixes is { Count: > 0 };

    public static bool IsEnabled
    {
        get
        {
            var cfg = EnemyAffixConfig.Instance;
            return cfg != null && cfg.Enable;
        }
    }

    /// <summary>该 NPC 是否有词缀（静态便利：GetGlobalNPC 可能失败时用）。</summary>
    public static bool HasAnyAffix(NPC npc)
        => npc.TryGetGlobalNPC(out EnemyAffixGlobalNPC g) && g.HasAffixes;

    // ===== 掷取与应用（服务端 / 单机）=====

    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (!IsEnabled) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return; // 只服务端掷，客户端随包收

        // 硬排除：地牢守卫（原版机制怪，绝不附加词缀——与 RiftSystem 池排除口径一致）
        if (npc.type == NPCID.DungeonGuardian) return;

        var cfg = EnemyAffixConfig.Instance!;
        if (cfg.IsExcluded(npc)) return;

        bool isBoss = npc.boss || npc.GetGlobalNPC<LootGlobalNPC>() is { IsRiftBoss: true };

        // 普通词缀池（普通/精英/Boss 都吃）
        var commonPool = new List<EnemyAffixId>();
        commonPool.AddRange(EnemyAffixDatabase.CommonPool);
        if (isBoss)
        {
            // Boss 普通词缀 3~4 条
            int n = Main.rand.Next(cfg.BossAffixCountMin, cfg.BossAffixCountMax + 1);
            RollDistinct(commonPool, n);
        }
        else if (Main.rand.NextFloat() < cfg.CommonAffixChance)
        {
            // 普通怪 1 条普通词缀
            RollDistinct(commonPool, 1);
        }

        // 精英（非 Boss）：Champion 档词缀 2~3 条（普通池 + 精英专属池），效果翻倍
        bool isElite = !isBoss && Main.rand.NextFloat() < cfg.EliteChance;
        if (isElite)
        {
            var elitePool = new List<EnemyAffixId>();
            elitePool.AddRange(EnemyAffixDatabase.CommonPool);
            elitePool.AddRange(EnemyAffixDatabase.ChampionPool);
            int n = Main.rand.Next(cfg.EliteAffixCountMin, cfg.EliteAffixCountMax + 1);
            RollDistinct(elitePool, n);
            Rarity = EnemyAffixRarity.Champion;
        }

        // Boss 专属词缀 1~2 条（Boss 专属池）
        if (isBoss)
        {
            var bossPool = new List<EnemyAffixId>();
            bossPool.AddRange(EnemyAffixDatabase.BossExclusivePool);
            int n = Main.rand.Next(cfg.BossExclusiveCountMin, cfg.BossExclusiveCountMax + 1);
            RollDistinct(bossPool, n);
        }

        if (Affixes.Count == 0) return;

        if (Rarity == EnemyAffixRarity.None)
            Rarity = EnemyAffixRarity.Common; // 普通怪带词缀 = 普通档

        ApplyStats(npc);
    }

    /// <summary>从池中掷 n 条不重复词缀，写入 Affixes。</summary>
    private void RollDistinct(List<EnemyAffixId> pool, int n)
    {
        if (pool == null || pool.Count == 0 || n <= 0) return;
        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            int idx = Main.rand.Next(pool.Count);
            var id = pool[idx];
            pool.RemoveAt(idx);
            if (!Affixes.Contains(id)) Affixes.Add(id);
        }
    }

    /// <summary>测试专用：强制走掷取+应用（绕开 Main.netMode 守卫；无头服务端上下文非 Server 时用）。</summary>
    public void RollForTest(NPC npc)
    {
        Affixes.Clear();
        Rarity = EnemyAffixRarity.None;
        LifeMult = 1f;
        DamageMult = 1f;

        var cfg = EnemyAffixConfig.Instance!;
        bool isBoss = npc.boss;
        var commonPool = new List<EnemyAffixId>();
        commonPool.AddRange(EnemyAffixDatabase.CommonPool);
        if (isBoss)
            RollDistinct(commonPool, Main.rand.Next(cfg.BossAffixCountMin, cfg.BossAffixCountMax + 1));
        else if (Main.rand.NextFloat() < cfg.CommonAffixChance)
            RollDistinct(commonPool, 1);

        bool isElite = !isBoss && Main.rand.NextFloat() < cfg.EliteChance;
        if (isElite)
        {
            var elitePool = new List<EnemyAffixId>();
            elitePool.AddRange(EnemyAffixDatabase.CommonPool);
            elitePool.AddRange(EnemyAffixDatabase.ChampionPool);
            RollDistinct(elitePool, Main.rand.Next(cfg.EliteAffixCountMin, cfg.EliteAffixCountMax + 1));
            Rarity = EnemyAffixRarity.Champion;
        }

        if (isBoss)
        {
            var bossPool = new List<EnemyAffixId>();
            bossPool.AddRange(EnemyAffixDatabase.BossExclusivePool);
            RollDistinct(bossPool, Main.rand.Next(cfg.BossExclusiveCountMin, cfg.BossExclusiveCountMax + 1));
        }

        if (Affixes.Count == 0) return;
        if (Rarity == EnemyAffixRarity.None)
            Rarity = EnemyAffixRarity.Common;
        ApplyStats(npc);
    }

    /// <summary>应用词缀属性（lifeMax/life/damage/defense）。记录倍率供秘境防线还原。</summary>
    private void ApplyStats(NPC npc)
    {
        bool champion = Rarity == EnemyAffixRarity.Champion;

        // 生命乘数 = 各词缀乘数取最大（防御性乘数不叠加相乘，防止爆炸）
        float lifeMult = 1f;
        float dmgMult = 1f;
        float dr = 0f;   // 减伤%（玩家→NPC）
        int defBonus = 0;
        foreach (var id in Affixes)
        {
            lifeMult = Math.Max(lifeMult, EnemyAffixDatabase.LifeMultFor(id, champion));
            dmgMult = Math.Max(dmgMult, EnemyAffixDatabase.DamageMultFor(id, champion));
            dr += EnemyAffixDatabase.DamageReductionFor(id);
            defBonus += EnemyAffixDatabase.DefenseBonusFor(id);
        }
        dr = Math.Min(dr, 0.8f); // 减伤累加上限 80%（防多词缀叠加到无敌）

        BaseLifeMax = npc.lifeMax;
        BaseDamage = npc.damage;
        LifeMult = lifeMult;
        DamageMult = dmgMult;

        if (lifeMult > 1f)
        {
            npc.lifeMax = (int)(npc.lifeMax * lifeMult);
            npc.life = npc.lifeMax;
        }
        if (dmgMult > 1f)
            npc.damage = (int)(npc.damage * dmgMult);
        if (defBonus > 0)
            npc.defense += defBonus;
        _dr = dr; // 存实例供 ModifyIncomingHit 使用（避免每帧重算）

        // 多人：属性在服务端改完 → 请求同步
        if (Main.netMode == NetmodeID.Server)
            npc.netUpdate = true;
    }

    private float _dr;

    // ===== 玩家 → NPC 伤害：减伤词缀 =====

    public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers)
    {
        if (!HasAffixes || _dr <= 0f) return;
        // 各端同输入同输出：ModifyIncomingHit 会在命中结算的机器上跑；这里做纯乘算，幂等
        modifiers.FinalDamage *= 1f - _dr;
    }

    // ===== NPC → 玩家：减益 / 吸血 / 荆棘 =====

    public override void OnHitPlayer(NPC npc, Player target, Player.HurtInfo hurtInfo)
    {
        if (!HasAffixes) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return; // 服务端守卫（6.5）

        foreach (var id in Affixes)
        {
            int debuff = EnemyAffixDatabase.HitDebuffFor(id);
            if (debuff > 0)
                target.AddBuff(debuff, 240); // 4 秒
            int debuff2 = EnemyAffixDatabase.HitDebuff2For(id);
            if (debuff2 > 0)
                target.AddBuff(debuff2, 240);
        }

        // 吸血：攻击造成伤害的 % 回复自身（服务端改生命 + netUpdate）
        float ls = 0f;
        foreach (var id in Affixes)
            ls = Math.Max(ls, EnemyAffixDatabase.LifestealFor(id));
        if (ls > 0f)
        {
            int heal = (int)(hurtInfo.Damage * ls);
            if (heal > 0)
            {
                npc.life = Math.Min(npc.lifeMax, npc.life + heal);
                npc.netUpdate = true;
            }
        }
    }

    public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
    {
        if (!HasAffixes || player == null) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        float reflect = 0f;
        foreach (var id in Affixes)
            reflect = Math.Max(reflect, EnemyAffixDatabase.ThornsReflectFor(id));
        if (reflect > 0f)
        {
            int dmg = Math.Max(1, (int)(damageDone * reflect));
            player.Hurt(PlayerDeathReason.ByNPC(npc.netID), dmg, 0);
        }
    }

    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        if (!HasAffixes || projectile == null) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers) return;
        float reflect = 0f;
        foreach (var id in Affixes)
            reflect = Math.Max(reflect, EnemyAffixDatabase.ThornsReflectFor(id));
        if (reflect > 0f)
        {
            var p = Main.player[projectile.owner];
            if (p == null || !p.active) return;
            int dmg = Math.Max(1, (int)(damageDone * reflect));
            p.Hurt(PlayerDeathReason.ByNPC(npc.netID), dmg, 0);
        }
    }

    // ===== 持续效果（AI）：再生 / 迅捷 / 风暴之眼 / 狂怒二阶段 =====

    public override void AI(NPC npc)
    {
        if (!HasAffixes || npc.life <= 0) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        // 再生：每秒回复 1% 或 2%（Boss 不朽）最大生命（服务端）
        float regen = 0f;
        foreach (var id in Affixes)
            regen = Math.Max(regen, EnemyAffixDatabase.RegenPctFor(id));
        if (regen > 0f && ++_regenTick >= 60)
        {
            _regenTick = 0;
            int heal = Math.Max(1, (int)(npc.lifeMax * regen));
            npc.life = Math.Min(npc.lifeMax, npc.life + heal);
            npc.netUpdate = true;
        }

        // 迅捷：移速加成（AI 近似：每帧把 velocity 长度按比例放大，方向不变）
        float speed = 0f;
        foreach (var id in Affixes)
            speed = Math.Max(speed, EnemyAffixDatabase.SpeedMultFor(id));
        if (speed > 0f && npc.velocity.LengthSquared() > 0.0001f)
        {
            npc.velocity *= 1f + speed * 0.1f; // 每帧轻微加速（10 帧 ≈ +10%）
            // 钳制最大速度避免弹飞
            float max = 12f;
            if (npc.velocity.Length() > max)
                npc.velocity = Vector2.Normalize(npc.velocity) * max;
        }

        // 狂怒二阶段：生命 <30% 时伤害 ×2（Boss 专属；服务端改 + netUpdate）
        bool hasFury = false;
        foreach (var id in Affixes)
            if (EnemyAffixDatabase.HasFury(id)) { hasFury = true; break; }
        if (hasFury)
        {
            bool nowActive = npc.life < npc.lifeMax * 0.3f;
            if (nowActive && !_furyActive)
            {
                _furyActive = true;
                npc.damage = (int)(npc.damage * 2f);
                npc.netUpdate = true;
            }
            else if (!nowActive && _furyActive)
            {
                _furyActive = false;
                npc.damage = Math.Max(npc.damage / 2, 1);
                npc.netUpdate = true;
            }
        }

        // 风暴之眼：每 3 秒朝玩家发射 1 枚弹幕（服务端）
        int interval = 0;
        foreach (var id in Affixes)
            interval = Math.Max(interval, EnemyAffixDatabase.StormEyeIntervalFor(id));
        if (interval > 0)
        {
            _stormTimer++;
            if (_stormTimer >= interval)
            {
                _stormTimer = 0;
                var player = RiftSystem.PickAnchorForProjectile(); // 服务端锚点（参战者优先，退化为任意在线玩家）
                if (player != null && player.active)
                {
                    Vector2 dir = player.Center - npc.Center;
                    if (dir.LengthSquared() > 0.0001f)
                    {
                        dir.Normalize();
                        int dmg = Math.Max(1, npc.damage / 2);
                        int proj = Projectile.NewProjectile(
                            npc.GetSource_FromThis(),
                            npc.Center,
                            dir * 8f,
                            ProjectileID.Fireball,
                            dmg,
                            2f,
                            Main.myPlayer,
                            0f, 0f);
                        if (proj >= 0 && proj < Main.projectile.Length)
                        {
                            Main.projectile[proj].hostile = true;
                            Main.projectile[proj].friendly = false;
                        }
                    }
                }
            }
        }
    }

    // ===== 死亡：分裂 / 召唤 / 爆炸 =====

    public override void OnKill(NPC npc)
    {
        if (!HasAffixes) return;
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        foreach (var id in Affixes)
        {
            int count = EnemyAffixDatabase.SpawnCountFor(id);
            float ratio = EnemyAffixDatabase.SplitRatioFor(id);
            if (count > 0 && ratio > 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    int type = npc.type;
                    // 分裂/召唤：生成同类型小怪（比例生命）
                    int id2 = NPC.NewNPC(npc.GetSource_FromThis(), (int)npc.Center.X + Main.rand.Next(-30, 31),
                        (int)npc.Center.Y, type);
                    if (id2 >= 0 && id2 < Main.npc.Length)
                    {
                        var child = Main.npc[id2];
                        child.lifeMax = Math.Max(1, (int)(child.lifeMax * ratio));
                        child.life = child.lifeMax;
                        child.damage = Math.Max(1, (int)(child.damage * ratio));
                        child.netUpdate = true;
                    }
                }
            }

            float radius = EnemyAffixDatabase.ExplosionRadiusFor(id);
            if (radius > 0f)
            {
                // 对周围玩家造成伤害（爆炸）
                int dmg = Math.Max(1, npc.damage);
                for (int p = 0; p < Main.maxPlayers; p++)
                {
                    var pl = Main.player[p];
                    if (pl == null || !pl.active || pl.dead) continue;
                    if (Vector2.Distance(pl.Center, npc.Center) <= radius)
                        pl.Hurt(PlayerDeathReason.ByNPC(npc.netID), dmg, 0);
                }
            }
        }
    }

    // ===== 名字前缀 / 染色（客户端也执行，纯表现）=====

    public override void ModifyTypeName(NPC npc, ref string typeName)
    {
        if (!HasAffixes) return;
        var cfg = EnemyAffixConfig.Instance;
        if (cfg is { ShowAffixInName: false }) return;
        if (Affixes.Count == 0) return;

        // 取第一条词缀做前缀（Boss 显示 Boss 专属优先）
        EnemyAffixId first = Affixes[0];
        foreach (var id in Affixes)
            if (EnemyAffixDatabase.IsBossExclusive(id)) { first = id; break; }
        string affixName = Language.GetTextValue("Mods.Looteria.EnemyAffix." + EnemyAffixDatabase.Key(first));
        if (string.IsNullOrEmpty(affixName)) return;
        typeName = Language.GetTextValue("Mods.Looteria.EnemyAffix.Prefix", affixName, typeName);
    }

    public override void DrawEffects(NPC npc, ref Color drawColor)
    {
        if (!HasAffixes) return;
        switch (Rarity)
        {
            case EnemyAffixRarity.Champion:
                drawColor = Color.Lerp(drawColor, new Color(170, 80, 220), 0.35f);
                break;
            case EnemyAffixRarity.BossExclusive:
                drawColor = Color.Lerp(drawColor, new Color(230, 190, 60), 0.35f);
                break;
            case EnemyAffixRarity.Common:
                drawColor = Color.Lerp(drawColor, new Color(120, 200, 255), 0.25f);
                break;
        }
    }

    // ===== 多人同步（服务端 → 客户端）=====

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        binaryWriter.Write((byte)Rarity);
        int n = Affixes?.Count ?? 0;
        binaryWriter.Write((byte)n);
        if (n > 0 && Affixes != null)
            foreach (var id in Affixes)
                binaryWriter.Write((byte)id);
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
    {
        int r = binaryReader.ReadByte();
        Rarity = (EnemyAffixRarity)Math.Clamp(r, 0, (int)EnemyAffixRarity.BossExclusive);
        int n = Math.Clamp((int)binaryReader.ReadByte(), 0, 16);
        Affixes = new List<EnemyAffixId>(n);
        for (int i = 0; i < n; i++)
        {
            var id = (EnemyAffixId)binaryReader.ReadByte();
            if (!Affixes.Contains(id)) Affixes.Add(id);
        }
    }
}
