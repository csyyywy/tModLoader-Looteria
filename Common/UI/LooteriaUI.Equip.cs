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

/// <summary>装备查看 / 重铸（逐条选择 + 演练预览 + 钱币消耗 + 升档消耗同名装备）。</summary>
public partial class LooteriaUIState
{
    // ===== 装备 =====
    private void BuildEquip()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        AddSectionTitle(_content, T("EquipHint"), C_Cyan, ref top);
        top += 4;
        top = AddInventoryGrid(top, slot => { _selectedSlot = slot.SlotIndex; ClearRerollState(); Rebuild(); }); // M5

        Item? item = GetSelected(player);
        if (item == null || item.IsAir)
        {
            AddLabel(_content, T("NoItem"), ref top, 0.8f, Color.Gray);
            return;
        }
        if (!item.TryGetGlobalItem(out AffixGlobalItem g) || !g.HasAffix)
        {
            if (ItemClassifier.IsEligible(item))
                AddLabel(_content, T("NoAffixYet"), ref top, 0.8f, Color.Orange);
            else
                AddLabel(_content, T("Ineligible"), ref top, 0.8f, Color.Gray);
            return;
        }
        AddSectionTitle(_content, $"{item.Name} · {T("Power")} {g.PowerScore}", RarityInfo.Colors[Math.Clamp((int)g.Rarity, 0, RarityInfo.Count - 1)], ref top); // M14
        AddCoinLabel(_content, $"{T("Value")}: ", item.value, ref top, 0.8f, Color.LightGray); // 原版钱币图标
        foreach (var line in FormatAffixLines(g))
        {
            AddLabel(_content, "  " + line, ref top, 0.8f, new Color(255, 200, 0));
            top += 20;
        }
        // 插槽宝石详情（每个槽位一行：◆ 宝石名 +N / ◇ 空）
        if (g.SocketCount > 0)
        {
            top += 4;
            foreach (var line in FormatSocketLines(g))
            {
                AddLabel(_content, "  " + line, ref top, 0.8f, Color.DeepSkyBlue);
                top += 20;
            }
        }
        AddButton(_content, T("Salvage"), ref top, () =>
        {
            if (BlockLocalCurrencyOp()) return; // R2：多人禁用本地拆解（尘是服务端权威）
            // L15：SalvageDivisor 默认统一为配置默认 2（与命令/分解侧一致）
            int divisor = Math.Max(1, LooteriaConfig.Instance?.SalvageDivisor ?? 2);
            int dust = Math.Max(1, g.PowerScore / divisor);
            player.GetModPlayer<LooteriaPlayer>().AddDust(dust);
            player.inventory[_selectedSlot].TurnToAir();
            _selectedSlot = -1;
            Rebuild();
        }, new Color(120, 50, 55));
    }

    // ===== 重铸 =====
    private void BuildReroll()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        AddSectionTitle(_content, T("RerollHint"), C_Cyan, ref top);
        top += 4;
        top = AddInventoryGrid(top, slot => { _selectedSlot = slot.SlotIndex; ClearRerollState(); Rebuild(); }); // M5

        Item? item = GetSelected(player);
        if (item == null || item.IsAir)
        {
            AddLabel(_content, T("NoItem"), ref top, 0.8f, Color.Gray);
            return;
        }
        if (!item.TryGetGlobalItem(out AffixGlobalItem g) || !g.HasAffix)
        {
            if (ItemClassifier.IsEligible(item))
                AddLabel(_content, T("NoAffixYet"), ref top, 0.8f, Color.Orange);
            else
                AddLabel(_content, T("Ineligible"), ref top, 0.8f, Color.Gray);
            return;
        }

        AddSectionTitle(_content, $"{item.Name} · {T("Power")} {g.PowerScore}", RarityInfo.Colors[Math.Clamp((int)g.Rarity, 0, RarityInfo.Count - 1)], ref top); // M14
        AddCoinLabel(_content, $"{T("Value")}: ", item.value, ref top, 0.8f, Color.LightGray); // 原版钱币图标（含铂金）

        // 词缀列表：每条右侧一个"重铸"按钮（点击即扣款，进入演练，未确认不生效）
        AddSectionTitle(_content, T("RerollAffixes"), C_Cyan, ref top);
        for (int i = 0; i < g.Affixes!.Count; i++)
        {
            int idx = i;
            AddLabel(_content, $"{i + 1}. {FormatAffixLine(g, i)}", ref top, 0.8f, new Color(255, 200, 0));
            var b = new UITextPanel<string>(T("RerollOne"), 0.7f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(430f, 0f),
                Width = new StyleDimension(84f, 0f),
                Height = new StyleDimension(24f, 0f)
            };
            b.BackgroundColor = new Color(60, 90, 150);
            b.WithFadedMouseOver();
            b.OnLeftClick += (_, _) =>
            {
                if (BlockLocalCurrencyOp()) return; // R2：多人禁用重铸（尘扣费是本地改）
                int coins = RerollOneCoins(item.value); // 钱币 = 价值 ÷ 配置除数（RerollOneCoinDiv）
                int pay = TryPay(player, RerollOneCost, coins);
                if (pay != 0) { Rebuild(); ShowMsg(pay == 1 ? T("NotEnoughDust") : T("NotEnoughCoins")); return; } // 演练即扣款
                _rerollIdx = idx;
                _rerollRoll = AffixRoller.PreviewRerollOne(item, g, idx);
                _rerollAllRolls = null;
                _consumeAction = 0;
                Rebuild();
            };
            _content.Append(b);
            top += 26;
        }
        top += 6;

        // 全部重铸的演练：显示所有"旧 → 新"
        if (_rerollAllRolls != null && _rerollAllRolls.Count == g.Affixes.Count)
        {
            AddSectionTitle(_content, T("RerollPreview"), C_Cyan, ref top);
            for (int i = 0; i < g.Affixes.Count; i++)
            {
                var ndef = AffixDatabase.GetById(_rerollAllRolls[i].AffixId);
                string to = ndef == null ? $"?({_rerollAllRolls[i].AffixId})" : FormatAffix(ndef, _rerollAllRolls[i].Value); // L1
                AddLabel(_content, $"{i + 1}. {FormatAffixLine(g, i)}  →  {to}",
                    ref top, 0.8f, C_Green);
                top += 22;
            }
            top += 6;
            AddButton(_content, T("RerollConfirmAll"), ref top, () =>
            {
                AffixRoller.RerollAll(item, g, _rerollAllRolls);
                _rerollAllRolls = null;
                Rebuild();
                ShowMsg(T("RerolledAll"));
            }, C_Green);
            AddButton(_content, T("RerollCancel"), ref top, () => { _rerollAllRolls = null; Rebuild(); }, new Color(60, 60, 70));
        }
        // 单条演练：旧 → 新（款已扣，确认才生效）
        else if (_rerollIdx >= 0 && _rerollIdx < g.Affixes.Count && _rerollRoll != null)
        {
            AddSectionTitle(_content, T("RerollPreview"), C_Cyan, ref top);
            AddLabel(_content, $"{T("RerollFrom")}: {FormatAffixLine(g, _rerollIdx)}", ref top, 0.8f, Color.LightGray);
            top += 22;
            var ndef = AffixDatabase.GetById(_rerollRoll.Value.AffixId);
            string to = ndef == null ? $"?({_rerollRoll.Value.AffixId})" : FormatAffix(ndef, _rerollRoll.Value.Value); // L1
            AddLabel(_content, $"{T("RerollTo")}: {to}", ref top, 0.8f, C_Green);
            top += 26;
            AddButton(_content, T("RerollConfirm"), ref top, () =>
            {
                AffixRoller.RerollOne(item, g, _rerollIdx, _rerollRoll);
                _rerollIdx = -1; _rerollRoll = null;
                Rebuild();
                ShowMsg(T("Rerolled"));
            }, C_Green);
            AddButton(_content, T("RerollCancel"), ref top, () => { _rerollIdx = -1; _rerollRoll = null; Rebuild(); }, new Color(60, 60, 70));
        }
        else
        {
            int allCoins = RerollAllCoins(item.value); // 钱币 = 价值 ÷ 配置除数（RerollAllCoinDiv）
            AddCoinButton(_content, $"{T("RerollAll")} ({RerollAllCost}{T("Dust")} + ", allCoins, ref top, () =>
            {
                if (BlockLocalCurrencyOp()) return; // R2
                int coins = RerollAllCoins(item.value);
                int pay = TryPay(player, RerollAllCost, coins);
                if (pay != 0) { Rebuild(); ShowMsg(pay == 1 ? T("NotEnoughDust") : T("NotEnoughCoins")); return; } // 演练即扣款
                _rerollAllRolls = AffixRoller.PreviewRerollAll(item, g);
                _rerollIdx = -1; _rerollRoll = null;
                _consumeAction = 0;
                Rebuild();
            }, new Color(60, 90, 150));
        }

        // 升档：120 尘 + 价值/500 钱 + 1 件同名装备（自选消耗）
        if (g.Rarity < LootRarity.Set)
        {
            int upCoins = UpgradeCoins(item.value); // 钱币 = 价值 ÷ 配置除数（UpgradeCoinDiv）
            AddCoinButton(_content, $"{T("Upgrade")} ({UpgradeCost}{T("Dust")} + {T("SameItem")}) + ", upCoins, ref top,
                () => { if (BlockLocalCurrencyOp()) return; _consumeAction = _consumeAction == 1 ? 0 : 1; _rerollIdx = -1; _rerollRoll = null; _rerollAllRolls = null; Rebuild(); }, // R2
                new Color(90, 70, 130));
            if (_consumeAction == 1)
            {
                AddSectionTitle(_content, T("PickMatItem"), C_Orange, ref top);
                // M4：材料先不销毁——回调只传槽位，DoUpgrade 先校验/扣费，成功后才消耗材料
                AddSameNamePicker(player, item.type, 2, slotIdx =>
                {
                    _consumeAction = 0;
                    DoUpgrade(player, item, g, slotIdx);
                }, ref top);
                AddButton(_content, T("RerollCancel"), ref top, () => { _consumeAction = 0; Rebuild(); }, new Color(60, 60, 70));
            }
        }
        _msgText = AddLabel(_content, "", ref top, 0.8f, Color.OrangeRed);
    }

    private void DoUpgrade(Player player, Item item, AffixGlobalItem g, int matSlot)
    {
        int coins = UpgradeCoins(item.value); // 钱币 = 价值 ÷ 配置除数（UpgradeCoinDiv）
        // M4：先校验后扣款（尘+钱币）；不足则什么都不动，材料保留
        int pay = TryPay(player, UpgradeCost, coins);
        if (pay != 0) { Rebuild(); ShowMsg(pay == 1 ? T("NotEnoughDust") : T("NotEnoughCoins")); return; }
        // 升档：材料（同名装备）无论成功/失败都消耗——博彩税（尘+钱+材料全扣，失败重试需重新准备材料）。
        // 先销毁材料再掷点：即使掷点抛异常材料也不会凭空多出来。
        if (matSlot >= 0 && matSlot < player.inventory.Length)
            player.inventory[matSlot].TurnToAir();
        bool ok = AffixRoller.UpgradeRarity(item, g);
        Rebuild();
        ShowMsg(ok ? T("UpgradeOk") : T("UpgradeFail"));
    }

    /// <summary>先校验后扣款（尘 + 钱币），任一不足则什么都不扣。返回 0=成功 / 1=尘不足 / 2=钱币不足。</summary>
    private static int TryPay(Player player, int dust, int coins)
    {
        var lp = player.GetModPlayer<LooteriaPlayer>();
        if (lp.Dust < dust) return 1;
        if (coins > 0 && !TrySpendCoins(player, coins)) return 2;
        lp.Dust -= dust;
        return 0;
    }

    /// <summary>商店式钱币扣款：整栏换算成铜币→校验→清栏→找零自动拆成 铂/金/银/铜 填回。</summary>
    private static bool TrySpendCoins(Player player, int copper)
    {
        if (copper <= 0) return true;
        long total = 0; // L4：long 累加防理论溢出（高堆叠 mod 物进钱币栏时）
        for (int i = 50; i < 54 && i < player.inventory.Length; i++)
            total += (long)player.inventory[i].stack * CoinValue(i);
        if (total < copper) return false;
        for (int i = 50; i < 54 && i < player.inventory.Length; i++)
            player.inventory[i].TurnToAir();
        GiveChange(player, (int)(total - copper));
        return true;
    }

    /// <summary>找零：把铜币数自动拆成 铂/金/银/铜 填回钱币栏（参考原版商店）。</summary>
    private static void GiveChange(Player player, int copper)
    {
        if (copper <= 0) return;
        int p = copper / 1000000; copper %= 1000000;
        int g = copper / 10000; copper %= 10000;
        int s = copper / 100; int c = copper % 100;
        SetCoin(player, 53, ItemID.PlatinumCoin, p);
        SetCoin(player, 52, ItemID.GoldCoin, g);
        SetCoin(player, 51, ItemID.SilverCoin, s);
        SetCoin(player, 50, ItemID.CopperCoin, c);
    }

    private static void SetCoin(Player player, int slot, int type, int stack)
    {
        if (slot >= player.inventory.Length) return;
        if (stack <= 0) { player.inventory[slot].TurnToAir(); return; }
        player.inventory[slot].SetDefaults(type);
        player.inventory[slot].stack = stack;
    }

    private static int CoinValue(int slot) => (int)Math.Pow(100, slot - 50); // 50铜 51银 52金 53铂

    private Item? GetSelected(Player player)
    {
        if (_selectedSlot < 0 || _selectedSlot >= player.inventory.Length) return null;
        return player.inventory[_selectedSlot];
    }
}
