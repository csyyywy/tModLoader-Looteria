using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.UI;

/// <summary>
/// 可点击物品格：绘制原版背包格 + 物品图标；点击触发 OnLeftClick（标准事件，与 UITextPanel 同机制）。
/// - 命中区 = GetDimensions()（Append 时已 Recalculate）；点击后由外部设置 Selected 并 Rebuild。
/// - 背景：TextureAssets.InventoryBack9（本 tML 版本正确资源，勿用 Main.Assets.Request/旧字段）。
/// - DrawSelf 全防御：任何贴图缺失只跳过绘制，不抛异常（避免破坏整块 UI 交互）。
/// </summary>
public class UIItemSlot : UIElement
{
    public int SlotIndex;
    public Item Item = new();
    public bool Selected;
    /// <summary>L9：悬停 tooltip 用的克隆（每帧 Clone 含词缀列表深拷贝 → GC 压力），进入悬停克隆一次、离开置空。</summary>
    private Item? _hoverClone;

    /// <summary>有词缀时高亮稀有度色（无则 -1）。</summary>
    public int RarityHighlight = -1;

    /// <summary>若非空，右下角显示数量角标（如宝石堆叠数）。</summary>
    public string? StackText;

    public UIItemSlot(int index, Item item)
    {
        SlotIndex = index;
        Item = item;
        Width = new StyleDimension(52f, 0f);
        Height = new StyleDimension(52f, 0f);
        // 标准点击事件（与面板页签按钮同机制，保证可点）
        OnLeftClick += (_, _) => OnSlotClicked?.Invoke(this);
    }

    /// <summary>点击回调（构造里接线到 OnLeftClick）。</summary>
    public Action<UIItemSlot>? OnSlotClicked;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        var dims = GetDimensions();

        // 悬停：显示物品完整属性（像物品栏一样，含本模组词缀 tooltip）
        if (IsMouseHovering)
        {
            Main.LocalPlayer.mouseInterface = true;
            if (Item != null && !Item.IsAir)
            {
                if (_hoverClone == null) _hoverClone = Item.Clone(); // L9：克隆一次并复用
                Main.HoverItem = _hoverClone;
                Main.instance.MouseText("");
            }
        }
        else
        {
            _hoverClone = null; // L9：离开悬停释放
        }

        // 背景（防御：贴图缺失画纯色框）
        var back = TextureAssets.InventoryBack9?.Value;
        if (back != null)
        {
            spriteBatch.Draw(back, dims.Position(), null, Selected ? Color.White : Color.White * 0.9f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        }
        else
        {
            var r = dims.ToRectangle();
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, r, Selected ? Color.LightCyan * 0.4f : Color.Gray * 0.3f);
        }

        // 选中框
        if (Selected)
        {
            var r = dims.ToRectangle();
            // M14：RarityHighlight 越界防御
            var border = RarityHighlight >= 0
                ? RarityInfo.Colors[Math.Clamp(RarityHighlight, 0, RarityInfo.Count - 1)]
                : Color.LightCyan;
            DrawBorder(spriteBatch, r, border);
        }

        if (Item == null || Item.IsAir) return;

        // 物品图标：原版物品贴图是惰性加载的，必须先 LoadItem 确保已加载，否则图标空白。
        // （本模组 ModItem 贴图在模组加载时预载，所以之前只有两张奖券能看到图标。）
        int type = Item.type;
        try
        {
            Main.instance.LoadItem(type);
            Asset<Texture2D>? tex = TextureAssets.Item[type];
            if (tex?.Value == null) return;
            var texV = tex.Value;
            Rectangle frame = Main.itemAnimations[type] != null
                ? Main.itemAnimations[type].GetFrame(texV)
                : texV.Frame(1, 1, 0, 0);
            if (frame.Width <= 0 || frame.Height <= 0) return;
            float maxW = 34f;
            float scale = Math.Min(maxW / frame.Width, maxW / frame.Height);
            var origin = frame.Size() * 0.5f;
            spriteBatch.Draw(texV, dims.Center(), frame, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        }
        catch (Exception e)
        {
            // 记录异常而非静默吞掉，便于排查个别异常物品
            global::Looteria.Looteria.Instance?.Logger.Error($"物品图标绘制失败 type={type} name={Item.Name}", e);
        }

        // 数量角标（如宝石堆叠数）
        if (StackText != null)
        {
            var font = FontAssets.ItemStack.Value;
            var sz = font.MeasureString(StackText);
            Utils.DrawBorderStringFourWay(spriteBatch, font, StackText,
                dims.X + dims.Width - 4f - sz.X,
                dims.Y + dims.Height - 12f,
                Color.White, Color.Black, Vector2.Zero);
        }
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle r, Color color)
    {
        var px = TextureAssets.MagicPixel.Value;
        sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, 2), color);
        sb.Draw(px, new Rectangle(r.X, r.Y, 2, r.Height), color);
        sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), color);
        sb.Draw(px, new Rectangle(r.Right - 2, r.Y, 2, r.Height), color);
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        if (Item != null && !Item.IsAir) Main.LocalPlayer.mouseInterface = true;
    }
}
