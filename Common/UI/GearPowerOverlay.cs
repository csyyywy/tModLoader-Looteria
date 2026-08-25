using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using global::Looteria.Common.Players;

namespace Looteria.Common.UI;

/// <summary>
/// 血量前方显示力量等级（掠夺之力）。
/// 通过 ModResourceOverlay 在生命显示的第一个资源处绘制【占位图 + 数值】：
/// - Classic/Fancy 心（左下）→ 徽标画在心的上方；
/// - Bars 生命条（顶部）→ 徽标自动画在条的下方，避免出屏。
/// 位置始终 clamp 在屏幕内，锚点异常时用固定兜底位置。
/// 占位图 Content/UI/PowerIcon.png 可直接替换（保持同路径覆盖即可，无需改代码）。
/// </summary>
[Autoload(Side = ModSide.Client)]
public class GearPowerOverlay : ModResourceOverlay
{
    private static Asset<Texture2D>? _icon;
    private static readonly Dictionary<string, Asset<Texture2D>?> _lifeAssetCache = new();

    /// <summary>占位图标（用户可替换 Content/UI/PowerIcon.png）。</summary>
    private static Asset<Texture2D> Icon => _icon ??= ModContent.Request<Texture2D>("Looteria/Content/UI/PowerIcon");

    /// <summary>
    /// 各资源显示集的生命填充纹理（只取确定存在的资源；排除法力星星与易变动的面板）。
    /// 注意：HorizontalBars 的左右面板名不是 HP_Panel_Left/Right（那会 AssetLoadException），
    /// 这里只用填充 HP_Fill/HP_Fill_Honey；Fancy 用 Heart_Fill 系列。
    /// </summary>
    private static readonly string[] LifePaths =
    {
        "Images/UI/PlayerResourceSets/FancyClassic/Heart_Fill",
        "Images/UI/PlayerResourceSets/FancyClassic/Heart_Fill_B",
        "Images/UI/PlayerResourceSets/HorizontalBars/HP_Fill",
        "Images/UI/PlayerResourceSets/HorizontalBars/HP_Fill_Honey",
    };

    private static bool IsLifeTexture(Asset<Texture2D> tex)
    {
        // Classic 心（默认显示）
        if (tex == TextureAssets.Heart || tex == TextureAssets.Heart2) return true;
        foreach (var p in LifePaths)
        {
            if (!_lifeAssetCache.TryGetValue(p, out var asset))
            {
                try { asset = _lifeAssetCache[p] = Main.Assets.Request<Texture2D>(p); }
                catch { _lifeAssetCache[p] = null; continue; } // 资源缺失绝不崩溃
            }
            if (asset != null && tex == asset) return true;
        }
        return false;
    }

    /// <summary>生命显示的第一个资源画完后，直接在生命资源左侧绘制力量等级徽标。</summary>
    public override void PostDrawResource(ResourceOverlayDrawContext context)
    {
        if (context.resourceNumber != 0) return;
        if (!IsLifeTexture(context.texture)) return;
        DrawBadge(context.position, context.texture.Height());
    }

    /// <summary>徽标放在生命资源左侧、垂直居中；数值文字居中画在图标内部，太长自动缩小适配。</summary>
    private void DrawBadge(Vector2 anchor, float resourceHeight)
    {
        var sb = Main.spriteBatch;
        var icon = Icon;
        float size = 36f;

        float centerY = anchor.Y + resourceHeight / 2f;
        // 徽标在生命资源左侧，再按玩家要求左移 64、上移 16 像素
        var pos = new Vector2(anchor.X - size - 6f - 64f, centerY - size / 2f - 16f);
        pos.X = Math.Clamp(pos.X, 2f, Math.Max(2f, Main.screenWidth - size - 4f));
        pos.Y = Math.Clamp(pos.Y, 2f, Math.Max(2f, Main.screenHeight - size - 4f));

        sb.Draw(icon.Value, new Rectangle((int)pos.X, (int)pos.Y, (int)size, (int)size), Color.White);

        int power = Main.LocalPlayer != null
            ? Main.LocalPlayer.GetModPlayer<LooteriaPlayer>().GearPower
            : 0;
        string text = power.ToString();
        var font = FontAssets.MouseText.Value;
        var sz = font.MeasureString(text);

        // 文字居中于图标内；超宽自动缩小适配（大数值也不会溢出）
        float scale = 0.95f;
        float maxW = size - 10f;
        if (sz.X > maxW) scale *= maxW / sz.X;
        float tx = pos.X + (size - sz.X * scale) / 2f;
        float ty = pos.Y + (size - sz.Y * scale) / 2f;
        Utils.DrawBorderStringFourWay(sb, font, text, tx, ty, Color.White, Color.Black, Vector2.Zero, scale);
    }
}
