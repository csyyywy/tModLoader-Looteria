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
/// 布局（root 1080 宽，参考目标草图——立柱式）：
///   [左 24..116]   防具（上：头盔/胸甲/腿甲）+ 武器（底部）
///   [中 136..436]  人物立绘（大）
///   [中下 136..436] 坐骑/宠物（肖像正下方）
///   [中右 448..540] 饰品 armor 3-9（未解锁=叉+变暗）
///   [右 556..1064] 属性列表（总览 + 战斗/防御/机动/命中/状态免疫，可滚动）
/// 所有元素用绝对像素 Top/Left；槽位纵向排；图标与文本同 top 横向并排（不用 VAlign）。
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

    // 布局常量（root 内绝对像素）
    private const float ROOT_W = 1080f;
    private const float PANEL_TOP = 44f;       // 面板距 root 顶部（让出标题）
    private const float EQUIP_X = 24f, EQUIP_W = 92f;
    private const float PORTRAIT_X = 136f, PORTRAIT_W = 300f;
    private const float PORTRAIT_H = 0.54f;    // 肖像面板高 = root 高的 54%（余下给坐骑/宠物）
    private const float MOUNT_H = 128f;
    private const float ACC_X = 448f, ACC_W = 92f;
    private const float STATS_X = 552f, STATS_W = 528f; // 右缘贴 root 右缘（552+528=1080）

    private UIPanel _root = null!;
    private UIPanel _equip = null!;
    private UIPanel _portraitPanel = null!;
    private UIPanel _mountPanel = null!;
    private UIPanel _acc = null!;
    private UIPanel _stats = null!;
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

        // ===== 左：防具（上）+ 武器（下）=====
        _equip = MakePanel(EQUIP_X, EQUIP_W);
        _root.Append(_equip);
        {
            int top = 24; // 首槽标签从面板内 10px 开始
            AddSlot(_equip, 0, T("SlotHead"), ref top, locked: false);
            AddSlot(_equip, 1, T("SlotChest"), ref top, locked: false);
            AddSlot(_equip, 2, T("SlotLegs"), ref top, locked: false);
            // 武器固定锚在面板底部
            var wlbl = new UIText(T("SlotHeld"), 0.6f)
            {
                Top = new StyleDimension(-82f, 1f),
                Left = new StyleDimension(8f, 0f),
                TextColor = C_Dim
            };
            _equip.Append(wlbl);
            var held = new UIItemSlot(-1, player.HeldItem)
            {
                Top = new StyleDimension(-64f, 1f),
                Left = new StyleDimension(14f, 0f)
            };
            held.OnHover = ShowHoverItemTooltip;
            _equip.Append(held);
        }

        // ===== 中上：人物立绘（UseImmediateMode 必须 true：1.4.4 UI 里 DrawPlayer 在
        // Deferred 批次下会被界面队列盖住——见 tCF "Characters being drawn behind interface"）=====
        _portraitPanel = MakePanel(PORTRAIT_X, PORTRAIT_W, height: new StyleDimension(0f, PORTRAIT_H));
        _root.Append(_portraitPanel);

        _portrait = new PortraitElement
        {
            Top = new StyleDimension(6f, 0f),
            Left = new StyleDimension(6f, 0f),
            Width = new StyleDimension(-12f, 1f),
            Height = new StyleDimension(-12f, 1f)
        };
        _portraitPanel.Append(_portrait);

        // ===== 中下：坐骑/宠物（肖像面板正下方，留 8px 间隙）=====
        _mountPanel = MakePanel(PORTRAIT_X, PORTRAIT_W,
            top: new StyleDimension(PANEL_TOP + 8f, PORTRAIT_H),
            height: new StyleDimension(MOUNT_H, 0f));
        _root.Append(_mountPanel);
        {
            var hdr = new UIText(T("SlotMount"), 0.8f)
            {
                Top = new StyleDimension(6f, 0f), Left = new StyleDimension(8f, 0f), TextColor = C_Accent
            };
            _mountPanel.Append(hdr);

            var pet = player.miscEquips[0];
            var mount = player.miscEquips[3];

            var petSlot = new UIItemSlot(0, pet)
            {
                Top = new StyleDimension(30f, 0f), Left = new StyleDimension(10f, 0f)
            };
            petSlot.OnHover = ShowHoverItemTooltip;
            _mountPanel.Append(petSlot);
            _mountPanel.Append(new UIText(T("SlotPet") + "：" + (pet.IsAir ? T("Empty") : pet.Name), 0.65f)
            {
                Top = new StyleDimension(44f, 0f), Left = new StyleDimension(70f, 0f),
                TextColor = pet.IsAir ? C_Dim : Color.White
            });

            var mountSlot = new UIItemSlot(3, mount)
            {
                Top = new StyleDimension(86f, 0f), Left = new StyleDimension(10f, 0f)
            };
            mountSlot.OnHover = ShowHoverItemTooltip;
            _mountPanel.Append(mountSlot);
            _mountPanel.Append(new UIText(T("SlotMountItem") + "：" + (mount.IsAir ? T("Empty") : mount.Name), 0.65f)
            {
                Top = new StyleDimension(100f, 0f), Left = new StyleDimension(70f, 0f),
                TextColor = mount.IsAir ? C_Dim : Color.White
            });
        }

        // ===== 中右：饰品（armor 3-9，未解锁叉+变暗）=====
        _acc = MakePanel(ACC_X, ACC_W);
        _root.Append(_acc);
        {
            var hdr = new UIText(T("SlotAcc"), 0.8f)
            {
                Top = new StyleDimension(6f, 0f), Left = new StyleDimension(8f, 0f), TextColor = C_Accent
            };
            _acc.Append(hdr);
            int accTop = 30;
            for (int s = 3; s <= 9; s++)
            {
                bool unlocked = player.IsItemSlotUnlockedAndUsable(s);
                AddSlot(_acc, s, null, ref accTop, locked: !unlocked);
            }
        }

        // ===== 右：属性（总览 + 详细，可滚动）=====
        _stats = MakePanel(STATS_X, STATS_W);
        _root.Append(_stats);
        BuildStats(player, lp);
    }

    private UIPanel MakePanel(float left, float width,
        StyleDimension? top = null, StyleDimension? height = null) => new()
    {
        Top = top ?? new StyleDimension(PANEL_TOP, 0f),
        Left = new StyleDimension(left, 0f),
        Width = new StyleDimension(width, 0f),
        Height = height ?? new StyleDimension(-PANEL_TOP - 8f, 1f),
        BackgroundColor = C_SubBg,
        BorderColor = new Color(50, 54, 70)
    };

    /// <summary>单个槽：标签（可空）在上，物品格在下；锁定槽=变暗+对角叉。纵向排。</summary>
    private static void AddSlot(UIPanel panel, int slot, string? label, ref int top, bool locked)
    {
        var player = Main.LocalPlayer;
        if (label != null)
        {
            var lbl = new UIText(label, 0.6f)
            {
                Top = new StyleDimension(top - 14f, 0f),
                Left = new StyleDimension(10f, 0f),
                TextColor = locked ? C_Locked : C_Dim
            };
            panel.Append(lbl);
        }

        var item = locked ? new Item()
            : (slot >= 0 && slot < player.armor.Length ? player.armor[slot] : player.HeldItem);
        var ui = new UIItemSlot(slot, item)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(14f, 0f)
        };
        if (!locked && slot >= 0 && item.TryGetGlobalItem(out AffixGlobalItem ag) && ag.HasAffix)
            ui.RarityHighlight = (int)ag.Rarity;
        ui.OnHover = ShowHoverItemTooltip;
        ui.OnSlotClicked += _ => { };
        if (locked)
        {
            ui.OnDrawOverride = sb =>
            {
                var d = ui.GetDimensions();
                var px = TextureAssets.MagicPixel.Value;
                sb.Draw(px, d.ToRectangle(), new Color(20, 22, 34) * 0.82f);
                // 对角叉（45° 两根斜杠）
                var c = new Vector2(d.X + d.Width * 0.5f, d.Y + d.Height * 0.5f);
                for (int i = 0; i < 2; i++)
                {
                    float ang = MathHelper.PiOver4 + i * MathHelper.PiOver2;
                    sb.Draw(px, c, null, C_Locked, ang, new Vector2(0.5f), new Vector2(30f, 4f), SpriteEffects.None, 0f);
                }
            };
        }
        panel.Append(ui);
        top += 56;
    }

    /// <summary>属性行（放入滚动列表）：图标 + 名称/值，同一行精确像素对齐（不用 VAlign、不用 UIImage 缩放）。</summary>
    private static UIElement StatRow(string iconKey, string text, Color color, string tooltip = "", float h = 22f)
    {
        var row = new UIElement { Width = new StyleDimension(488f, 0f), Height = new StyleDimension(h, 0f) };
        // 图标：自定义绘制，等比缩放进 18x18 盒，与文字行顶对齐
        row.Append(new IconElement(StatIcon(iconKey))
        {
            Top = new StyleDimension(2f, 0f),
            Left = new StyleDimension(2f, 0f),
            Width = new StyleDimension(18f, 0f),
            Height = new StyleDimension(18f, 0f)
        });

        var txt = new UIText(text, 0.68f)
        {
            Top = new StyleDimension(3f, 0f),
            Left = new StyleDimension(25f, 0f),
            TextColor = color
        };
        row.Append(txt);

        if (tooltip.Length > 0)
            row.OnMouseOver += (_, _) => { Main.LocalPlayer.mouseInterface = true; Main.instance.MouseText(tooltip); };
        return row;
    }

    /// <summary>图标元素：等比缩放居中绘制（无 UIImage 的 ImageScale 中心偏移）。</summary>
    private sealed class IconElement : UIElement
    {
        private readonly Asset<Texture2D> _tex;
        public IconElement(Asset<Texture2D> tex) => _tex = tex;

        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            var tex = _tex?.Value;
            if (tex == null) return;
            var d = GetDimensions();
            if (d.Width <= 0f || d.Height <= 0f) return;
            float sc = MathF.Min(d.Width / tex.Width, d.Height / tex.Height);
            var size = tex.Size() * sc;
            sb.Draw(tex, d.Center() - size * 0.5f, null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
        }
    }

    private static UIElement SectionRow(string title, float h = 22f)
    {
        var row = new UIElement { Width = new StyleDimension(488f, 0f), Height = new StyleDimension(h, 0f) };
        row.Append(new UIText(title, 0.78f)
        {
            Top = new StyleDimension(0f, 0f), Left = new StyleDimension(2f, 0f), TextColor = C_Accent
        });
        return row;
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

    /// <summary>右侧属性列表（可滚动）：总览 + 实时 Player 值（含全部增益/套装/宝石）。</summary>
    private void BuildStats(Player p, LooteriaPlayer lp)
    {
        var list = new UIList
        {
            Top = new StyleDimension(8f, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(-30f, 1f),
            Height = new StyleDimension(-18f, 1f)
        };
        var scrollbar = new UIScrollbar
        {
            Top = new StyleDimension(8f, 0f),
            Left = new StyleDimension(-22f, 1f),
            Height = new StyleDimension(-18f, 1f)
        };
        scrollbar.SetView(100f, 1000f);
        list.SetScrollbar(scrollbar);
        // UIList 默认用 CompareTo(=0 恒等) 排序：List.Sort 不稳定，行序会被打乱 —— 必须禁用排序保持添加顺序
        list.ManualSortMethod = _ => { };
        _stats.Append(list);

        // ===== 总览 =====
        list.Add(SectionRow(T("StatSectionOverview")));
        list.Add(StatRow("Stat_GearPower",
            $"{T("Power")}: {lp.GearPower}", C_Accent,
            Language.GetTextValue("Mods.Looteria.UI.PowerTip", lp.GearPower)));
        var counts = SetCounts();
        if (counts.Count == 0)
        {
            list.Add(StatRow("Stat_SetBonus", T("SetNone"), C_Dim));
        }
        else
        {
            foreach (var kv in counts)
            {
                string themeName = Language.GetTextValue($"Mods.Looteria.Theme.{kv.Key}");
                list.Add(StatRow("Stat_SetBonus", $"{themeName} ×{kv.Value}{(kv.Value >= 2 ? " ✓" : "")}",
                    kv.Value >= 2 ? C_Green : C_Dim));
            }
        }
        list.Add(StatRow("Stat_SetBonus", T("SetBonusHint"), C_Dim));

        // ===== 详细 =====
        list.Add(SectionRow(T("StatSectionCombat")));
        float dmgBonus = p.GetDamage(DamageClass.Generic).Additive * 100f;
        float critChance = p.GetCritChance(DamageClass.Generic);
        list.Add(StatRow("Stat_Damage", $"{T("StatDamage")}: +{dmgBonus:0.#}%", Color.White));
        list.Add(StatRow("Stat_CritChance", $"{T("StatCritChance")}: {critChance:0.#}%", C_Orange));
        list.Add(StatRow("Stat_CritDamage", $"{T("StatCritDamage")}: +{lp.PassiveCritDamage:0.#}%", C_Orange));
        list.Add(StatRow("Stat_AttackSpeed", $"{T("StatAttackSpeed")}: +{p.GetAttackSpeed(DamageClass.Generic) * 100f:0.#}%", C_Green));

        list.Add(SectionRow(T("StatSectionDefense")));
        list.Add(StatRow("Stat_Life", $"{T("StatLife")}: {p.statLifeMax2}", Color.White));
        list.Add(StatRow("Stat_Mana", $"{T("StatMana")}: {p.statManaMax2}", Color.White));
        list.Add(StatRow("Stat_Defense", $"{T("StatDefense")}: {p.statDefense}", Color.White));
        list.Add(StatRow("Stat_DamageReduction", $"{T("StatDamageReduction")}: {p.endurance * 100f:0.#}%", C_Green));
        list.Add(StatRow("Stat_LifeRegen", $"{T("StatLifeRegen")}: {p.lifeRegen / 2f:0.#}/s", C_Green));
        list.Add(StatRow("Stat_ManaRegen", $"{T("StatManaRegen")}: {p.manaRegenBonus}", C_Cyan));

        list.Add(SectionRow(T("StatSectionMobility")));
        list.Add(StatRow("Stat_MoveSpeed", $"{T("StatMoveSpeed")}: {p.moveSpeed * 100f:0.#}%", C_Cyan));

        list.Add(SectionRow(T("StatSectionOnHit")));
        list.Add(StatRow("Stat_Life", $"{T("StatLifeOnHit")}: +{lp.PassiveLifeOnHit:0.#}", C_Green));
        list.Add(StatRow("Stat_Mana", $"{T("StatManaOnHit")}: +{lp.PassiveManaOnHit}", C_Cyan));

        list.Add(SectionRow(T("StatSectionResist")));
        list.Add(StatRow("Stat_Defense", $"{T("StatBuffResistPoison")}: {p.buffImmune[BuffID.Poisoned]}", C_Dim));
        list.Add(StatRow("Stat_Defense", $"{T("StatBuffResistFire")}: {p.buffImmune[BuffID.OnFire]}", C_Dim));
        list.Add(StatRow("Stat_Defense", $"{T("StatBuffResistBleed")}: {p.buffImmune[BuffID.Bleeding]}", C_Dim));
        list.Add(StatRow("Stat_Defense", $"{T("StatBuffResistCurse")}: {p.buffImmune[BuffID.CursedInferno]}", C_Dim));
        list.Add(StatRow("Stat_Defense", $"{T("StatBuffResistSlow")}: {p.buffImmune[BuffID.Slow]}", C_Dim));
    }

    /// <summary>悬停物品悬浮预览（与掠夺面板共用，每帧克隆防击退爆炸）。</summary>
    private static void ShowHoverItemTooltip(Item item)
    {
        if (item == null || item.IsAir) return;
        Main.HoverItem = item.Clone();
        Main.instance.MouseText("");
    }

    /// <summary>
    /// 角色肖像。
    /// · 用 <see cref="PortraitRenderer"/>（自研慢路径：BoringSetup → 各层 → RenderAllLayersSlow）
    ///   完全绕开 Main.PlayerRenderer 的 spriteBuffer 原始绘制问题。
    /// · 视觉克隆（clientClone）：不动真玩家的装备/增益；isDisplayDollOrInanimate 全亮。
    /// · 缩放锚点 = 碰撞盒底部中心（feet），把碰撞盒底部放到盒子底边，放大时向上长。
    /// </summary>
    private class PortraitElement : UIElement
    {
        public PortraitElement()
        {
            // 不再需要 UseImmediateMode：慢路径用普通 spriteBatch 绘制，走 UI 矩阵。
            OverrideSamplerState = SamplerState.PointClamp;
        }

        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            var d = GetDimensions();
            if (d.Width < 20f || d.Height < 20f) return;

            var player = Main.LocalPlayer;
            if (player == null) return;

            var clone = player.clientClone();
            clone.dead = false;
            clone.isDisplayDollOrInanimate = true; // 全亮肤色 + 无视隐身穿插

            float s = MathF.Max(2f, (d.Height - 24f) / 56f); // scale1 时人物约 20x56（含头）
            var uiPos = new Vector2(
                d.X + d.Width * 0.5f - clone.width * 0.5f,
                d.Y + d.Height - 10f - clone.height);

            try
            {
                using var _currentPlr = new Main.CurrentPlayerOverride(clone);
                clone.ResetEffects();
                clone.ResetVisibleAccessories();
                clone.UpdateMiscCounter();
                clone.UpdateDyes();
                clone.PlayerFrame();

                // 手动控制批次：结束外层 UI Deferred → 以 UIScaleMatrix 开 Immediate 批次绘制 →
                // 结束 → 恢复 Deferred。避免 RenderAllLayersSlow 里 SetShaderForData/Apply
                // 把坐标/着色器状态搞乱，也避免 spriteBuffer 相关的原始绘制问题。
                var uiMatrix = Main.UIScaleMatrix;
                sb.End();
                sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend,
                    SamplerState.PointClamp, DepthStencilState.None,
                    RasterizerState.CullNone, null, uiMatrix);

                PortraitRenderer.DrawPlayer(sb, clone, uiPos, s);

                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.AnisotropicClamp, DepthStencilState.None,
                    RasterizerState.CullNone, null, uiMatrix);
            }
            catch (Exception e)
            {
                global::Looteria.Looteria.Instance?.Logger.Error("PortraitElement draw failed", e);
            }
        }
    }
}
