using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using global::Looteria.Common.Globals;

namespace Looteria.Commands;

/// <summary>
/// Phase 0 测试命令：
///   /spike set N   —— 给当前手持物品的 SpikeGlobalItem.SpikeValue 赋 N（配合存档验证）
///   /spike info    —— 打印手持物品的 SpikeValue 与资格
/// </summary>
public class SpikeCommand : ModCommand
{
    // H3+R4：测试命令用 Chat|Console（单机聊天可用）；多人聊天拒绝（防任意玩家改物品数据）。
    public override CommandType Type => CommandType.Chat | CommandType.Console;

    public override string Command => "spike";

    public override string Usage => "/spike set <N> | /spike info";

    public override string Description => "Looteria Phase 0 持久化 spike 测试命令";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Item held = caller.Player?.HeldItem!;
        if (caller.Player == null || held == null || held.IsAir)
        {
            caller.Reply("No item in hand (console has no player context).");
            return;
        }
        if (Main.netMode != NetmodeID.SinglePlayer)
        {
            caller.Reply("spike 命令仅单机/控制台可用。");
            return;
        }

        if (args.Length >= 1 && args[0] == "set" && args.Length >= 2 && int.TryParse(args[1], out int n))
        {
            if (held.TryGetGlobalItem(out SpikeGlobalItem spike))
            {
                spike.SpikeValue = n;
                caller.Reply($"SpikeValue = {n} set on {held.Name}.");
            }
            else
            {
                caller.Reply($"Item {held.Name} does not qualify (maxStack={held.maxStack}).");
            }
        }
        else if (args.Length >= 1 && args[0] == "info")
        {
            bool hasSpike = held.TryGetGlobalItem(out SpikeGlobalItem spike);
            caller.Reply($"Item: {held.Name} | type={held.type} | maxStack={held.maxStack} | eligible={hasSpike} | spike={(hasSpike ? spike.SpikeValue.ToString() : "n/a")}");
        }
        else
        {
            caller.Reply(Usage);
        }
    }
}
