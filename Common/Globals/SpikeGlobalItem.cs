using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace Looteria.Common.Globals;

/// <summary>
/// Phase 0 侦察 spike（计划 §3 Phase 0）：
/// 验证 InstancePerEntity GlobalItem 的持久化契约 ——
///   ① 玩家背包/装备栏物品：SaveData/LoadData 是否往返；
///   ② 世界箱子内物品：同上；
///   ③ OnSpawn 的 IEntitySource 能否捕获 NPC 掉落（EntitySource_Loot）与开箱来源；
///   ④ GlobalItem.OnHitNPC（近战）是否触发。
/// 结论写入工程根《spike-结论.md》。
/// </summary>
public class SpikeGlobalItem : GlobalItem
{
    /// <summary>每物品实例一个计数器，验证 per-entity 数据。</summary>
    public int SpikeValue;

    public override bool InstancePerEntity => true;

    public override bool AppliesToEntity(Item entity, bool lateInstantiation)
    {
        // spike 阶段：对所有非空且非堆叠物品生效即可。
        return entity != null && !entity.IsAir && entity.maxStack == 1;
    }

    public override void LoadData(Item item, TagCompound tag)
    {
        SpikeValue = tag.GetInt("spike");
    }

    public override void SaveData(Item item, TagCompound tag)
    {
        if (SpikeValue != 0)
        {
            tag["spike"] = SpikeValue;
        }
    }

    public override void NetSend(Item item, BinaryWriter writer)
    {
        writer.Write(SpikeValue);
    }

    public override void NetReceive(Item item, BinaryReader reader)
    {
        SpikeValue = reader.ReadInt32();
    }

    public override void OnSpawn(Item item, IEntitySource source)
    {
        // ③ 打印来源类型，确认能否识别掉落来源。
        // 服务端/单机打印到日志；不刷屏（只在带 spike 标记的物品上打印）。
        if (SpikeValue > 0)
        {
            string srcName = source?.GetType().FullName ?? "null";
            Mod.Logger.Info($"[SpikeGlobalItem] OnSpawn source = {srcName}");
        }
    }

    public override void OnHitNPC(Item item, Player player, NPC target, NPC.HitInfo hit, int damageDone)
    {
        // ④ 验证近战命中入口（仅测试用，命中 1 次打印 1 条）。
        if (SpikeValue > 0)
        {
            Mod.Logger.Info($"[SpikeGlobalItem] GlobalItem.OnHitNPC fired, dmg={damageDone}");
            SpikeValue = 0; // 打印一次后清标记，避免刷屏
        }
    }

    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (SpikeValue > 0)
        {
            tooltips.Add(new TooltipLine(Mod, "SpikeValue", $"[spike] {SpikeValue}"));
        }
    }
}
