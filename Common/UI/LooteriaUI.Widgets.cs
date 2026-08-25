using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.UI;
using Terraria.UI;
using global::Looteria.Common.Configs;
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
                s.OnHover = ShowHoverItemTooltip;
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

    /// <summary>插槽宝石格式化：每槽一行（"◆ 宝石名 +N"或"◇ 空"）。值编码 = gemId + upgrade×1000。</summary>
    private static List<string> FormatSocketLines(AffixGlobalItem g)
    {
        var list = new List<string>();
        if (g == null || g.SocketCount <= 0) return list;
        for (int i = 0; i < g.SocketCount; i++)
        {
            int sockVal = (g.Sockets != null && i < g.Sockets.Count) ? g.Sockets[i] : 0;
            if (sockVal > 0)
            {
                int gemId = sockVal % 1000;
                int gemUp = sockVal / 1000;
                list.Add("◆ " + Language.GetTextValue(GemDatabase.Key(gemId))
                       + (gemUp > 0 ? $" +{gemUp}" : ""));
            }
            else
            {
                list.Add("◇ " + T("Empty"));
            }
        }
        return list;
    }

    /// <summary>钱币文案（含铂金；纯文本兜底，UI 优先用图标版 UICoinLine/UICoinButton）。</summary>
    private static string CoinText(int copper)
    {
        if (copper <= 0) return "0";
        var (p, g, s, c) = SplitCoins(copper);
        string r = "";
        if (p > 0) r += $"{p}{T("CoinPlatinum")}";
        if (g > 0) r += $"{g}{T("CoinGold")}";
        if (s > 0) r += $"{s}{T("CoinSilver")}";
        if (c > 0 || r.Length == 0) r += $"{c}{T("CoinCopper")}";
        return r;
    }

    /// <summary>把铜币数拆成 铂/金/银/铜 四档（值 = 100 进制，1铂=100金=10000银=1000000铜）。
    /// ⚠️ 单位换算恒为 100 进制，与"花销除以 50"的调价互不相干——别把两者混为一谈。</summary>
    private static (int P, int G, int S, int C) SplitCoins(int copper)
    {
        int p = copper / 1000000; copper %= 1000000;
        int g = copper / 10000; copper %= 10000;
        int s = copper / 100; int c = copper % 100;
        return (p, g, s, c);
    }

    /// <summary>钱币费用 = 装备价值 ÷ 除数（每项独立配置，最低 1 铜）。</summary>
    private static int CoinCost(int value, int divisor) => Math.Max(1, value / Math.Max(1, divisor));

    /// <summary>重铸单条钱币费用（配置 RerollOneCoinDiv）。</summary>
    private static int RerollOneCoins(int value) => CoinCost(value, LooteriaConfig.Instance?.RerollOneCoinDiv ?? 50);

    /// <summary>全部重铸钱币费用（配置 RerollAllCoinDiv）。</summary>
    private static int RerollAllCoins(int value) => CoinCost(value, LooteriaConfig.Instance?.RerollAllCoinDiv ?? 100);

    /// <summary>稀有度升档钱币费用（配置 UpgradeCoinDiv）。</summary>
    private static int UpgradeCoins(int value) => CoinCost(value, LooteriaConfig.Instance?.UpgradeCoinDiv ?? 50);

    /// <summary>开槽钱币费用（配置 SocketCoinDiv）。</summary>
    private static int SocketCoins(int value) => CoinCost(value, LooteriaConfig.Instance?.SocketCoinDiv ?? 100);

    /// <summary>宝石升阶钱币费用（配置 GemUpgradeCoinDiv）。</summary>
    private static int GemUpgradeCoins(int value) => CoinCost(value, LooteriaConfig.Instance?.GemUpgradeCoinDiv ?? 50);

    /// <summary>玩家当前全部钱币（铜币，含背包钱币栏 + 4 个银行，同原版商店 CanAfford 口径）。</summary>
    private static long PlayerCoins(Player player)
    {
        bool over;
        long inv = Utils.CoinsCount(out over, player.inventory, 58, 57, 56, 55, 54); // 钱币栏（50-53）
        long b1 = Utils.CoinsCount(out over, player.bank.item);    // 猪猪存钱罐
        long b2 = Utils.CoinsCount(out over, player.bank2.item);   // 保险箱
        long b3 = Utils.CoinsCount(out over, player.bank3.item);   // 护甲假人（锻造台）
        long b4 = Utils.CoinsCount(out over, player.bank4.item);   // 虚空保险库
        long total = inv + b1 + b2 + b3 + b4;
        return total > int.MaxValue ? int.MaxValue : total;
    }

    /// <summary>在 sb 上连续绘制 "数量 + 原版钱币图标"（铂/金/银/铜），返回结束 X。替代汉字单位。</summary>
    private static float DrawCoinIcons(SpriteBatch sb, DynamicSpriteFont font, float x, float y, int copper, Color textColor, float scale = 0.8f)
    {
        var (p, g, s, c) = SplitCoins(copper);
        int[] counts = { p, g, s, c };
        int[] itemTypes = { ItemID.PlatinumCoin, ItemID.GoldCoin, ItemID.SilverCoin, ItemID.CopperCoin };
        for (int i = 0; i < 4; i++)
        {
            if (counts[i] <= 0) continue;
            string num = counts[i].ToString();
            var sz = font.MeasureString(num) * scale;
            Utils.DrawBorderStringFourWay(sb, font, num, x, y, textColor, Color.Black, Vector2.Zero, scale);
            x += sz.X + 2f;
            // 原版钱币物品图标
            try
            {
                Main.instance.LoadItem(itemTypes[i]);
                var tex = TextureAssets.Item[itemTypes[i]].Value;
                float iconH = 16f * scale;
                float iconW = tex.Width * (iconH / tex.Height);
                sb.Draw(tex, new Rectangle((int)x, (int)(y - 2f), (int)iconW, (int)iconH), Color.White);
                x += iconW + 4f;
            }
            catch { x += 12f; } // 贴图缺失：留空继续
        }
        return x;
    }

    /// <summary>文字行 + 原版钱币图标（铂/金/银/铜）。替代 "12金34银" 的汉字写法。</summary>
    private class UICoinLine : UIElement
    {
        public string Prefix = "";
        public int Copper;
        public Color TextColor = Color.White;
        public float Scale = 0.8f;

        public UICoinLine(string prefix, int copper, Color color, float scale = 0.8f)
        {
            Prefix = prefix; Copper = copper; TextColor = color; Scale = scale;
            Width = new StyleDimension(600f, 0f);
            Height = new StyleDimension(24f, 0f);
        }

        protected override void DrawSelf(SpriteBatch sb)
        {
            base.DrawSelf(sb);
            var d = GetDimensions();
            var font = FontAssets.MouseText.Value;
            float x = d.X;
            float y = d.Y + 4f;
            if (Prefix.Length > 0)
            {
                Utils.DrawBorderStringFourWay(sb, font, Prefix, x, y, TextColor, Color.Black, Vector2.Zero, Scale);
                x += font.MeasureString(Prefix).X * Scale + 2f;
            }
            DrawCoinIcons(sb, font, x, y, Copper, TextColor, Scale);
        }
    }

    /// <summary>带原版钱币图标的行标签（前缀 + 钱币），自动换行高度推进。替代 AddLabel + CoinText。</summary>
    private static void AddCoinLabel(UIPanel panel, string prefix, int copper, ref int top, float scale, Color color)
    {
        panel.Append(new UICoinLine(prefix, copper, color, scale)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f)
        });
        top += (int)(26f * scale) + 6;
    }

    /// <summary>
    /// 原版圆角样式钱币按钮：继承 UIPanel → 自带原版 PanelBackground/PanelBorder 九宫格圆角贴图；
    /// 鼠标悬停显示"花费 [原版钱币图标]"悬浮框（原版 tooltip 背景框 + 铂/金/银/铜图标，随装备价值实时变化）。
    /// </summary>
    private class UICoinButton : UIPanel
    {
        public string Label = "";
        public int Copper;
        public Color TextColor = Color.White;
        public float Scale = 0.75f;

        private readonly Color _baseBg;
        private readonly Color _baseBorder;

        public UICoinButton(string label, int copper, Color bg, Action onClick, float scale = 0.75f)
        {
            Label = label; Copper = copper; Scale = scale;
            _baseBg = bg;
            _baseBorder = new Color(70, 74, 96);
            BackgroundColor = _baseBg;
            BorderColor = _baseBorder;
            Width = new StyleDimension(480f, 0f);
            Height = new StyleDimension(36f, 0f);
            SetPadding(6f);
            OnLeftClick += (_, _) => onClick();
            // 原版悬停反馈：亮背景 + 亮边框（与 tML 其它按钮一致）
            OnMouseOver += (_, _) =>
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
                BackgroundColor = Color.Lerp(_baseBg, Color.White, 0.18f);
                BorderColor = Color.Lerp(_baseBorder, Color.White, 0.3f);
            };
            OnMouseOut += (_, _) =>
            {
                BackgroundColor = _baseBg;
                BorderColor = _baseBorder;
            };
        }

        /// <summary>更新钱币花销（悬停悬浮框内容跟随变化）。</summary>
        public void SetCost(int copper) => Copper = copper;

        protected override void DrawSelf(SpriteBatch sb)
        {
            // 原版圆角九宫格（UIPanel.DrawSelf 用 PanelBackground/PanelBorder 贴图，随 BackgroundColor/BorderColor 上色）
            base.DrawSelf(sb);

            var d = GetDimensions();
            var font = FontAssets.MouseText.Value;
            float x = d.X + 10f;
            float y = d.Y + Math.Max(2f, (d.Height - font.MeasureString(Label).Y * Scale) / 2f);
            if (Label.Length > 0)
            {
                Utils.DrawBorderStringFourWay(sb, font, Label, x, y, TextColor, Color.Black, Vector2.Zero, Scale);
            }
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            // 悬浮框不在按钮内直接画——子元素按顺序绘制，下方按钮会盖住它。
            // 登记到面板状态，由 LooteriaUIState.Draw 在整棵 UI 树画完后最后绘制（永远最上层）。
            if (IsMouseHovering && Copper > 0)
                Instance._hoverCoinTooltip = Copper;
        }
    }

    /// <summary>带原版圆角样式 + 悬停悬浮框显示钱币花销的按钮（替代 AddButton + CoinText 汉字）。</summary>
    private static void AddCoinButton(UIPanel panel, string label, int copper, ref int top, Action onClick, Color? bg = null)
    {
        panel.Append(new UICoinButton(label, copper, bg ?? new Color(60, 90, 150), onClick)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f)
        });
        top += 42;
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
