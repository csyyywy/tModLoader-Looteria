using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace Looteria.Common.UI;

/// <summary>
/// 独立的人物渲染器：完全绕开 <see cref="Main.PlayerRenderer"/>（1.4.4 它内部走
/// <c>Main.spriteBuffer</c> → 原始 <c>DrawIndexedPrimitives</c>，在 UI Deferred 批次下
/// 会被界面盖住/顺序错乱）。这里复刻 tML <c>LegacyPlayerRenderer.DrawPlayerInternal</c>
/// 的完整管线，但最后一步用 <c>DrawPlayer_RenderAllLayersSlow</c>（纯 spriteBatch 绘制，
/// 走 UI 的变换矩阵），因此可以直接画进任意 UI 元素。
/// </summary>
public static class PortraitRenderer
{
    private static readonly List<DrawData> _drawData = new();
    private static readonly List<int> _dust = new();
    private static readonly List<int> _gore = new();

    /// <summary>
    /// 用「慢路径」（spriteBatch 直绘）画玩家。
    /// <paramref name="uiPos"/> = 玩家碰撞盒左上角的 UI 坐标（UI 像素，UIScale 坐标空间）。
    /// 注意：调用方需保证当前 spriteBatch 已 Begin（矩阵 = UIScaleMatrix）。
    /// 内部坐标 = uiPos（世界 − screenPosition），直接喂给各层（层内不再减 screenPosition）。
    /// </summary>
    public static void DrawPlayer(SpriteBatch sb, Player player, Vector2 uiPos, float scale = 1f)
    {
        if (player == null || player.ShouldNotDraw)
            return;

        _drawData.Clear();
        _dust.Clear();
        _gore.Clear();

        var drawInfo = new PlayerDrawSet();
        drawInfo.BoringSetup(player, _drawData, _dust, _gore,
            uiPos + Main.screenPosition, // 世界坐标（层内 DrawData 再减回 screenPosition → uiPos）
            0f, 0f, Vector2.Zero);

        PlayerLoader.ModifyDrawInfo(ref drawInfo);

        foreach (var layer in PlayerDrawLayerLoader.GetDrawLayers(drawInfo))
            layer.DrawWithTransformationAndChildren(ref drawInfo);

        PlayerDrawLayers.DrawPlayer_MakeIntoFirstFractalAfterImage(ref drawInfo);
        PlayerDrawLayers.DrawPlayer_TransformDrawData(ref drawInfo);
        if (scale != 1f)
            PlayerDrawLayers.DrawPlayer_ScaleDrawData(ref drawInfo, scale);
        PlayerLoader.TransformDrawData(ref drawInfo);

        // 关键：慢路径 = 直接 spriteBatch.Draw（每张 DrawData 用 Main.spriteBatch），
        // 走当前 batch 的矩阵；不触碰 Main.spriteBuffer。
        PlayerDrawLayers.DrawPlayer_RenderAllLayersSlow(ref drawInfo);
    }
}
