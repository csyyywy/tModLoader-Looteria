using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.Effects;

/// <summary>从词缀数据生成 tooltip 行（GlobalItem 与 UI 共用）。</summary>
public static class TooltipBuilder
{
    public static void Build(AffixGlobalItem g, Mod mod, List<TooltipLine> tooltips)
    {
        if (g == null || !g.HasAffix) return;

        // 稀有度
        string rarityName = Language.GetTextValue($"Mods.Looteria.Rarity.{(int)g.Rarity}");
        tooltips.Add(new TooltipLine(mod, "LooteriaRarity", rarityName)
        { OverrideColor = RarityInfo.Colors[Math.Clamp((int)g.Rarity, 0, RarityInfo.Count - 1)] }); // M14

        // 词缀
        if (g.Affixes != null)
        {
            foreach (var r in g.Affixes)
            {
                var def = AffixDatabase.GetById(r.AffixId);
                if (def == null) continue; // L1：未知词缀 id 防御（坏档/版本迁移，跳过该条）
                string txt = Language.GetTextValue($"Mods.Looteria.Affix.{def.Key}", FormatValue(def, r.Value));
                tooltips.Add(new TooltipLine(mod, "LooteriaAffix", txt) { OverrideColor = new Color(255, 200, 0) });
            }
        }

        // 插槽（表头行：槽位数 + 图标预留行）。
        // 图标本身由 AffixGlobalItem.PostDrawTooltip 自绘（原版宝石物品图标、自动换行、空槽灰框、+N 强化角标）。
        // 预留行 = 每 6 个一行的换行占位，让 tooltip 背景框包住图标区（box 尺寸在 PreDrawTooltip 前已按文本测出，
        // 唯一能扩框的办法就是加入真实高度的文本行——多行 '\n' 会被 MeasureString 计为多行高度）。
        if (g.SocketCount > 0)
        {
            tooltips.Add(new TooltipLine(mod, "LooteriaSockets",
                Language.GetTextValue("Mods.Looteria.UI.SocketsCount", g.SocketCount)) { OverrideColor = Color.DeepSkyBlue });
            int iconRows = (g.SocketCount + 5) / 6;
            if (iconRows > 0)
                tooltips.Add(new TooltipLine(mod, "LooteriaSocketIcons", new string('\n', iconRows)));
        }

        // 传说之力
        if (g.LegendaryPowerId > 0)
        {
            tooltips.Add(new TooltipLine(mod, "LooteriaLegendary",
                Language.GetTextValue($"Mods.Looteria.Legendary.{g.LegendaryPowerId}"))
            { OverrideColor = new Color(255, 130, 0) });
        }

        // 套装主题
        if (g.SetThemeId >= 0)
        {
            string theme = Language.GetTextValue($"Mods.Looteria.Theme.{g.SetThemeId}");
            tooltips.Add(new TooltipLine(mod, "LooteriaSet",
                Language.GetTextValue("Mods.Looteria.UI.SetTag", theme)) { OverrideColor = new Color(0, 255, 120) });
        }

        // 力量等级
        tooltips.Add(new TooltipLine(mod, "LooteriaPower",
            Language.GetTextValue("Mods.Looteria.UI.Power", g.PowerScore)) { OverrideColor = Color.LightGray });
    }

    /// <summary>数值显示：百分比整数，平坦 1 位小数。</summary>
    public static string FormatValue(AffixDef def, float v)
        => def.IsPercent ? $"+{v:0}%" : $"+{v:0.#}";
}
