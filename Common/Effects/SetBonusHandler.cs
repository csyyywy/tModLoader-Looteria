using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using global::Looteria.Common.Data;
using global::Looteria.Common.Globals;

namespace Looteria.Common.Effects;

/// <summary>
/// 主题套装（2/4/6 件）加成。在 LooteriaPlayer.PostUpdateEquips 调用。
/// 2:+5% 全伤；4:+10% 全伤+5% 减伤；6:+20% 全伤+8% 减伤+10% 移速。
/// </summary>
public static class SetBonusHandler
{
    public static void Apply(Player player)
    {
        var counts = new Dictionary<int, int>();
        // M7：只扫描真实装备槽（0..9）；10 起是时装位（社交护甲/饰品），凑套装/吃加成是作弊
        for (int i = 0; i < AffixGlobalItem.RealEquipSlots && i < player.armor.Length; i++)
        {
            var item = player.armor[i];
            if (item.TryGetGlobalItem(out AffixGlobalItem g) && g.SetThemeId >= 0)
            {
                if (counts.TryGetValue(g.SetThemeId, out int c)) counts[g.SetThemeId] = c + 1;
                else counts[g.SetThemeId] = 1;
            }
        }

        foreach (var kv in counts)
        {
            int n = kv.Value;
            if (n >= 6)
            {
                player.GetDamage(DamageClass.Generic) += 0.20f;
                player.endurance += 0.08f;
                player.moveSpeed *= 1.10f;
            }
            else if (n >= 4)
            {
                player.GetDamage(DamageClass.Generic) += 0.10f;
                player.endurance += 0.05f;
            }
            else if (n >= 2)
            {
                player.GetDamage(DamageClass.Generic) += 0.05f;
            }
        }
    }
}
