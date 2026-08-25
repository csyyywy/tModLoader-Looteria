using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader.UI;
using Terraria.UI;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.UI;

/// <summary>共享 UI 组件：小节标题 / 标签 / 按钮 / 物品网格 / 词缀格式化 / 钱币文案。</summary>
public partial class LooteriaUIState
{
    // ===== 小节标题 =====
    private static void AddSectionTitle(UIPanel panel, string text, Color color, ref int top)
    {
        AddLabel(panel, text, ref top, 0.9f, color);
        // 分隔线画在文字下方（文字高约 20px），避免压住文字
        panel.Append(new UILine
        {
            Top = new StyleDimension(top + 21f, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(-16f, 1f),
            Height = new StyleDimension(2f, 0f),
            LineColor = color
        });
        top += 26;
    }

    private static UIText AddLabel(UIPanel panel, string text, ref int top, float scale, Color color)
    {
        var t = new UIText(text, scale) { Top = new StyleDimension(top, 0f), Left = new StyleDimension(8f, 0f), TextColor = color };
        panel.Append(t);
        return t;
    }

    private static void AddButton(UIPanel panel, string text, ref int top, Action onClick, Color? bg = null)
    {
        var b = new UITextPanel<string>(text, 0.75f)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f),
            Width = new StyleDimension(300f, 0f),
            Height = new StyleDimension(30f, 0f)
        };
        if (bg.HasValue) b.BackgroundColor = bg.Value;
        b.WithFadedMouseOver();
        b.OnLeftClick += (_, _) => onClick();
        panel.Append(b);
        top += 36;
    }

    /// <summary>
    /// 物品网格：显示背包里所有"可附加词缀/已带词缀"物品，最多 4 行、可滚动（支持大背包 mod）。
    /// 返回网格底部 Y（供后续布局使用）。
    /// </summary>
    private int AddInventoryGrid(int top, Action<UIItemSlot> onClick)
    {
        var player = Main.LocalPlayer;
        var items = new List<(int Slot, bool Affix, LootRarity Rarity)>();
        for (int i = 0; i < player.inventory.Length; i++)
        {
            var item = player.inventory[i];
            if (item == null || item.IsAir) continue;
            bool hasAffix = item.TryGetGlobalItem(out AffixGlobalItem g) && g.HasAffix;
            if (!hasAffix && !ItemClassifier.IsEligible(item)) continue;
            items.Add((i, hasAffix, hasAffix ? g.Rarity : LootRarity.None));
        }
        if (items.Count == 0)
        {
            AddLabel(_content, T("NoEligible"), ref top, 0.8f, Color.Gray);
            return top + 24;
        }

        int rows = (items.Count + 9) / 10;
        float listH = Math.Min(rows, 4) * 54f;
        var list = new UIList
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(6f, 0f),
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
            var row = new UIElement { Width = new StyleDimension(580f, 0f), Height = new StyleDimension(52f, 0f) };
            for (int k = 0; k < 10; k++)
            {
                int n = r * 10 + k;
                if (n >= items.Count) break;
                var (slot, affix, rarity) = items[n];
                var it = player.inventory[slot];
                var s = new UIItemSlot(slot, it)
                {
                    Top = new StyleDimension(0f, 0f),
                    Left = new StyleDimension(k * 56f, 0f)
                };
                s.Selected = slot == _selectedSlot;
                if (affix) s.RarityHighlight = (int)rarity;
                s.OnSlotClicked += onClick;
                row.Append(s);
            }
            list.Add(row);
        }
        _content.Append(list);
        _content.Append(scrollbar);
        list.Recalculate();
        return top + (int)listH + 8;
    }

    // ===== 词缀格式化 =====
    private static List<string> FormatAffixLines(AffixGlobalItem g)
    {
        var list = new List<string>();
        if (g.Affixes == null) return list;
        for (int i = 0; i < g.Affixes.Count; i++) list.Add(FormatAffixLine(g, i));
        return list;
    }

    private static string FormatAffixLine(AffixGlobalItem g, int i)
    {
        var r = g.Affixes![i];
        var def = AffixDatabase.GetById(r.AffixId);
        return def == null ? $"?({r.AffixId})" : FormatAffix(def, r.Value); // L1：未知 id 显示占位
    }

    private static string FormatAffix(AffixDef def, float v)
        => Language.GetTextValue($"Mods.Looteria.Affix.{def.Key}", FormatValue(def, v));

    private static string FormatValue(AffixDef def, float v)
        => def.IsPercent ? $"+{v:0}%" : $"+{v:0.#}";

    /// <summary>钱币文案：铜币 → 金/银/铜。</summary>
    private static string CoinText(int copper)
    {
        if (copper <= 0) return "0";
        int g = copper / 10000, s = (copper % 10000) / 100, c = copper % 100;
        string r = "";
        if (g > 0) r += $"{g}{T("CoinGold")}";
        if (s > 0) r += $"{s}{T("CoinSilver")}";
        if (c > 0 || r.Length == 0) r += $"{c}{T("CoinCopper")}";
        return r;
    }

    /// <summary>强调色分隔线。</summary>
    private class UILine : UIElement
    {
        public Color LineColor = new(255, 200, 0);
        protected override void DrawSelf(SpriteBatch sb)
        {
            var d = GetDimensions();
            var px = TextureAssets.MagicPixel.Value;
            sb.Draw(px, new Rectangle((int)d.X, (int)d.Y, (int)d.Width, Math.Max(1, (int)d.Height)), LineColor);
        }
    }

    /// <summary>进度条（可带文字）。</summary>
    private class UIBar : UIElement
    {
        public float Fraction;
        public Color FillColor = new(80, 200, 255);
        public Color BgColor = new(16, 18, 28);
        public string Text = "";
        public Color TextColor = Color.White;
        protected override void DrawSelf(SpriteBatch sb)
        {
            var d = GetDimensions();
            var px = TextureAssets.MagicPixel.Value;
            sb.Draw(px, new Rectangle((int)d.X, (int)d.Y, (int)d.Width, (int)d.Height), BgColor);
            int w = (int)(d.Width * Math.Clamp(Fraction, 0f, 1f));
            if (w > 0)
                sb.Draw(px, new Rectangle((int)d.X, (int)d.Y, w, (int)d.Height), FillColor);
            if (Text.Length > 0)
            {
                var font = FontAssets.MouseText.Value;
                var sz = font.MeasureString(Text);
                Utils.DrawBorderStringFourWay(sb, font, Text,
                    d.X + d.Width / 2f - sz.X / 2f,
                    d.Y + d.Height / 2f - sz.Y / 2f,
                    TextColor, Color.Black, Vector2.Zero);
            }
        }
    }
}
