using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Players;

namespace Looteria.Content.Items;

/// <summary>
/// 「谢谢惠顾券」：抽奖空奖的另一种奖励（没中也没免费机会），使用后获得少量重铸之尘作为安慰。
/// 安慰尘数量可配置（LooteriaConfig.TicketDust，默认 5）。
/// </summary>
public class ThankYouTicket : ModItem
{
    public override LocalizedText DisplayName => Language.GetText("Mods.Looteria.Ticket.ThankYouName");
    public override LocalizedText Tooltip => Language.GetText("Mods.Looteria.Ticket.ThankYouTooltip");

    public override void SetDefaults()
    {
        Item.width = 24; Item.height = 24;
        Item.maxStack = 99;
        Item.value = 100;
        Item.rare = ItemRarityID.White;
        Item.consumable = true;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20; Item.useAnimation = 20;
        Item.useTurn = true;
    }

    public override bool? UseItem(Player player)
    {
        // R2：多人下尘是服务端权威，本地 AddDust 会被下一次 CurrencyPush 回滚（券白用）。
        // 多人客户端禁用；服务端/单机照常。
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
        {
            Main.NewText(Language.GetTextValue("Mods.Looteria.UI.MpLocalOpDisabled"));
            return false;
        }
        int dust = LooteriaConfig.Instance?.TicketDust ?? 5;
        player.GetModPlayer<LooteriaPlayer>().AddDust(dust);
        Main.NewText(Language.GetTextValue("Mods.Looteria.Messages.TicketThanks", dust));
        return true;
    }
}