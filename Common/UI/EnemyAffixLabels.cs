using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using global::Looteria.Common.Configs;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.UI;

/// <summary>
/// 敌人词缀血条标签（UI 层绘制，恒定显示）：
/// 在「Vanilla: Entity Health Bars」层之后插入一层，收集所有带词缀的存活敌人，
/// 在其血条下方渲染彩色词缀标签（每条一行，按词缀上色）。
/// 纯表现、各端都执行；配置 AffixDisplayMode=UnderHealthBar 时启用。
/// </summary>
public class EnemyAffixLabels : ModSystem
{
    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int idx = layers.FindIndex(l => l.Name == "Vanilla: Entity Health Bars");
        if (idx < 0) return;
        var layer = new LegacyGameInterfaceLayer(
            "Looteria: Enemy Affix Labels",
            delegate
            {
                DrawAll();
                return true;
            },
            InterfaceScaleType.UI);
        layers.Insert(idx + 1, layer);
    }

    /// <summary>收集全部带词缀的存活敌人并绘制标签（每帧调用）。</summary>
    private static void DrawAll()
    {
        if (EnemyAffixConfig.Instance is not { AffixDisplayMode: AffixDisplayMode.UnderHealthBar }) return;
        if (Main.gameMenu || Main.LocalPlayer == null || Main.LocalPlayer.dead) return;

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc == null || !npc.active || npc.life <= 0 || npc.shimmerTransparency != 0f) continue;
            if (!EnemyAffixGlobalNPC.HasAnyAffix(npc)) continue;

            var g = npc.GetGlobalNPC<EnemyAffixGlobalNPC>();
            if (g == null || g.Affixes == null || g.Affixes.Count == 0) continue;

            float scale = 1f;
            if (npc.boss || EnemyAffixDatabase.RarityOf(g.Affixes[0]) == EnemyAffixRarity.BossExclusive)
                scale = 1.5f; // Boss 血条放大，标签同步放大

            // 血条底部（世界坐标 → 屏幕坐标）
            float barBottom;
            if (Main.HealthBarDrawSettings == 0)
            {
                barBottom = npc.Top.Y - 30f + npc.gfxOffY;
            }
            else
            {
                float barTop = Main.HealthBarDrawSettings == 1
                    ? npc.position.Y + npc.height + 10f + Main.NPCAddHeight(npc) + npc.gfxOffY
                    : npc.position.Y + 10f - Main.NPCAddHeight(npc) / 2f + npc.gfxOffY;
                barBottom = barTop + 36f * scale;
            }
            float y = barBottom - Main.screenPosition.Y + 4f;

            var font = FontAssets.MouseText.Value;
            string line = "";
            float lineW = 0f;
            foreach (var id in g.Affixes)
            {
                string txt = Language.GetTextValue("Mods.Looteria.EnemyAffix." + EnemyAffixDatabase.Key(id));
                if (string.IsNullOrEmpty(txt)) continue;
                Vector2 size = font.MeasureString(txt) * scale;
                if (line.Length > 0 && lineW + size.X > 250f * scale)
                {
                    DrawLabelLine(font, line, npc.Center.X, ref y, scale);
                    line = "";
                    lineW = 0f;
                }
                line += (line.Length > 0 ? "  " : "") + txt;
                lineW += size.X + (lineW > 0f ? 12f * scale : 0f);
            }
            if (line.Length > 0)
                DrawLabelLine(font, line, npc.Center.X, ref y, scale);
        }
    }

    /// <summary>绘制一行词缀标签（屏幕坐标，UI 矩阵）。</summary>
    private static void DrawLabelLine(ReLogic.Graphics.DynamicSpriteFont font, string line, float npcCenterX, ref float y, float scale)
    {
        Vector2 size = font.MeasureString(line) * scale;
        float x = npcCenterX - Main.screenPosition.X - size.X / 2f;
        Utils.DrawBorderStringFourWay(Main.spriteBatch, font, line, x, y, Color.White, Color.Black * 0.8f, Vector2.Zero, scale);
        y += size.Y + 2f;
    }
}
