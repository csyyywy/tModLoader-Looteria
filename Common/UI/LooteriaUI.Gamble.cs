using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.UI;
using Terraria.UI;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria.Common.UI;

/// <summary>赌博页：档位选择、抽奖、200 格掠夺容器。</summary>
public partial class LooteriaUIState
{
    private void BuildGamble()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        var lp = player.GetModPlayer<LooteriaPlayer>();
        AddSectionTitle(_content, T("GambleHint"), C_Pink, ref top);
        top += 4;

        // 档位选择（8 档；未解锁灰显）
        for (int i = 0; i < GambleTiers.All.Length; i++)
        {
            int idx = i;
            var tier = GambleTiers.All[i];
            bool unlocked = GambleTiers.IsUnlocked(i);
            var b = new UITextPanel<string>($"{T($"GambleTier.{tier.Key}")} · {tier.Cost}{T("Shards")}", 0.7f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(10f + (i % 4) * 172f, 0f),
                Width = new StyleDimension(164f, 0f),
                Height = new StyleDimension(30f, 0f)
            };
            b.BackgroundColor = _gambleTier == i ? C_Selected : (unlocked ? new Color(50, 52, 70) : new Color(30, 31, 40));
            b.TextColor = unlocked ? Color.White : Color.Gray;
            b.BorderColor = _gambleTier == i ? C_Accent : new Color(60, 64, 84);
            b.WithFadedMouseOver();
            if (unlocked)
                b.OnLeftClick += (_, _) => { _gambleTier = idx; Rebuild(); };
            _content.Append(b);
            if (i % 4 == 3) top += 34;
        }
        top += 34;

        // 当前档费用 + 血岩 + 容器
        var cur = GambleTiers.All[Math.Clamp(_gambleTier, 0, GambleTiers.All.Length - 1)];
        AddLabel(_content, $"{T("GambleCost")}: {cur.Cost}{T("Shards")} · {T("CurShards")}: {lp.BloodShards} · {T("Container")}: {lp.GambleContainer.Count}/{LootGenerator.ContainerSize}",
            ref top, 0.8f, C_Pink);
        top += 30;

        AddButton(_content, T("GambleOnce"), ref top, () =>
        {
            if (lp.GambleContainer.Count >= LootGenerator.ContainerSize)
                _gambleLog = T("ContainerFull");
            else
            {
                // 多人：请求包上行，服务端结算后回发容器镜像；单机：本地直通
                GambleService.RequestGamble(_gambleTier, 1);
                if (Main.netMode != NetmodeID.MultiplayerClient) Rebuild();
            }
        }, new Color(60, 90, 150));
        AddButton(_content, T("GambleTen"), ref top, () =>
        {
            GambleService.RequestGamble(_gambleTier, 10);
            if (Main.netMode != NetmodeID.MultiplayerClient) Rebuild();
        }, new Color(60, 90, 150));
        AddButton(_content, T("ClaimAll"), ref top, () => { GambleService.RequestContainerOp(1, 0); if (Main.netMode != NetmodeID.MultiplayerClient) Rebuild(); }, C_Green);
        AddButton(_content, T("SalvageAll"), ref top, () => { GambleService.RequestContainerOp(2, 0); if (Main.netMode != NetmodeID.MultiplayerClient) Rebuild(); }, new Color(120, 50, 55));
        _msgText = AddLabel(_content, _gambleLog, ref top, 0.8f, Color.OrangeRed);
        top += 20;

        // 200 格容器（可上下滚动，排布像物品栏；点击格子领取到背包）
        if (lp.GambleContainer.Count == 0)
        {
            AddLabel(_content, T("GambleEmpty"), ref top, 0.8f, Color.Gray);
            return;
        }
        AddSectionTitle(_content, T("ContainerTitle"), C_Cyan, ref top);
        top += 4;

        int listTop = top;
        var list = new UIList
        {
            Top = new StyleDimension(listTop, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(-44f, 1f),
            Height = new StyleDimension(-listTop - 8f, 1f)
        };
        var scrollbar = new UIScrollbar
        {
            Top = new StyleDimension(listTop, 0f),
            Left = new StyleDimension(-28f, 1f),
            Height = new StyleDimension(-listTop - 8f, 1f)
        };
        scrollbar.SetView(100f, 1000f);
        list.SetScrollbar(scrollbar);

        var c = lp.GambleContainer;
        for (int r = 0; r * 10 < c.Count; r++)
        {
            var row = new UIElement { Width = new StyleDimension(600f, 0f), Height = new StyleDimension(56f, 0f) };
            for (int k = 0; k < 10; k++)
            {
                int idx = r * 10 + k;
                if (idx >= c.Count) break;
                var it = c[idx];
                if (it == null || it.IsAir) continue;
                int i2 = idx;
                var slot = new UIItemSlot(idx, it)
                {
                    Top = new StyleDimension(0f, 0f),
                    Left = new StyleDimension(k * 56f, 0f),
                    StackText = it.stack > 1 ? it.stack.ToString() : null
                };
                if (it.TryGetGlobalItem(out AffixGlobalItem g) && g.HasAffix)
                    slot.RarityHighlight = (int)g.Rarity;
                slot.OnSlotClicked += _ => { GambleService.RequestContainerOp(0, (ushort)i2); if (Main.netMode != NetmodeID.MultiplayerClient) Rebuild(); }; // H2+多人
                row.Append(slot);
            }
            list.Add(row);
        }
        _content.Append(list);
        _content.Append(scrollbar);
        list.Recalculate();
    }

    // 领取/分解已移至 GambleService（多人服务端权威；单机直通），见 RequestContainerOp。
}
