using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Effects;

namespace Looteria.Common.Globals;

/// <summary>
/// 弹幕侧传说之力：5 穿透（生成时）+ 命中分发（连锁/灼烧/嗜血/处刑/分裂）。
/// M10：生成瞬间把持有武器的传说之力 id 固化进弹幕实例（射后切枪不再错配/失效），
/// 经 SendExtraAI/ReceiveExtraAI 随弹幕同步（多人两端一致）。
/// M8（审计 r3 模型）：OnHitNPC 只在"命中的客户端"触发（GlobalProjectile.cs:243），
/// 由 LegendaryPowerHandler.Strike 内部配对 SendStrikeNPC 同步服务端——不存在双端重复执行。
/// </summary>
public class AffixGlobalProjectile : GlobalProjectile
{
    /// <summary>生成瞬间持有武器的传说之力 id（-1=无）。M10：命中时不再实时读 HeldItem。</summary>
    public int CachedLegendaryPowerId = -1;

    public override bool InstancePerEntity => true;

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (!projectile.friendly || projectile.owner < 0 || projectile.owner >= Main.maxPlayers) return;
        var player = Main.player[projectile.owner];
        if (player == null || !player.active) return;
        if (player.HeldItem.TryGetGlobalItem(out AffixGlobalItem g))
        {
            CachedLegendaryPowerId = g.LegendaryPowerId; // M10：固化
            if ((LegendaryPowerId)g.LegendaryPowerId == LegendaryPowerId.Pierce
                && projectile.penetrate >= 0)
            {
                projectile.penetrate += 1;
            }
        }
    }

    public override void OnHitNPC(Projectile projectile, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (!projectile.friendly || projectile.owner < 0 || projectile.owner >= Main.maxPlayers) return;
        if (CachedLegendaryPowerId <= 0) return;
        // M8（审计 r3）：OnHitNPC 只在命中的客户端触发（GlobalProjectile.cs:243），
        // 由 LegendaryPowerHandler.Strike 内部配对 SendStrikeNPC 同步服务端。
        var player = Main.player[projectile.owner];
        if (player == null || !player.active) return;
        LegendaryPowerHandler.OnProjectileHitById(player, target, projectile, CachedLegendaryPowerId, damageDone);
    }

    // 随弹幕同步（签名核实：GlobalProjectile.cs:81/93；BitWriter/BitReader 在 Terraria.ModLoader.IO）
    public override void SendExtraAI(Projectile projectile, Terraria.ModLoader.IO.BitWriter bitWriter,
        BinaryWriter binaryWriter)
        => binaryWriter.Write(CachedLegendaryPowerId);

    public override void ReceiveExtraAI(Projectile projectile, Terraria.ModLoader.IO.BitReader bitReader,
        BinaryReader binaryReader)
        => CachedLegendaryPowerId = binaryReader.ReadInt32();
}
