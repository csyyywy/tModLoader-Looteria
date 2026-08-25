using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;
using global::Looteria.Common.Players;
using global::Looteria.Common.Systems;

namespace Looteria.Common.UI;

/// <summary>
/// 角色属性面板（刷宝游戏样式）：
/// - 左：角色肖像（占位图 CharPortrait.png）+ 力量/套装概览
/// - 中：两侧装备槽（护甲+饰品+手持），悬停悬浮预览完整 tooltip
/// - 右：详细属性列表（图标 + 名称 + 实时值，含来源 tooltip）
/// 按键 C 打开（UISystem.CharSheetKeybind）；单机暂停。
/// </summary>
public class CharacterSheetUI : UIState
{
    public static CharacterSheetUI Instance = new();

    private static readonly Color C_Accent = new(255, 200, 0);
    private static readonly Color C_Cyan = new(80, 200, 255);
    private static readonly Color C_Green = new(120, 255, 160);
    private static readonly Color C_Orange = new(255, 150, 60);
    private static readonly Color C_Dim = new(150, 155, 170);
    private static readonly Color C_PanelBg = new(26, 28, 42);
    private static readonly Color C_SubBg = new(20, 22, 34);

    private UIPanel _root = null!;
    private UIPanel _left = null!;
    private UIPanel _mid = null!;
    private UIPanel _right = null!;
    private PortraitElement _portrait = null!;

    private static readonly Dictionary<string, Asset<Texture2D>> _statIcons = new();

    private static Asset<Texture2D> StatIcon(string key)
    {
        if (!_statIcons.TryGetValue(key, out var a))
        {
            try { a = _statIcons[key] = ModContent.Request<Texture2D>($"Looteria/Content/UI/CharacterSheet/Stat_{key}"); }
            catch { a = _statIcons[key] = ModContent.Request<Texture2D>("Looteria/Content/UI/PowerIcon"); } // 兜底
        }
        return a;
    }

    private static string T(string key) => Language.GetTextValue($"Mods.Looteria.UI.{key}");

    public override void OnActivate()
    {
        UISystem.CharSheetOpen = true;
        if (Main.netMode != NetmodeID.MultiplayerClient)
            Main.gamePaused = true;
        Rebuild();
    }

    public override void OnDeactivate()
    {
        UISystem.CharSheetOpen = false;
        Main.gamePaused = false;
    }

    public override void OnInitialize()
    {
        _root = new UIPanel
        {
            HAlign = 0.5f, VAlign = 0.5f,
            Width = new StyleDimension(1000f, 0f),
            Height = new StyleDimension(-60f, 1f),
            BackgroundColor = C_PanelBg,
            BorderColor = new Color(70, 74, 96)
        };
        Append(_root);
    }

    public void Rebuild()
    {
        if (_root == null) return;
        _root.RemoveAllChildren();

        // 标题
        var title = new UIText(T("CharSheetTitle"), 1.15f) { HAlign = 0.5f, Top = new StyleDimension(6f, 0f), TextColor = C_Accent };
        _root.Append(title);

        // ===== 左：角色肖像 + 概览 =====
        _left = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(10f, 0f),
            Width = new StyleDimension(240f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_left);

        // 肖像：真实玩家渲染（PlayerRenderer.DrawPlayer），占位图已废弃
        _portrait = new PortraitElement
        {
            HAlign = 0.5f,
            Top = new StyleDimension(12f, 0f),
            Width = new StyleDimension(140f, 0f),
            Height = new StyleDimension(140f, 0f)
        };
        _left.Append(_portrait);

        var lp = Main.LocalPlayer.GetModPlayer<LooteriaPlayer>();
        int top = 150;

        // 力量
        AddRow(_left, "Stat_GearPower", T("Power") + ": " + lp.GearPower, ref top, C_Accent,
            Language.GetTextValue("Mods.Looteria.UI.PowerTip", lp.GearPower));
        top += 6;

        // 套装概览（各主题已穿戴件数）
        var counts = SetCounts();
        if (counts.Count == 0)
        {
            AddRow(_left, "Stat_SetBonus", T("SetNone"), ref top, C_Dim, "");
        }
        else
        {
            foreach (var kv in counts)
            {
                string themeName = Language.GetTextValue($"Mods.Looteria.Theme.{kv.Key}");
                string active = kv.Value >= 2 ? "✓" : "";
                AddRow(_left, "Stat_SetBonus", $"{themeName} ×{kv.Value} {active}", ref top, kv.Value >= 2 ? C_Green : C_Dim, "");
            }
        }
        top += 8;
        AddRow(_left, "Stat_SetBonus", T("SetBonusHint"), ref top, C_Dim, "");

        // ===== 中：装备槽（护甲 + 饰品 + 手持）=====
        _mid = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(258f, 0f),
            Width = new StyleDimension(300f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_mid);

        var player = Main.LocalPlayer;
        // 槽位布局：左列 = 头盔/胸甲/腿/鞋(饰品槽3)，右列 = 饰品槽1/2/4/5 + 手持
        // armor 索引：0头 1胸 2腿 3-7饰品 8钩爪 9盾/坐骑；时装 10+
        BuildEquipColumn(_mid, "Stat_Defense", new (int, string)[]
        {
            (0, T("SlotHead")), (1, T("SlotChest")), (2, T("SlotLegs")),
            (3, T("SlotAcc")), (4, T("SlotAcc")), (5, T("SlotAcc"))
        }, 0);
        BuildEquipColumn(_mid, "Stat_Damage", new (int, string)[]
        {
            (6, T("SlotAcc")), (7, T("SlotAcc")), (8, T("SlotHook")),
            (9, T("SlotShield")), (-1, T("SlotHeld"))
        }, 150);

        // ===== 右：属性列表 =====
        _right = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(566f, 0f),
            Width = new StyleDimension(-576f, 1f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_right);

        BuildStats(player, lp);
    }

    /// <summary>装备槽列：在 _mid 内按固定 52px 格子摆放，悬停完整悬浮预览。</summary>
    private static void BuildEquipColumn(UIPanel panel, string fallbackIcon, (int Slot, string Label)[] slots, float startX)
    {
        var player = Main.LocalPlayer;
        int y = 12;
        foreach (var (slot, label) in slots)
        {
            // 标签
            var lbl = new UIText(label, 0.6f)
            {
                Top = new StyleDimension(y - 14f, 0f),
                Left = new StyleDimension(startX + 2f, 0f),
                TextColor = C_Dim
            };
            panel.Append(lbl);

            // 槽
            var item = slot >= 0 && slot < player.armor.Length ? player.armor[slot] : player.HeldItem;
            var ui = new UIItemSlot(slot, item)
            {
                Top = new StyleDimension(y, 0f),
                Left = new StyleDimension(startX, 0f)
            };
            if (slot >= 0 && item.TryGetGlobalItem(out AffixGlobalItem ag) && ag.HasAffix)
                ui.RarityHighlight = (int)ag.Rarity;
            ui.OnHover = ShowHoverItemTooltip;
            ui.OnSlotClicked += _ => { }; // 仅预览，不交互
            panel.Append(ui);
            y += 58;
        }
    }

    /// <summary>角色肖像旁/概览的图标+文本行（tooltip 悬停显示）。</summary>
    private static void AddRow(UIPanel panel, string iconKey, string text, ref int top, Color color, string tooltip)
    {
        var img = new UIImage(StatIcon(iconKey))
        {
            Top = new StyleDimension(top - 2f, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(24f, 0f),
            Height = new StyleDimension(24f, 0f)
        };
        img.ImageScale = 0.75f;
        panel.Append(img);

        var t = new UIText(text, 0.75f)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(38f, 0f),
            TextColor = color
        };
        panel.Append(t);
        if (tooltip.Length > 0)
            t.OnMouseOver += (_, _) => { Main.LocalPlayer.mouseInterface = true; Main.instance.MouseText(tooltip); };
        top += 26;
    }

    /// <summary>属性行：图标 + 名称 + 值 + 悬停来源说明。</summary>
    private static void AddStat(UIPanel panel, string iconKey, string name, string value, ref int top, Color color, string tooltip = "")
    {
        var img = new UIImage(StatIcon(iconKey))
        {
            Top = new StyleDimension(top - 2f, 0f),
            Left = new StyleDimension(6f, 0f),
            Width = new StyleDimension(24f, 0f),
            Height = new StyleDimension(24f, 0f)
        };
        img.ImageScale = 0.75f;
        panel.Append(img);

        var row = new UIText($"{name}: {value}", 0.7f)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(34f, 0f),
            TextColor = color
        };
        panel.Append(row);
        if (tooltip.Length > 0)
            row.OnMouseOver += (_, _) => { Main.LocalPlayer.mouseInterface = true; Main.instance.MouseText(tooltip); };
        top += 24;
    }

    /// <summary>统计各套装主题已穿戴件数（真实槽 0..9，与 SetBonusHandler 同口径）。</summary>
    private static Dictionary<int, int> SetCounts()
    {
        var counts = new Dictionary<int, int>();
        var player = Main.LocalPlayer;
        for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < player.armor.Length; i++)
        {
            var item = player.armor[i];
            if (item.TryGetGlobalItem(out AffixGlobalItem g) && g.SetThemeId >= 0)
            {
                if (counts.TryGetValue(g.SetThemeId, out int c)) counts[g.SetThemeId] = c + 1;
                else counts[g.SetThemeId] = 1;
            }
        }
        return counts;
    }

    /// <summary>右侧属性列表：实时 Player 值（含全部增益/套装/宝石），足够详细。</summary>
    private void BuildStats(Player p, LooteriaPlayer lp)
    {
        int top = 8;
        AddSection(_right, T("StatSectionCombat"), ref top);

        // 伤害：总伤害加成（含职业），按通用显示
        float dmgBonus = p.GetDamage(DamageClass.Generic).Additive * 100f;
        float critChance = p.GetCritChance(DamageClass.Generic);
        AddStat(_right, "Stat_Damage", T("StatDamage"), $"+{dmgBonus:0.#}%", ref top, Color.White);
        AddStat(_right, "Stat_CritChance", T("StatCritChance"), $"{critChance:0.#}%", ref top, C_Orange);
        AddStat(_right, "Stat_CritDamage", T("StatCritDamage"), $"+{lp.PassiveCritDamage:0.#}%", ref top, C_Orange);
        AddStat(_right, "Stat_AttackSpeed", T("StatAttackSpeed"), $"+{p.GetAttackSpeed(DamageClass.Generic) * 100f:0.#}%", ref top, C_Green);

        top += 6;
        AddSection(_right, T("StatSectionDefense"), ref top);

        AddStat(_right, "Stat_Life", T("StatLife"), $"{p.statLifeMax2}", ref top, Color.White);
        AddStat(_right, "Stat_Mana", T("StatMana"), $"{p.statManaMax2}", ref top, Color.White);
        AddStat(_right, "Stat_Defense", T("StatDefense"), $"{p.statDefense}", ref top, Color.White);
        AddStat(_right, "Stat_DamageReduction", T("StatDamageReduction"), $"{p.endurance * 100f:0.#}%", ref top, C_Green);
        AddStat(_right, "Stat_LifeRegen", T("StatLifeRegen"), $"{p.lifeRegen / 2f:0.#}/s", ref top, C_Green);
        AddStat(_right, "Stat_ManaRegen", T("StatManaRegen"), $"{p.manaRegenBonus}", ref top, C_Cyan);

        top += 6;
        AddSection(_right, T("StatSectionMobility"), ref top);

        AddStat(_right, "Stat_MoveSpeed", T("StatMoveSpeed"), $"{p.moveSpeed * 100f:0.#}%", ref top, C_Cyan);

        top += 6;
        AddSection(_right, T("StatSectionOnHit"), ref top);

        AddStat(_right, "Stat_Life", T("StatLifeOnHit"), $"+{lp.PassiveLifeOnHit:0.#}", ref top, C_Green);
        AddStat(_right, "Stat_Mana", T("StatManaOnHit"), $"+{lp.PassiveManaOnHit}", ref top, C_Cyan);

        top += 6;
        AddSection(_right, T("StatSectionResist"), ref top);

        AddStat(_right, "Stat_Defense", T("StatBuffResistPoison"), $"{p.buffImmune[BuffID.Poisoned]}", ref top, C_Dim);
        AddStat(_right, "Stat_Defense", T("StatBuffResistFire"), $"{p.buffImmune[BuffID.OnFire]}", ref top, C_Dim);
        AddStat(_right, "Stat_Defense", T("StatBuffResistBleed"), $"{p.buffImmune[BuffID.Bleeding]}", ref top, C_Dim);
        AddStat(_right, "Stat_Defense", T("StatBuffResistCurse"), $"{p.buffImmune[BuffID.CursedInferno]}", ref top, C_Dim);
        AddStat(_right, "Stat_Defense", T("StatBuffResistSlow"), $"{p.buffImmune[BuffID.Slow]}", ref top, C_Dim);
    }

    private static void AddSection(UIPanel panel, string title, ref int top)
    {
        var t = new UIText(title, 0.8f) { Top = new StyleDimension(top, 0f), Left = new StyleDimension(4f, 0f), TextColor = C_Accent };
        panel.Append(t);
        top += 22;
    }

    /// <summary>悬停物品悬浮预览（与掠夺面板共用，每帧克隆防击退爆炸）。</summary>
    private static void ShowHoverItemTooltip(Item item)
    {
        if (item == null || item.IsAir) return;
        Main.HoverItem = item.Clone();
        Main.instance.MouseText("");
    }

    /// <summary>
    /// 角色肖像：直接用游戏内玩家形象渲染（Main.PlayerRenderer.DrawPlayer）。
    /// 文档明确支持 UI 内绘制：设 isDisplayDollOrInanimate=true 避免世界光照影响。
    /// DrawPlayer 的 position 是【世界坐标】脚底锚点（Main.Camera 带世界变换），
    /// 因此把 UI 坐标 + Main.screenPosition 转成世界坐标；scale 放大到格子宽。
    /// </summary>
    private class PortraitElement : UIElement
    {
        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            var player = Main.LocalPlayer;
            if (player == null) return;

            var d = GetDimensions();
            bool oldInanimate = player.isDisplayDollOrInanimate;
            player.isDisplayDollOrInanimate = true;
            try
            {
                // 玩家脚底在格子底部中央；世界坐标 = UI 坐标 + 屏幕位置
                float scale = d.Width / 48f;
                var uiPos = new Vector2(d.X + d.Width / 2f, d.Y + d.Height - 6f);
                var worldPos = uiPos + Main.screenPosition;
                Main.PlayerRenderer.DrawPlayer(Main.Camera, player, worldPos, 0f, Vector2.Zero, 0f, scale);
            }
            catch (Exception e)
            {
                global::Looteria.Looteria.Instance?.Logger.Error("PortraitElement draw failed", e);
            }
            finally
            {
                player.isDisplayDollOrInanimate = oldInanimate;
            }
        }
    }
}
