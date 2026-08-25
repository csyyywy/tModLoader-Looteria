using Terraria;

namespace Looteria.Common.Data;

/// <summary>主题套装（2/4/6 件激活加成）。Theme 枚举本身即 13 个主题。</summary>
public static class SetThemeDatabase
{
    public const int Count = 13; // Theme 枚举值数

    public static Theme PickRandom() => (Theme)Main.rand.Next(Count);
}
