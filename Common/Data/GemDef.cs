using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace Looteria.Common.Data;

/// <summary>宝石类型。</summary>
public enum GemType : byte
{
    Ruby, Sapphire, Emerald, Amethyst, Topaz, Diamond
}

/// <summary>
/// 宝石定义：id = (int)Type*4 + Level + 1（1..24；0=空插槽）。
/// Level: 0=Flawed 1=Normal 2=Flawless 3=Perfect。
/// 见 _devmem/02-design-tables.md。
/// </summary>
public static class GemDatabase
{
    public const int Types = 6;
    public const int Levels = 4;

    public static int Id(GemType type, int level) => (int)type * Levels + level + 1;

    public static GemType GetType(int gemId) => IsValid(gemId) ? (GemType)((gemId - 1) / Levels) : 0; // L8：无效 id 防御

    public static int GetLevel(int gemId) => IsValid(gemId) ? (gemId - 1) % Levels : 0; // L8

    public static bool IsValid(int gemId) => gemId >= 1 && gemId <= Types * Levels;

    /// <summary>本地化键（Mods.Looteria.Gem.{type}.{level}）。</summary>
    public static string Key(int gemId)
    {
        if (!IsValid(gemId)) return "Mods.Looteria.Gem.0.0";
        return $"Mods.Looteria.Gem.{(int)GetType(gemId)}.{GetLevel(gemId)}";
    }

    public static List<int> AllIds() => Enumerable.Range(1, Types * Levels).ToList();

    /// <summary>按世界进度掷一颗宝石（类型随机，等级随 Boss 进度提高）。</summary>
    public static int RollGemIdForProgression()
    {
        var type = (GemType)Main.rand.Next(Types);
        int level;
        if (NPC.downedMoonlord) level = Main.rand.Next(2, 4);        // 2~3 完美
        else if (NPC.downedGolemBoss) level = Main.rand.Next(2, 3);  // 2 无瑕
        else if (NPC.downedPlantBoss) level = Main.rand.Next(1, 3);  // 1~2
        else if (Main.hardMode) level = Main.rand.Next(0, 2);        // 0~1
        else level = 0;                                              // 0 瑕疵
        return Id(type, level);
    }
}
