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
/// - 左：真实玩家渲染（PlayerRenderer）+ 力量/套装概览 + 坐骑/宠物
/// - 中左：装备+武器槽（头盔/胸甲/腿甲/手持）
/// - 中右：饰品槽（armor 3-9，未解锁打叉变暗）
/// - 右：详细属性列表（图标 + 名称 + 值，垂直对齐）
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
    private static readonly Color C_Locked = new(60, 62, 78);

    private UIPanel _root = null!;
    private UIPanel _left = null!;
    private UIPanel _equip = null!;
    private UIPanel _acc = null!;
    private UIPanel _right = null!;
    private PortraitElement _portrait = null!;

    private static readonly Dictionary<string, Asset<Texture2D>> _statIcons = new();

    private static Asset<Texture2D> StatIcon(string key)
    {
        if (!_statIcons.TryGetValue(key, out var a))
        {
            try { a = _statIcons[key] = ModContent.Request<Texture2D>($"Looteria/Content/UI/CharacterSheet/{key}"); }
            catch { a = _statIcons[key] = ModContent.Request<Texture2D>("Looteria/Content/UI/PowerIcon"); }
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
            Width = new StyleDimension(1080f, 0f),
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

        var title = new UIText(T("CharSheetTitle"), 1.15f) { HAlign = 0.5f, Top = new StyleDimension(6f, 0f), TextColor = C_Accent };
        _root.Append(title);

        var player = Main.LocalPlayer;
        var lp = player.GetModPlayer<LooteriaPlayer>();

        // ===== 左：角色 + 概览 + 坐骑/宠物 =====
        _left = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(10f, 0f),
            Width = new StyleDimension(230f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_left);

        // 真实玩家渲染（全身）
        _portrait = new PortraitElement
        {
            HAlign = 0.5f,
            Top = new StyleDimension(8f, 0f),
            Width = new StyleDimension(150f, 0f),
            Height = new StyleDimension(170f, 0f)
        };
        _left.Append(_portrait);

        int top = 182;

        // 力量（图标与文字垂直居中对齐）
        AddRow(_left, "Stat_GearPower", T("Power") + ": " + lp.GearPower, ref top, C_Accent,
            Language.GetTextValue("Mods.Looteria.UI.PowerTip", lp.GearPower));
        top += 4;

        // 套装概览
        var counts = SetCounts();
        if (counts.Count == 0)
            AddRow(_left, "Stat_SetBonus", T("SetNone"), ref top, C_Dim, "");
        else
            foreach (var kv in counts)
            {
                string themeName = Language.GetTextValue($"Mods.Looteria.Theme.{kv.Key}");
                AddRow(_left, "Stat_SetBonus", $"{themeName} ×{kv.Value}{(kv.Value >= 2 ? " ✓" : "")}", ref top,
                    kv.Value >= 2 ? C_Green : C_Dim, "");
            }
        top += 4;
        AddRow(_left, "Stat_SetBonus", T("SetBonusHint"), ref top, C_Dim, "");

        // 坐骑/宠物（角色最下方，miscEquips: 0=宠物 3=坐骑）
        top += 6;
        AddSection(_left, T("SlotMount"), ref top);
        var pet = player.miscEquips[0];
        var mount = player.miscEquips[3];
        AddRow(_left, "Stat_Life", T("SlotPet") + ": " + (pet.IsAir ? T("Empty") : pet.Name), ref top, pet.IsAir ? C_Dim : Color.White, "");
        AddRow(_left, "Stat_MoveSpeed", T("SlotMount") + ": " + (mount.IsAir ? T("Empty") : mount.Name), ref top, mount.IsAir ? C_Dim : Color.White, "");

        // ===== 中左：装备 + 武器 =====
        _equip = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(248f, 0f),
            Width = new StyleDimension(120f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_equip);

        BuildEquipColumn(_equip, new (int, string)[]
        {
            (0, T("SlotHead")), (1, T("SlotChest")), (2, T("SlotLegs")),
        }, 0, isArmor: true);
        // 手持放底部
        int heldTop = 12 + 3 * 58;
        AddSlot(_equip, -1, T("SlotHeld"), ref heldTop, 0, locked: false);

        // ===== 中右：饰品（armor 3-9，未解锁叉+变暗）=====
        _acc = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(376f, 0f),
            Width = new StyleDimension(120f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_acc);

        int accTop = 12;
        for (int s = 3; s <= 9; s++)
        {
            bool unlocked = player.IsItemSlotUnlockedAndUsable(s);
            AddSlot(_acc, s, T("SlotAcc"), ref accTop, s - 3, locked: !unlocked);
        }

        // ===== 右：属性列表（贴右边缘）=====
        _right = new UIPanel
        {
            Top = new StyleDimension(44f, 0f),
            Left = new StyleDimension(-10f, 1f),
            Width = new StyleDimension(560f, 0f),
            Height = new StyleDimension(-58f, 1f),
            BackgroundColor = C_SubBg,
            BorderColor = new Color(50, 54, 70)
        };
        _root.Append(_right);

        BuildStats(player, lp);
    }

    /// <summary>装备槽列（固定 52px 格子），标签在格子上方。</summary>
    private static void BuildEquipColumn(UIPanel panel, (int Slot, string Label)[] slots, float startX, bool isArmor)
    {
        int y = 12;
        foreach (var (slot, label) in slots)
            AddSlot(panel, slot, label, ref y, startX, locked: false);
    }

    /// <summary>单个槽：标签 + 物品格；锁定槽打叉变暗。</summary>
    private static void AddSlot(UIPanel panel, int slot, string label, ref int top, float startX, bool locked)
    {
        var player = Main.LocalPlayer;
        var lbl = new UIText(label, 0.6f)
        {
            Top = new StyleDimension(top - 15f, 0f),
            Left = new StyleDimension(startX + 2f, 0f),
            TextColor = locked ? C_Locked : C_Dim
        };
        panel.Append(lbl);

        var item = slot >= 0 && slot < player.armor.Length ? player.armor[slot] : player.HeldItem;
        var ui = new UIItemSlot(slot, item)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(startX, 0f)
        };
        if (!locked && slot >= 0 && item.TryGetGlobalItem(out AffixGlobalItem ag) && ag.HasAffix)
            ui.RarityHighlight = (int)ag.Rarity;
        ui.OnHover = ShowHoverItemTooltip;
        ui.OnSlotClicked += _ => { };
        // 锁定槽：变暗 + 打叉
        if (locked)
            ui.OnDrawOverride = sb =>
            {
                var d = ui.GetDimensions();
                var px = TextureAssets.MagicPixel.Value;
                sb.Draw(px, d.ToRectangle(), new Color(20, 22, 34) * 0.75f); // 变暗
                // 叉
                var r = d.ToRectangle();
                sb.Draw(px, new Rectangle(r.X + 8, r.Y + 24, r.Width - 16, 3), C_Locked);
                sb.Draw(px, new Rectangle(r.X + 8, r.Y + 24, 3, r.Height - 48), C_Locked);
                sb.Draw(px, new Rectangle(r.X + r.Width - 11, r.Y + 24, 3, r.Height - 48), C_Locked);
                sb.Draw(px, new Rectangle(r.X + 8, r.Y + r.Height - 27, r.Width - 16, 3), C_Locked);
            };
        panel.Append(ui);
        top += 58;
    }

    /// <summary>角色肖像旁/概览的图标+文本行（图标与文字垂直居中对齐）。</summary>
    private static void AddRow(UIPanel panel, string iconKey, string text, ref int top, Color color, string tooltip)
    {
        var img = new UIImage(StatIcon(iconKey))
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(26f, 0f),
            Height = new StyleDimension(26f, 0f),
            VAlign = 0.5f
        };
        img.ImageScale = 0.8f;
        panel.Append(img);

        var t = new UIText(text, 0.75f)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(40f, 0f),
            TextColor = color
        };
        panel.Append(t);
        if (tooltip.Length > 0)
            t.OnMouseOver += (_, _) => { Main.LocalPlayer.mouseInterface = true; Main.instance.MouseText(tooltip); };
        top += 26;
    }

    /// <summary>属性行：图标 + 名称 + 值，图标与文本垂直居中对齐。</summary>
    private static void AddStat(UIPanel panel, string iconKey, string name, string value, ref int top, Color color, string tooltip = "")
    {
        var img = new UIImage(StatIcon(iconKey))
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(6f, 0f),
            Width = new StyleDimension(24f, 0f),
            Height = new StyleDimension(24f, 0f),
            VAlign = 0.5f
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
    /// 角色肖像：真实玩家渲染。DrawPlayer 的 position 是【世界坐标】，
    /// 世界→屏幕偏移用 Main.Camera.UnscaledPosition（比 screenPosition 可靠，UI 模式同样有效）。
    /// isDisplayDollOrInanimate=true 避免世界光照。
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
                float scale = d.Width / 48f;
                var uiPos = new Vector2(d.X + d.Width / 2f, d.Y + d.Height - 4f);
                var worldPos = uiPos + Main.Camera.UnscaledPosition;
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
