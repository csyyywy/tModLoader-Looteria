using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
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
using global::Looteria.Common.Systems;

namespace Looteria.Common.UI;

/// <summary>
/// 掠夺面板（IngameFancyUI 全屏 UI）。7 页签：装备 / 重铸 / 镶嵌 / 升阶 / 赌博 / 秘境 / 说明。
/// 已按页签拆分成多个 partial 文件（LooteriaUI.*.cs），本文件只保留共享状态与入口。
/// </summary>
public partial class LooteriaUIState : UIState
{
    public static LooteriaUIState Instance = new();

    // —— 成本（2026-08-24 玩家定价；值可进配置调整，默认即玩家定价）——
    public static int RerollOneCost => LooteriaConfig.Instance?.RerollOneDust ?? 60;      // 重铸单条：60 尘 + 装备价值/10 铜币
    public static int RerollAllCost => LooteriaConfig.Instance?.RerollAllDust ?? 20;      // 全部重铸：20 尘 + 装备价值/20 铜币
    public static int UpgradeCost => LooteriaConfig.Instance?.UpgradeDust ?? 120;         // 升档：120 尘 + 装备价值/10 铜币 + 1 同名装备
    public static int MaxSockets => LooteriaConfig.Instance?.MaxSockets ?? 6;             // 装备总插槽上限
    public static int MaxOpenedSockets => LooteriaConfig.Instance?.MaxOpenedSockets ?? 4; // 开槽最多开的插槽数（传说/套装自带不计入）

    // —— 配色方案（设计感）——
    private static readonly Color C_Accent = new(255, 200, 0);      // 金
    private static readonly Color C_Cyan = new(80, 200, 255);       // 青
    private static readonly Color C_Pink = new(255, 105, 180);      // 玫红
    private static readonly Color C_Green = new(120, 255, 160);     // 绿
    private static readonly Color C_Orange = new(255, 150, 60);     // 橙
    private static readonly Color C_Red = new(255, 110, 110);       // 红
    private static readonly Color C_Dim = new(150, 155, 170);       // 灰
    private static readonly Color C_PanelBg = new(26, 28, 42);      // 面板底
    private static readonly Color C_Selected = new(60, 90, 160);    // 选中

    private UIPanel _content = null!;
    private UIText _curText = null!;
    private UIText _msgText = null!;
    private UIText _gemMsg = null!;
    private int _tab;
    private int _selectedSlot = -1;
    private int _selectedGemId;
    private int _selectedGemItemSlot = -1;
    private int _gambleTier = 0;
    private int _riftLevel = 1;   // 秘境页：玩家选择的层数（1 ~ 最佳层+1）
    private string _gambleLog = "";
    private int _helpDoc;    // 说明页选中的文档
    private int _helpPage;   // 说明页当前页
    private int _rerollIdx = -1;              // 重铸演练：选中的词缀下标（-1=无）
    private AffixRoll? _rerollRoll;           // 重铸演练：单条掷出的新词缀（未确认）
    private List<AffixRoll>? _rerollAllRolls; // 重铸演练：全部掷出的新词缀（未确认）
    private int _consumeAction;               // 0=无；1=升档待选同名装备；2=开槽待选同名装备
    private int _hoverCoinTooltip;            // 悬停按钮的钱币花销（>0 时在 UI 树绘制完后画悬浮框）
    private bool _hoverPowerLabel;            // 本帧是否要画"力量: 数值"悬浮框（悬停"力量"标签时置位）

    private static string T(string key) => Language.GetTextValue($"Mods.Looteria.UI.{key}");

    /// <summary>在文本旁追加"力量"标签：悬停时弹出"力量: 数值"悬浮框（原版样式，由 Draw 最后绘制）。</summary>
    private static void AddPowerTag(UIPanel panel, int top, float scale, Color color)
    {
        var tag = new UIText(T("Power"), scale)
        {
            Top = new StyleDimension(top, 0f),
            Left = new StyleDimension(8f, 0f),
            TextColor = color
        };
        tag.OnMouseOver += (_, _) => { Instance._hoverPowerLabel = true; };
        tag.OnMouseOut += (_, _) => { Instance._hoverPowerLabel = false; };
        panel.Append(tag);
    }

    /// <summary>R11：服务端操作回执的客户端反馈（GambleService.ApplyOpResult 调用）。</summary>
    public void ShowOpResult(string msgKey, bool ok)
    {
        if (ok)
        {
            _gambleLog = T("Claimed"); // 成功默认提示
            Rebuild();
            return;
        }
        if (!string.IsNullOrEmpty(msgKey))
            _gambleLog = Language.GetTextValue($"Mods.Looteria.UI.{msgKey}");
        else
            _gambleLog = T("OpFailed");
        Rebuild();
    }

    /// <summary>悬停 UI 物品格时，用原版 tooltip 完整展示该物品（含本模组词缀/插槽/套装/传说/力量）。</summary>
    private static void ShowHoverItemTooltip(Item item)
    {
        if (item == null || item.IsAir) return;
        // 每帧克隆：tML 的 MouseText_DrawItemTooltip 会对 HoverItem.knockBack 原地乘潜行加成（×1.5），
        // 复用同一实例会每帧累乘 → 击退指数爆炸（面板内击退无限上升根因）。原版也是每帧克隆背包物品。
        Main.HoverItem = item.Clone();
        Main.instance.MouseText("");
    }

    /// <summary>弹出一个"力量: 数值"的原版样式悬浮框（在鼠标旁、始终最上层）。</summary>
    private void ShowPowerTooltip(int power)
    {
        var font = FontAssets.MouseText.Value;
        string text = T("Power") + ": " + power;
        var sz = font.MeasureString(text) * 0.8f;
        float boxW = sz.X + 24f;
        float boxH = 34f;
        var mouse = Main.MouseScreen;
        var rect = new Rectangle((int)mouse.X + 14, (int)mouse.Y + 14, (int)boxW, (int)boxH);
        if (rect.Right > Main.screenWidth - 8) rect.X = Main.screenWidth - rect.Width - 8;
        if (rect.Bottom > Main.screenHeight - 8) rect.Y = Main.screenHeight - rect.Height - 8;
        Utils.DrawInvBG(Main.spriteBatch, rect, new Color(23, 25, 81, 255) * 0.925f);
        float tx = rect.X + (rect.Width - sz.X) / 2f;
        float ty = rect.Y + (rect.Height - font.MeasureString(text).Y * 0.8f) / 2f;
        Utils.DrawBorderStringFourWay(Main.spriteBatch, font, text, tx, ty, Color.LightGray, Color.Black, Vector2.Zero, 0.8f);
    }

    /// <summary>M5：清空重铸演练状态（切页/换选物品时调用，防演练词缀写入别的物品）。</summary>
    private void ClearRerollState()
    {
        _rerollIdx = -1;
        _rerollRoll = null;
        _rerollAllRolls = null;
        _consumeAction = 0;
    }

    /// <summary>
    /// R2：多人客户端禁用"本地直改货币/物品"的操作（装备页拆解、重铸/升档/开槽的尘扣费、
    /// 谢谢惠顾券）——货币是服务端权威，本地扣的尘会被下一次 CurrencyPush 回滚（物品已销毁 = 白扔）。
    /// 返回 true 表示已拦截并提示。
    /// </summary>
    private bool BlockLocalCurrencyOp()
    {
        if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient) return false;
        ShowMsg(T("MpLocalOpDisabled"));
        return true;
    }

    public override void OnInitialize()
    {
        var root = new UIPanel
        {
            HAlign = 0.5f, VAlign = 0.5f,
            Width = new StyleDimension(880f, 0f),   // 固定宽度：正好容纳页签/网格/按钮，不再全屏过宽
            Height = new StyleDimension(-70f, 1f), // 高度保持
            BackgroundColor = C_PanelBg,
            BorderColor = new Color(70, 74, 96)
        };
        Append(root);

        var title = new UIText(T("Title"), 1.2f) { HAlign = 0.5f, Top = new StyleDimension(8f, 0f), TextColor = C_Accent };
        root.Append(title);

        _curText = new UIText("") { HAlign = 0.5f, Top = new StyleDimension(36f, 0f), TextColor = Color.LightGray };
        root.Append(_curText);

        string[] tabs = { "TabEquip", "TabReroll", "TabSocket", "TabEnhance", "TabGamble", "TabRift", "TabHelp" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            var b = new UITextPanel<string>(T(tabs[i]), 0.75f)
            {
                Top = new StyleDimension(62f, 0f),
                Left = new StyleDimension(12f + i * 120f, 0f),
                Width = new StyleDimension(112f, 0f),
                Height = new StyleDimension(30f, 0f)
            };
            b.BackgroundColor = new Color(44, 47, 66);
            b.BorderColor = new Color(70, 74, 96);
            b.WithFadedMouseOver();
            b.OnLeftClick += (_, _) => { _tab = idx; ClearRerollState(); Rebuild(); }; // M5：切页清演练状态
            root.Append(b);
        }

        _content = new UIPanel
        {
            Top = new StyleDimension(98f, 0f),
            Left = new StyleDimension(12f, 0f),
            Width = new StyleDimension(-24f, 1f),
            Height = new StyleDimension(-110f, 1f),
            BackgroundColor = new Color(20, 22, 34)
        };
        root.Append(_content);
    }

    public override void OnActivate()
    {
        UISystem.PanelOpen = true;
        // 暂停行为遵循原版：开「自动暂停」设置时，打开本全屏面板同样会暂停世界（CanPauseGame 的
        // autoPause 分支命中；面板本身不在内置白名单，但不影响自动暂停生效）。多人不受影响。
        if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient)
            Main.gamePaused = true;
        Rebuild();
    }

    public override void OnDeactivate()
    {
        UISystem.PanelOpen = false;
        Main.gamePaused = false;
    }

    /// <summary>
    /// 在整棵 UI 树绘制完之后再画钱币悬浮框——子元素是按顺序绘制的，直接在按钮内画会被
    /// 排在其后的按钮（下方）盖住；这里最后画保证悬浮框永远在最上层。
    /// </summary>
    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        // 各按钮登记的"花费"悬浮框（钱币）
        int copper = _hoverCoinTooltip;
        _hoverCoinTooltip = 0; // 每帧重置，只有当前帧有按钮悬停时才画
        if (copper > 0) DrawCostTooltip(copper);
        // 力量悬浮框（悬停"力量"标签时显示选中物品的力量数值）
        if (_hoverPowerLabel)
        {
            _hoverPowerLabel = false;
            var sel = GetSelected(Main.LocalPlayer);
            if (sel != null && sel.TryGetGlobalItem(out AffixGlobalItem ag) && ag.HasAffix)
                ShowPowerTooltip(ag.PowerScore);
        }
    }
    /// <summary>画"花费 [钱币图标]"悬浮框（在鼠标旁、始终最上层）。</summary>
    private void DrawCostTooltip(int copper)
    {
        var font = FontAssets.MouseText.Value;
        string costText = T("Cost") + ": ";
        var sz = font.MeasureString(costText) * 0.8f;
        var (p, g, s, c) = SplitCoins(copper);
        int iconCount = (p > 0 ? 1 : 0) + (g > 0 ? 1 : 0) + (s > 0 ? 1 : 0) + (c > 0 ? 1 : 0);
        float iconW = 24f;
        float boxW = sz.X + iconCount * (iconW + 4f) + 24f;
        float boxH = 36f;
        var mouse = Main.MouseScreen;
        var rect = new Rectangle((int)mouse.X + 14, (int)mouse.Y + 14, (int)boxW, (int)boxH);
        // 保持屏幕内
        if (rect.Right > Main.screenWidth - 8) rect.X = Main.screenWidth - rect.Width - 8;
        if (rect.Bottom > Main.screenHeight - 8) rect.Y = Main.screenHeight - rect.Height - 8;
        var sb = Main.spriteBatch;
        Utils.DrawInvBG(sb, rect, new Color(23, 25, 81, 255) * 0.925f);
        float tx = rect.X + 10f;
        float ty = rect.Y + (rect.Height - font.MeasureString(costText).Y * 0.8f) / 2f;
        Utils.DrawBorderStringFourWay(sb, font, costText, tx, ty, Color.White, Color.Black, Vector2.Zero, 0.8f);
        tx += sz.X + 2f;
        DrawCoinIcons(sb, font, tx, ty - 2f, copper, Color.White, 0.75f);
    }

    public void Rebuild()
    {
        if (_content == null) return;
        _content.RemoveAllChildren();
        _msgText = null!;
        _gemMsg = null!;
        UpdateCurrency();
        // 选中页签高亮
        var root = _content.Parent;
        if (root != null)
        {
            int t = 0;
            foreach (var child in root.Children)
            {
                if (child is UITextPanel<string> tp && tp.Top.Pixels == 62f)
                {
                    tp.BackgroundColor = t == _tab ? C_Selected : new Color(44, 47, 66);
                    t++;
                }
            }
        }
        switch (_tab)
        {
            case 0: BuildEquip(); break;
            case 1: BuildReroll(); break;
            case 2: BuildSocket(); break;
            case 3: BuildEnhance(); break;
            case 4: BuildGamble(); break;
            case 5: BuildRift(); break;
            default: BuildHelp(); break;
        }
        _content.Recalculate();
    }

    private void UpdateCurrency()
    {
        var lp = Main.LocalPlayer.GetModPlayer<LooteriaPlayer>();
        _curText.SetText(T("Currency").Replace("{0}", lp.BloodShards.ToString())
            .Replace("{1}", lp.Dust.ToString()).Replace("{2}", lp.GearPower.ToString()));
    }

    private void ShowMsg(string text)
    {
        var target = _gemMsg ?? _msgText;
        target?.SetText(text);
    }
}
