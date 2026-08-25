using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ModLoader.UI;
using Terraria.UI;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Roll;

namespace Looteria.Common.UI;

/// <summary>镶嵌 / 升阶：宝石背包、插槽、开槽（自选同名装备）、升阶（原版宝石 + 钱币）。</summary>
public partial class LooteriaUIState
{
    // ===== 镶嵌 =====
    private void BuildSocket()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        var lp = player.GetModPlayer<LooteriaPlayer>();

        AddSectionTitle(_content, T("SocketGemSel"), C_Pink, ref top);
        string selInfo = DescribeSelectedGem(player);
        AddLabel(_content, selInfo, ref top, 0.8f, Color.LightCyan);
        top += 24; // 留出文字高度，避免被下方宝石图标盖住
        AddGemTiles(_content, ref top, _selectedGemItemSlot, slotIdx =>
        {
            _selectedGemItemSlot = slotIdx;
            var gi = GetSelectedGem(player);
            _selectedGemId = gi?.GemId ?? 0;
            Rebuild();
        });
        top += 8;

        AddSectionTitle(_content, T("SocketEquipSel"), C_Green, ref top);
        top = AddInventoryGrid(top, slot => { _selectedSlot = slot.SlotIndex; Rebuild(); });

        Item? item = GetSelected(player);
        if (item == null || item.IsAir)
        {
            AddLabel(_content, T("NoItem"), ref top, 0.8f, Color.Gray);
            return;
        }
        if (!item.TryGetGlobalItem(out AffixGlobalItem g))
        {
            AddLabel(_content, T("Ineligible"), ref top, 0.8f, Color.Gray);
            return;
        }
        AddSectionTitle(_content, $"{item.Name} · {T("Power")} {g.PowerScore} · {T("Sockets")} {g.SocketCount}/{MaxSockets}",
            RarityInfo.Colors[Math.Clamp((int)g.Rarity, 0, RarityInfo.Count - 1)], ref top); // M14：半钳制补全

        // 插槽列表（点击：空槽=镶入选中宝石；有宝石=取下返还）
        if (g.SocketCount > 0)
        {
            for (int i = 0; i < g.SocketCount; i++)
            {
                int sock = i;
                int sockVal = g.Sockets![i];
                int gemId = sockVal % 1000;
                int gemUp = sockVal / 1000;
                string txt = sockVal > 0
                    ? "◆ " + Language.GetTextValue(GemDatabase.Key(gemId)) + (gemUp > 0 ? $" +{gemUp}" : "")
                    : "◇ " + T("Empty");
                AddButton(_content, txt, ref top, () =>
                {
                    if (sockVal > 0)
                    {
                        // 取下宝石：返还宝石物品（含强化）
                        int returnType = Content.Items.Gems.GemItemHelper.TypeForGem(gemId);
                        if (returnType > 0)
                        {
                            var gi = new Item(returnType);
                            if (gi.ModItem is Content.Items.Gems.LooteriaGemItem gg)
                            {
                                gg.Upgrade = gemUp;
                                var tag = ItemIO.Save(gi);
                                var loaded = ItemIO.Load(tag);
                                player.QuickSpawnItem(player.GetSource_FromThis(), loaded, 1);
                            }
                            else
                            {
                                player.QuickSpawnItem(player.GetSource_FromThis(), returnType, 1);
                            }
                        }
                        g.Sockets[sock] = 0;
                    }
                    else if (_selectedGemItemSlot >= 0 && _selectedGemItemSlot < player.inventory.Length)
                    {
                        var gemItem = player.inventory[_selectedGemItemSlot];
                        if (gemItem != null && !gemItem.IsAir && gemItem.ModItem is Content.Items.Gems.LooteriaGemItem selGem)
                        {
                            gemItem.stack -= 1; // 宝石不可堆叠，扣 1 即整颗
                            if (gemItem.stack <= 0) gemItem.TurnToAir();
                            g.Sockets[sock] = selGem.GemId + selGem.Upgrade * 1000;
                            _selectedGemId = 0;
                            _selectedGemItemSlot = -1;
                        }
                    }
                    else
                    {
                        ShowMsg(T("SocketPickGemFirst"));
                        return;
                    }
                    g.PowerScore = AffixRoller.PowerScore(g);
                    Rebuild();
                }, sockVal > 0 ? new Color(70, 80, 120) : new Color(45, 60, 90));
            }
        }
        else
        {
            AddLabel(_content, T("NoSockets"), ref top, 0.8f, Color.Gray);
            top += 24;
        }

        // 开槽：自选一件同名装备消耗 + 尘/钱币（M15 修复：原按钮显示成本但分文未收）
        if (g.OpenedSockets < MaxOpenedSockets && g.SocketCount < MaxSockets)
        {
            int sockCoins = SocketCoins(item.value); // 钱币 = 价值 ÷ 配置除数（SocketCoinDiv）
            int sockDust = LooteriaConfig.Instance?.SocketCostDust ?? 40;
            // 当前钱币（你身上+银行全部）；消耗已写进下方开槽按钮内
            long wallet = PlayerCoins(player);
            AddCoinLabel(_content, $"{T("WalletCurrent")}: ", (int)Math.Min(wallet, int.MaxValue), ref top, 0.8f, Color.LightGray);
            AddCoinButton(_content, $"{T("SocketAdd")} ({g.OpenedSockets}/{MaxOpenedSockets} · {sockDust}{T("Dust")} + {T("SameItem")})",
                sockCoins, ref top, () => { if (BlockLocalCurrencyOp()) return; _consumeAction = _consumeAction == 2 ? 0 : 2; Rebuild(); }, new Color(90, 70, 130)); // R2
            if (_consumeAction == 2)
            {
                AddSectionTitle(_content, T("PickMatItem"), C_Orange, ref top);
                AddSameNamePicker(player, item.type, 2, slotIdx =>
                {
                    _consumeAction = 0;
                    DoOpenSocket(player, g, item, slotIdx);
                }, ref top);
                AddButton(_content, T("RerollCancel"), ref top, () => { _consumeAction = 0; Rebuild(); }, new Color(60, 60, 70));
            }
        }
        _msgText = AddLabel(_content, "", ref top, 0.8f, Color.OrangeRed);
    }

    /// <summary>M4+M15：开槽 = 先校验（含材料槽）并扣费（尘+钱币），成功才消耗同名装备并 +1 槽。
    /// 任一不足：不开槽、不耗材料、不扣费（原子）。R10：材料槽校验在扣款前。</summary>
    private void DoOpenSocket(Player player, AffixGlobalItem g, Item target, int matSlot)
    {
        if (g.OpenedSockets >= MaxOpenedSockets || g.SocketCount >= MaxSockets) return;
        // R10：先校验材料槽（选择器已保证同 type，这里兜底时序竞争）
        if (matSlot < 0 || matSlot >= player.inventory.Length || player.inventory[matSlot].IsAir)
        {
            Rebuild(); ShowMsg(T("NoSameItem"));
            return;
        }
        int sockCoins = SocketCoins(target.value); // 钱币 = 价值 ÷ 配置除数（SocketCoinDiv）
        int sockDust = LooteriaConfig.Instance?.SocketCostDust ?? 40;
        // 先校验后扣款
        var lp = player.GetModPlayer<LooteriaPlayer>();
        if (lp.Dust < sockDust) { Rebuild(); ShowMsg(T("NotEnoughDust")); return; }
        if (sockCoins > 0 && !player.BuyItem(sockCoins)) { Rebuild(); ShowMsg(T("NotEnoughCoins")); return; } // 原版商店扣款
        lp.Dust -= sockDust;

        player.inventory[matSlot].TurnToAir();

        g.OpenedSockets++;
        g.SocketCount++;
        g.Sockets ??= new List<int>();
        g.Sockets.Add(0);
        g.PowerScore = AffixRoller.PowerScore(g);
        Rebuild();
        ShowMsg(T("SocketAdded"));
    }

    /// <summary>同名装备选择器：列出背包里 type 相同的物品（排除选中），点击回调（外部负责消耗）。</summary>
    private void AddSameNamePicker(Player player, int type, int rows, Action<int> onPick, ref int top)
    {
        int shown = 0;
        for (int i = 0; i < player.inventory.Length && shown < rows * 6; i++)
        {
            if (i == _selectedSlot) continue;
            var cand = player.inventory[i];
            if (cand == null || cand.IsAir || cand.type != type) continue;
            int slotIdx = i;
            int r = shown / 6, col = shown % 6;
            var s = new UIItemSlot(slotIdx, cand)
            {
                Top = new StyleDimension(top + r * 54f, 0f),
                Left = new StyleDimension(8f + col * 56f, 0f),
                StackText = cand.stack > 1 ? cand.stack.ToString() : null
            };
            s.OnSlotClicked += _ => onPick(slotIdx);
            _content.Append(s);
            shown++;
        }
        if (shown == 0)
        {
            AddLabel(_content, T("NoSameItem"), ref top, 0.8f, Color.Gray);
            top += 24;
            return;
        }
        top += ((shown + 5) / 6) * 54 + 8;
    }

    // ===== 升阶 =====
    private void BuildEnhance()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        var lp = player.GetModPlayer<LooteriaPlayer>();

        AddSectionTitle(_content, T("EnhanceGemSel"), C_Pink, ref top);
        AddGemTiles(_content, ref top, _selectedGemItemSlot, slotIdx =>
        {
            _selectedGemItemSlot = slotIdx;
            var gi = GetSelectedGem(player);
            _selectedGemId = gi?.GemId ?? 0;
            Rebuild();
        });
        top += 8;

        AddSectionTitle(_content, T("EnhanceTitle"), C_Orange, ref top);

        var selGem = GetSelectedGem(player);
        if (selGem == null)
        {
            AddLabel(_content, T("EnhanceNoSel"), ref top, 0.8f, Color.Gray);
            return;
        }

        int cost = GemUpgradeCost(selGem.Upgrade);
        int vType = Content.Items.Gems.GemItemHelper.VanillaGemType(selGem.GemType);
        string vName = vType > 0 ? Lang.GetItemNameValue(vType) : T("GemUpgradeMat");
        int have = CountInventory(player, vType);
        int coins = GemUpgradeCoins(selGem.Item.value); // 钱币 = 宝石价值 ÷ 配置除数（GemUpgradeCoinDiv）
        float rate = Math.Max(0.3f, 1f - 0.05f * selGem.Upgrade);

        AddLabel(_content, $"{selGem.DisplayName.Value} · {T("GemUp")} +{selGem.Upgrade}", ref top, 0.9f, Color.LightCyan);
        top += 26;
        AddCoinLabel(_content, $"{T("EnhanceMat")}: {vName} ×{have} / {T("Need")} {cost} + ",
            coins, ref top, 0.8f, have >= cost ? C_Green : C_Red);
        top += 28;
        AddLabel(_content, $"{T("EnhanceRate")}: {(int)(rate * 100)}%", ref top, 0.8f, Color.LightYellow);
        // 进度条画在文字下方，避免压住文字
        _content.Append(new UIBar
        {
            Top = new StyleDimension(top + 19f, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(240f, 0f),
            Height = new StyleDimension(12f, 0f),
            Fraction = rate,
            FillColor = rate > 0.6f ? C_Green : rate > 0.4f ? C_Orange : C_Red
        });
        top += 34;

        // 当前钱币（你身上+银行全部）；消耗已写进升阶按钮内
        long wallet = PlayerCoins(player);
        AddCoinLabel(_content, $"{T("WalletCurrent")}: ", (int)Math.Min(wallet, int.MaxValue), ref top, 0.8f, Color.LightGray);

        AddCoinButton(_content, $"{T("GemUpgrade")} ({cost} {vName})", coins, ref top,
            () => { if (BlockLocalCurrencyOp()) return; UpgradeGem(player, lp, selGem, _selectedGemItemSlot); }, new Color(70, 90, 150)); // R2
        _gemMsg = AddLabel(_content, "", ref top, 0.8f, Color.OrangeRed);
    }

    /// <summary>升到下一阶需要的同色原版宝石数：2 × 1.5^当前阶（向上取整），即每升 1 阶消耗翻 1.5 倍。</summary>
    public static int GemUpgradeCost(int currentUpgrade)
        => Math.Max(1, (int)Math.Ceiling(2.0 * Math.Pow(1.5, currentUpgrade)));

    /// <summary>宝石升阶：消耗 cost 颗同色原版宝石 + 宝石价值÷GemUpgradeCoinDiv 钱币（默认 500），成功率随阶数下降，成功 +1 阶（+10% 效果）。</summary>
    private void UpgradeGem(Player player, LooteriaPlayer lp, Content.Items.Gems.LooteriaGemItem gem, int targetSlot)
    {
        int cost = GemUpgradeCost(gem.Upgrade);
        int vType = Content.Items.Gems.GemItemHelper.VanillaGemType(gem.GemType);
        if (vType <= 0) { ShowMsg(T("GemUpgradeNoMat")); return; }

        // 先全部校验，再扣款/扣材料（任一不足则什么都不动）
        int have = CountInventory(player, vType);
        if (have < cost)
        {
            ShowMsg(Language.GetTextValue("Mods.Looteria.Messages.GemUpgradeNeed", cost - have, Lang.GetItemNameValue(vType)));
            return;
        }
        int coins = GemUpgradeCoins(gem.Item.value); // 钱币 = 宝石价值 ÷ 配置除数
        if (!player.BuyItem(coins)) { ShowMsg(T("NotEnoughCoins")); return; } // 原版商店扣款（含找零）

        int need = cost;
        for (int i = 0; i < player.inventory.Length && need > 0; i++)
        {
            if (i == targetSlot) continue;
            var it = player.inventory[i];
            if (it == null || it.IsAir || it.type != vType) continue;
            int take = Math.Min(need, it.stack);
            it.stack -= take;
            if (it.stack <= 0) it.TurnToAir();
            need -= take;
        }

        float rate = Math.Max(0.3f, 1f - 0.05f * gem.Upgrade);
        bool ok = Main.rand.NextFloat() < rate;
        if (ok) gem.Upgrade++;
        Rebuild();
        ShowMsg(ok ? T("GemUpgradeOk") : T("GemUpgradeFail"));
    }

    /// <summary>宝石物品网格（图标），最多 4 行、可滚动（支持大背包），点击选中。</summary>
    private void AddGemTiles(UIPanel panel, ref int top, int selectedSlot, Action<int> onClick)
    {
        var player = Main.LocalPlayer;
        var gems = new List<int>();
        for (int i = 0; i < player.inventory.Length; i++)
        {
            var gem = player.inventory[i];
            if (gem != null && !gem.IsAir && gem.ModItem is Content.Items.Gems.LooteriaGemItem) gems.Add(i);
        }
        if (gems.Count == 0)
        {
            AddLabel(panel, T("NoGems"), ref top, 0.8f, Color.Gray);
            top += 20;
            return;
        }

        int rows = (gems.Count + 5) / 6;
        float listH = Math.Min(rows, 4) * 56f;
        var list = new UIList
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(-44f, 1f),
            Height = new StyleDimension(listH, 0f)
        };
        var scrollbar = new UIScrollbar
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(-28f, 1f),
            Height = new StyleDimension(listH, 0f)
        };
        scrollbar.SetView(100f, 1000f);
        list.SetScrollbar(scrollbar);

        for (int r = 0; r < rows; r++)
        {
            var row = new UIElement { Width = new StyleDimension(360f, 0f), Height = new StyleDimension(52f, 0f) };
            for (int k = 0; k < 6; k++)
            {
                int n = r * 6 + k;
                if (n >= gems.Count) break;
                int slotIdx = gems[n];
                var gem = player.inventory[slotIdx];
                var slot = new UIItemSlot(slotIdx, gem)
                {
                    Top = new StyleDimension(0f, 0f),
                    Left = new StyleDimension(k * 56f, 0f)
                };
                slot.Selected = slotIdx == selectedSlot;
                slot.OnSlotClicked += _ => onClick(slotIdx);
                row.Append(slot);
            }
            list.Add(row);
        }
        panel.Append(list);
        panel.Append(scrollbar);
        list.Recalculate();
        top += (int)listH + 8;
    }

    private string DescribeSelectedGem(Player player)
    {
        var gi = GetSelectedGem(player);
        if (gi == null) return T("SocketPickGem");
        int vType = Content.Items.Gems.GemItemHelper.VanillaGemType(gi.GemType);
        int have = CountInventory(player, vType);
        return $"{T("SocketGemHeld")}: {gi.DisplayName.Value}"
             + (vType > 0 ? $" · {Lang.GetItemNameValue(vType)} ×{have}" : "");
    }

    private Content.Items.Gems.LooteriaGemItem? GetSelectedGem(Player player)
    {
        if (_selectedGemItemSlot < 0 || _selectedGemItemSlot >= player.inventory.Length) return null;
        var it = player.inventory[_selectedGemItemSlot];
        return it != null && !it.IsAir && it.ModItem is Content.Items.Gems.LooteriaGemItem gi ? gi : null;
    }

    private static int CountInventory(Player player, int type)
    {
        int n = 0;
        foreach (var it in player.inventory)
            if (it != null && !it.IsAir && it.type == type) n += it.stack;
        return n;
    }
}
