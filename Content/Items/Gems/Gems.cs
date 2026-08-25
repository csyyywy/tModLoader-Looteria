using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using global::Looteria.Common.Data;

namespace Looteria.Content.Items.Gems;

/// <summary>
/// 宝石物品基类：名字/提示走 Gem 命名空间键；强度 = 类型 × 等级 × 强化（+10%/阶，无限阶）。
/// 宝石只由 Boss 掉落（含模组 Boss）。插槽镶宝石消耗物品本身。升阶消耗同型宝石，可失败。
/// </summary>
public abstract class LooteriaGemItem : ModItem
{
    public abstract GemType GemType { get; }
    public abstract int GemLevel { get; }
    public int GemId => GemDatabase.Id(GemType, GemLevel);

    /// <summary>强化等级（DNF 式，每次 +10% 效果，可失败，无限阶）。持久化。</summary>
    public int Upgrade;

    public override void SaveData(TagCompound tag)
    {
        if (Upgrade > 0) tag["u"] = Upgrade;
    }

    public override void LoadData(TagCompound tag)
    {
        Upgrade = tag.GetInt("u");
    }

    // R9：强化值随物品网络同步（联机丢弃/交易/中转后不归零）
    public override void NetSend(BinaryWriter writer) => writer.Write(Upgrade);

    public override void NetReceive(BinaryReader reader) => Upgrade = reader.ReadInt32();

    public override LocalizedText DisplayName => Language.GetText(GemDatabase.Key(GemId));

    /// <summary>tooltip 首行：类型名（静态键）。</summary>
    public override LocalizedText Tooltip => Language.GetText($"Mods.Looteria.GemTip.{(int)GemType}");

    /// <summary>tooltip 动态效果行（含等级与强化）。用 ModifyTooltips 注入，避免动态 LocalizedText 触发 tML 自动补键。</summary>
    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        float k = (GemLevel + 1) * (1f + 0.1f * Upgrade);
        tooltips.Add(new TooltipLine(Mod, "GemEffect",
            Language.GetTextValue($"Mods.Looteria.GemTipEffect.{(int)GemType}", FormatEffect(GemType, k)))
        { OverrideColor = Color.DeepSkyBlue });
        if (Upgrade > 0)
        {
            tooltips.Add(new TooltipLine(Mod, "GemUpgrade",
                Language.GetTextValue("Mods.Looteria.GemTipUpgrade", Upgrade, Upgrade * 10))
            { OverrideColor = Color.Orange });
        }
    }

    private static string FormatEffect(GemType type, float k) => type switch
    {
        GemType.Ruby => $"{3f * k:0}",
        GemType.Sapphire => $"{1.5f * k:0.#}",
        GemType.Emerald => $"{2f * k:0}",
        GemType.Amethyst => $"{12f * k:0}",
        GemType.Topaz => $"{1.5f * k:0.#}",
        GemType.Diamond => $"{1f * k:0.#}",
        _ => "0"
    };

    public override void SetDefaults()
    {
        Item.width = 24; Item.height = 24;
        Item.maxStack = 1; // 宝石不可堆叠：每颗独立持有自己的强化值（堆叠会让整叠共享一次强化）
        Item.value = 500 * (GemLevel + 1);
        Item.rare = GemLevel switch
        {
            0 => ItemRarityID.Blue,
            1 => ItemRarityID.Green,
            2 => ItemRarityID.Orange,
            _ => ItemRarityID.Pink
        };
    }
}

/// <summary>gem id → 物品类型映射（懒初始化）。</summary>
public static class GemItemHelper
{
    /// <summary>本模组宝石类型 → 原版同名宝石 ItemID（升阶材料用，颜色一一对应）。</summary>
    public static int VanillaGemType(GemType type) => type switch
    {
        GemType.Ruby => ItemID.Ruby,
        GemType.Sapphire => ItemID.Sapphire,
        GemType.Emerald => ItemID.Emerald,
        GemType.Amethyst => ItemID.Amethyst,
        GemType.Topaz => ItemID.Topaz,
        GemType.Diamond => ItemID.Diamond,
        _ => 0
    };

    private static Dictionary<int, int>? _map;
    public static int TypeForGem(int gemId)
    {
        _map ??= new Dictionary<int, int>
        {
            [GemDatabase.Id(GemType.Ruby, 0)] = ModContent.ItemType<Ruby0>(),
            [GemDatabase.Id(GemType.Ruby, 1)] = ModContent.ItemType<Ruby1>(),
            [GemDatabase.Id(GemType.Ruby, 2)] = ModContent.ItemType<Ruby2>(),
            [GemDatabase.Id(GemType.Ruby, 3)] = ModContent.ItemType<Ruby3>(),
            [GemDatabase.Id(GemType.Sapphire, 0)] = ModContent.ItemType<Sapphire0>(),
            [GemDatabase.Id(GemType.Sapphire, 1)] = ModContent.ItemType<Sapphire1>(),
            [GemDatabase.Id(GemType.Sapphire, 2)] = ModContent.ItemType<Sapphire2>(),
            [GemDatabase.Id(GemType.Sapphire, 3)] = ModContent.ItemType<Sapphire3>(),
            [GemDatabase.Id(GemType.Emerald, 0)] = ModContent.ItemType<Emerald0>(),
            [GemDatabase.Id(GemType.Emerald, 1)] = ModContent.ItemType<Emerald1>(),
            [GemDatabase.Id(GemType.Emerald, 2)] = ModContent.ItemType<Emerald2>(),
            [GemDatabase.Id(GemType.Emerald, 3)] = ModContent.ItemType<Emerald3>(),
            [GemDatabase.Id(GemType.Amethyst, 0)] = ModContent.ItemType<Amethyst0>(),
            [GemDatabase.Id(GemType.Amethyst, 1)] = ModContent.ItemType<Amethyst1>(),
            [GemDatabase.Id(GemType.Amethyst, 2)] = ModContent.ItemType<Amethyst2>(),
            [GemDatabase.Id(GemType.Amethyst, 3)] = ModContent.ItemType<Amethyst3>(),
            [GemDatabase.Id(GemType.Topaz, 0)] = ModContent.ItemType<Topaz0>(),
            [GemDatabase.Id(GemType.Topaz, 1)] = ModContent.ItemType<Topaz1>(),
            [GemDatabase.Id(GemType.Topaz, 2)] = ModContent.ItemType<Topaz2>(),
            [GemDatabase.Id(GemType.Topaz, 3)] = ModContent.ItemType<Topaz3>(),
            [GemDatabase.Id(GemType.Diamond, 0)] = ModContent.ItemType<Diamond0>(),
            [GemDatabase.Id(GemType.Diamond, 1)] = ModContent.ItemType<Diamond1>(),
            [GemDatabase.Id(GemType.Diamond, 2)] = ModContent.ItemType<Diamond2>(),
            [GemDatabase.Id(GemType.Diamond, 3)] = ModContent.ItemType<Diamond3>()
        };
        return _map.TryGetValue(gemId, out var t) ? t : 0;
    }
}

// ===== 6 类型 × 4 级 =====
public class Ruby0 : LooteriaGemItem { public override GemType GemType => GemType.Ruby; public override int GemLevel => 0; }
public class Ruby1 : LooteriaGemItem { public override GemType GemType => GemType.Ruby; public override int GemLevel => 1; }
public class Ruby2 : LooteriaGemItem { public override GemType GemType => GemType.Ruby; public override int GemLevel => 2; }
public class Ruby3 : LooteriaGemItem { public override GemType GemType => GemType.Ruby; public override int GemLevel => 3; }

public class Sapphire0 : LooteriaGemItem { public override GemType GemType => GemType.Sapphire; public override int GemLevel => 0; }
public class Sapphire1 : LooteriaGemItem { public override GemType GemType => GemType.Sapphire; public override int GemLevel => 1; }
public class Sapphire2 : LooteriaGemItem { public override GemType GemType => GemType.Sapphire; public override int GemLevel => 2; }
public class Sapphire3 : LooteriaGemItem { public override GemType GemType => GemType.Sapphire; public override int GemLevel => 3; }

public class Emerald0 : LooteriaGemItem { public override GemType GemType => GemType.Emerald; public override int GemLevel => 0; }
public class Emerald1 : LooteriaGemItem { public override GemType GemType => GemType.Emerald; public override int GemLevel => 1; }
public class Emerald2 : LooteriaGemItem { public override GemType GemType => GemType.Emerald; public override int GemLevel => 2; }
public class Emerald3 : LooteriaGemItem { public override GemType GemType => GemType.Emerald; public override int GemLevel => 3; }

public class Amethyst0 : LooteriaGemItem { public override GemType GemType => GemType.Amethyst; public override int GemLevel => 0; }
public class Amethyst1 : LooteriaGemItem { public override GemType GemType => GemType.Amethyst; public override int GemLevel => 1; }
public class Amethyst2 : LooteriaGemItem { public override GemType GemType => GemType.Amethyst; public override int GemLevel => 2; }
public class Amethyst3 : LooteriaGemItem { public override GemType GemType => GemType.Amethyst; public override int GemLevel => 3; }

public class Topaz0 : LooteriaGemItem { public override GemType GemType => GemType.Topaz; public override int GemLevel => 0; }
public class Topaz1 : LooteriaGemItem { public override GemType GemType => GemType.Topaz; public override int GemLevel => 1; }
public class Topaz2 : LooteriaGemItem { public override GemType GemType => GemType.Topaz; public override int GemLevel => 2; }
public class Topaz3 : LooteriaGemItem { public override GemType GemType => GemType.Topaz; public override int GemLevel => 3; }

public class Diamond0 : LooteriaGemItem { public override GemType GemType => GemType.Diamond; public override int GemLevel => 0; }
public class Diamond1 : LooteriaGemItem { public override GemType GemType => GemType.Diamond; public override int GemLevel => 1; }
public class Diamond2 : LooteriaGemItem { public override GemType GemType => GemType.Diamond; public override int GemLevel => 2; }
public class Diamond3 : LooteriaGemItem { public override GemType GemType => GemType.Diamond; public override int GemLevel => 3; }
