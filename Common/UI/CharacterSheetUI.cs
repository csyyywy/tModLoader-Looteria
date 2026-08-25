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
/// 角色属性面板（刷宝游戏样式）。按键 C 打开。
/// 布局（root 1080 宽、顶部 44 起始）：
///   [左 10..250]  角色立绘 + 力量/套装 + 坐骑/宠物
///   [中1 260..375] 装备：头盔/胸甲/腿甲/手持
///   [中2 385..500] 饰品：armor 3-9（未解锁变暗打叉）
///   [右 510..1070] 属性列表（贴右边缘）
/// 所有元素用绝对像素 Top/Left，槽位纵向排，图标与文本同 top 横向并排（不用 VAlign）。
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

    // 布局常量
    private const float ROOT_W = 1080f;
    private const float PANEL_TOP = 44f;
    private const float ROOT_H = -60f; // 高 = 父高 - 60
    private const float SIDE_W = 246f; // 左面板
    private const float EQUIP_X = 260f, EQUIP_W = 114f;
    private const float ACC_X = 384f, ACC_W = 114f;
    private const float RIGHT_X = 510f, RIGHT_W = 556f;

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
            Width = new StyleDimension(ROOT_W, 0f),
            Height = new StyleDimension(ROOT_H, 1f),
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

        // ===== 左：角色立绘 + 力量/套装 + 坐骑/宠物 =====
        _left = MakePanel(10f, SIDE_W);
        _root.Append(_left);

        _portrait = new PortraitElement
        {
            HAlign = 0.5f,
            Top = new StyleDimension(8f, 0f),
            Width = new StyleDimension(SIDE_W - 20f, 0f),
            Height = new StyleDimension(180f, 0f)
        };
        _left.Append(_portrait);

        int leftTop = 196;
        AddRow(_left, "Stat_GearPower", T("Power") + ": " + lp.GearPower, ref leftTop, C_Accent,
            Language.GetTextValue("Mods.Looteria.UI.PowerTip", lp.GearPower));
        leftTop += 4;

        var counts = SetCounts();
        if (counts.Count == 0)
            AddRow(_left, "Stat_SetBonus", T("SetNone"), ref leftTop, C_Dim, "");
        else
            foreach (var kv in counts)
            {
                string themeName = Language.GetTextValue($"Mods.Looteria.Theme.{kv.Key}");
                AddRow(_left, "Stat_SetBonus", $"{themeName} ×{kv.Value}{(kv.Value >= 2 ? " ✓" : "")}", ref leftTop,
                    kv.Value >= 2 ? C_Green : C_Dim, "");
            }
        leftTop += 4;
        AddRow(_left, "Stat_SetBonus", T("SetBonusHint"), ref leftTop, C_Dim, "");

        leftTop += 6;
        AddSection(_left, T("SlotMount"), ref leftTop);
        var pet = player.miscEquips[0];
        var mount = player.miscEquips[3];
        AddRow(_left, "Stat_Life", T("SlotPet") + ": " + (pet.IsAir ? T("Empty") : pet.Name), ref leftTop, pet.IsAir ? C_Dim : Color.White, "");
        AddRow(_left, "Stat_MoveSpeed", T("SlotMount") + ": " + (mount.IsAir ? T("Empty") : mount.Name), ref leftTop, mount.IsAir ? C_Dim : Color.White, "");

        // ===== 中左：装备 + 武器（纵向）=====
        _equip = MakePanel(EQUIP_X, EQUIP_W);
        _root.Append(_equip);
        int eqTop = 12;
        AddSlot(_equip, 0, T("SlotHead"), ref eqTop, 0, locked: false);
        AddSlot(_equip, 1, T("SlotChest"), ref eqTop, 0, locked: false);
        AddSlot(_equip, 2, T("SlotLegs"), ref eqTop, 0, locked: false);
        AddSlot(_equip, -1, T("SlotHeld"), ref eqTop, 0, locked: false);

        // ===== 中右：饰品（armor 3-9，未解锁叉+变暗；纵向）=====
        _acc = MakePanel(ACC_X, ACC_W);
        _root.Append(_acc);
        int accTop = 12;
        for (int s = 3; s <= 9; s++)
        {
            bool unlocked = player.IsItemSlotUnlockedAndUsable(s);
            AddSlot(_acc, s, T("SlotAcc"), ref accTop, 0, locked: !unlocked);
        }

        // ===== 右：属性列表（贴右边缘）=====
        _right = MakePanel(RIGHT_X, RIGHT_W);
        _root.Append(_right);
        BuildStats(player, lp);
    }

    private UIPanel MakePanel(float left, float width) => new()
    {
        Top = new StyleDimension(PANEL_TOP, 0f),
        Left = new StyleDimension(left, 0f),
        Width = new StyleDimension(width, 0f),
        Height = new StyleDimension(ROOT_H, 1f),
        BackgroundColor = C_SubBg,
        BorderColor = new Color(50, 54, 70)
    };

    /// <summary>单个槽：标签在上，物品格在下；锁定槽变暗+打叉。纵向排（startX 固定）。</summary>
    private static void AddSlot(UIPanel panel, int slot, string label, ref int top, float startX, bool locked)
    {
        var player = Main.LocalPlayer;
        var lbl = new UIText(label, 0.6f)
        {
            Top = new StyleDimension(top - 14f, 0f),
            Left = new StyleDimension(startX + 4f, 0f),
            TextColor = locked ? C_Locked : C_Dim
        };
        panel.Append(lbl);

        var item = slot >= 0 && slot < player.armor.Length ? player.armor[slot] : player.HeldItem;
        var ui = new UIItemSlot(slot, item)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(startX + 2f, 0f)
        };
        if (!locked && slot >= 0 && item.TryGetGlobalItem(out AffixGlobalItem ag) && ag.HasAffix)
            ui.RarityHighlight = (int)ag.Rarity;
        ui.OnHover = ShowHoverItemTooltip;
        ui.OnSlotClicked += _ => { };
        if (locked)
        {
            var dim = ui.GetDimensions();
            ui.OnDrawOverride = sb =>
            {
                var d = ui.GetDimensions();
                var px = TextureAssets.MagicPixel.Value;
                sb.Draw(px, d.ToRectangle(), new Color(20, 22, 34) * 0.8f);
                var r = d.ToRectangle();
                int th = 4, cx = r.Center.X, cy = r.Center.Y;
                // 对角叉
                sb.Draw(px, new Rectangle(cx - 12, cy - 2, 24, th), C_Locked);
                sb.Draw(px, new Rectangle(cx - 2, cy - 12, th, 24), C_Locked);
            };
        }
        panel.Append(ui);
        top += 56;
    }

    /// <summary>概览/属性行：图标(26px) + 文本，同 top 横向并排（不用 VAlign）。</summary>
    private static void AddRow(UIPanel panel, string iconKey, string text, ref int top, Color color, string tooltip)
    {
        AddStat(panel, iconKey, text, "", ref top, color, tooltip);
    }

    /// <summary>属性行：图标(24px) + 名称 + 值，同 top 横向并排（不用 VAlign）。</summary>
    private static void AddStat(UIPanel panel, string iconKey, string name, string value, ref int top, Color color, string tooltip = "")
    {
        var img = new UIImage(StatIcon(iconKey))
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(6f, 0f),
            Width = new StyleDimension(24f, 0f),
            Height = new StyleDimension(24f, 0f)
        };
        img.ImageScale = 0.7f;
        img.AllowResizingDimensions = false;
        panel.Append(img);

        var row = new UIText(string.IsNullOrEmpty(value) ? name : $"{name}: {value}", 0.7f)
        {
            Top = new StyleDimension(top + 3f, 0f), // 图标盒 24 + 文字略下移 = 垂直居中
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

    /// <summary>右侧属性列表：实时 Player 值（含全部增益/套装/宝石）。</summary>
    private void BuildStats(Player p, LooteriaPlayer lp)
    {
        int top = 10;
        AddSection(_right, T("StatSectionCombat"), ref top);

        float dmgBonus = p.GetDamage(DamageClass.Generic).Additive * 100f;
        float critChance = p.GetCritChance(DamageClass.Generic);
        AddStat(_right, "Stat_Damage", T("StatDamage"), $"+{dmgBonus:0.#}%", ref top, Color.White);
        AddStat(_right, "Stat_CritChance", T("StatCritChance"), $"{critChance:0.#}%", ref top, C_Orange);
        AddStat(_right, "Stat_CritDamage", T("StatCritDamage"), $"+{lp.PassiveCritDamage:0.#}%", ref top, C_Orange);
        AddStat(_right, "Stat_AttackSpeed", T("StatAttackSpeed"), $"+{p.GetAttackSpeed(DamageClass.Generic) * 100f:0.#}%", ref top, C_Green);

        top += 4;
        AddSection(_right, T("StatSectionDefense"), ref top);

        AddStat(_right, "Stat_Life", T("StatLife"), $"{p.statLifeMax2}", ref top, Color.White);
        AddStat(_right, "Stat_Mana", T("StatMana"), $"{p.statManaMax2}", ref top, Color.White);
        AddStat(_right, "Stat_Defense", T("StatDefense"), $"{p.statDefense}", ref top, Color.White);
        AddStat(_right, "Stat_DamageReduction", T("StatDamageReduction"), $"{p.endurance * 100f:0.#}%", ref top, C_Green);
        AddStat(_right, "Stat_LifeRegen", T("StatLifeRegen"), $"{p.lifeRegen / 2f:0.#}/s", ref top, C_Green);
        AddStat(_right, "Stat_ManaRegen", T("StatManaRegen"), $"{p.manaRegenBonus}", ref top, C_Cyan);

        top += 4;
        AddSection(_right, T("StatSectionMobility"), ref top);

        AddStat(_right, "Stat_MoveSpeed", T("StatMoveSpeed"), $"{p.moveSpeed * 100f:0.#}%", ref top, C_Cyan);

        top += 4;
        AddSection(_right, T("StatSectionOnHit"), ref top);

        AddStat(_right, "Stat_Life", T("StatLifeOnHit"), $"+{lp.PassiveLifeOnHit:0.#}", ref top, C_Green);
        AddStat(_right, "Stat_Mana", T("StatManaOnHit"), $"+{lp.PassiveManaOnHit}", ref top, C_Cyan);

        top += 4;
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
    /// 世界→屏幕偏移用 Main.Camera.UnscaledPosition。isDisplayDollOrInanimate=true 避免世界光照。
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
