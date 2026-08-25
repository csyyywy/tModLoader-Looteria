using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
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
            // 占位行 = iconRows 行，每行一个空格（行间 '\n'，末行不带换行）。
            // 不能用 "n 个 '\n'"：'\n' 开头会让 ChatManager 绘制多推进 1 行而 MeasureString 只算 1 行
            // → 图标行后视觉多空一行、最后一行（力量）被推出框外。空格行测量与绘制行数严格一致。
            int iconRows = (g.SocketCount + 5) / 6;
            if (iconRows > 0)
            {
                var rows = new string[iconRows];
                for (int i = 0; i < iconRows; i++) rows[i] = " ";
                tooltips.Add(new TooltipLine(mod, "LooteriaSocketIcons", string.Join("\n", rows)));
            }
        }

        // 传说之力
        if (g.LegendaryPowerId > 0)
        {
            tooltips.Add(new TooltipLine(mod, "LooteriaLegendary",
                Language.GetTextValue("Mods.Looteria.UI.LegendaryTag",
                    Language.GetTextValue($"Mods.Looteria.Legendary.{g.LegendaryPowerId}")))
            { OverrideColor = new Color(255, 130, 0) });
        }

        // 套装主题 + 进度（所属套装名、已穿件数、各档效果与激活状态）
        if (g.SetThemeId >= 0)
        {
            string theme = Language.GetTextValue($"Mods.Looteria.Theme.{g.SetThemeId}");
            tooltips.Add(new TooltipLine(mod, "LooteriaSet",
                Language.GetTextValue("Mods.Looteria.UI.SetTag", theme)) { OverrideColor = new Color(0, 255, 120) });
            AppendSetProgress(mod, g, tooltips);
        }

        // 力量等级（Power 键本身不带 {0}，UI 面板手动拼数值；这里拼 ": 数值"）
        tooltips.Add(new TooltipLine(mod, "LooteriaPower",
            Language.GetTextValue("Mods.Looteria.UI.Power") + ": " + g.PowerScore) { OverrideColor = Color.LightGray });
    }

    /// <summary>套装进度行：当前身上同主题件数 + 2/4/6 各档效果（已激活亮绿，未激活灰并显示还差几件）。</summary>
    private static void AppendSetProgress(Mod mod, AffixGlobalItem g, List<TooltipLine> tooltips)
    {
        var player = Main.LocalPlayer;
        if (player == null) return;
        int worn = 0;
        // M7：只统计真实装备槽（0..9），时装位不算
        for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < player.armor.Length; i++)
        {
            if (player.armor[i].TryGetGlobalItem(out AffixGlobalItem ag) && ag.SetThemeId == g.SetThemeId) worn++;
        }
        tooltips.Add(new TooltipLine(mod, "LooteriaSetProgress",
            Language.GetTextValue("Mods.Looteria.UI.SetProgress", worn)) { OverrideColor = new Color(150, 255, 180) });

        string[] bonusKeys = { "Mods.Looteria.UI.SetBonus2", "Mods.Looteria.UI.SetBonus4", "Mods.Looteria.UI.SetBonus6" };
        for (int i = 0; i < SetBonusHandler.Thresholds.Length && i < bonusKeys.Length; i++)
        {
            int t = SetBonusHandler.Thresholds[i];
            bool active = worn >= t;
            string status = active
                ? Language.GetTextValue("Mods.Looteria.UI.SetActive")
                : Language.GetTextValue("Mods.Looteria.UI.SetNeed", t - worn);
            tooltips.Add(new TooltipLine(mod, "LooteriaSetBonus" + t,
                $"{Language.GetTextValue(bonusKeys[i])} · {status}")
            { OverrideColor = active ? new Color(0, 255, 120) : new Color(140, 145, 160) });
        }
    }

    /// <summary>数值显示：百分比整数，平坦 1 位小数。</summary>
    public static string FormatValue(AffixDef def, float v)
        => def.IsPercent ? $"+{v:0}%" : $"+{v:0.#}";
}
