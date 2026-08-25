namespace Looteria.Common.Data;

/// <summary>掉落来源。</summary>
public enum DropSource : byte
{
    Normal, Elite, Boss, Chest, Rift, Craft
}

/// <summary>
/// 稀有度权重矩阵（每 1000 权）。行 = 来源，列 = 稀有度 None/Magic/Rare/Legendary/Set。
/// 见 _devmem/02-design-tables.md。
/// </summary>
public static class DropTable
{
    public static int[] Weights(DropSource src) => src switch
    {
        DropSource.Normal => new[] { 500, 300, 150, 45, 5 },
        DropSource.Elite => new[] { 200, 350, 320, 110, 20 },
        DropSource.Boss => new[] { 0, 250, 450, 220, 80 },
        DropSource.Chest => new[] { 300, 400, 240, 55, 5 },
        DropSource.Rift => new[] { 0, 200, 420, 320, 60 },
        DropSource.Craft => new[] { 400, 350, 200, 45, 5 },
        _ => new[] { 500, 300, 150, 45, 5 }
    };

    /// <summary>按来源+配置倍率+秘境层掷一个稀有度。层数越高传说权重越大。</summary>
    public static LootRarity RollRarity(DropSource src, float mult, int riftLevel = 0)
    {
        var w = (int[])Weights(src).Clone();
        if (riftLevel > 0)
        {
            // 秘境层 → 传说/套装权重上移（从"普通/魔法"平移一部分）
            // M3：shift 钳制上限 0.9，防 34 层以上 w[0] 变负（None 永不可选、总权重失真）
            float shift = MathF.Min(0.03f * riftLevel, 0.9f);
            w[0] = Math.Max(0, (int)(w[0] * (1 - shift)));
            w[3] = (int)(w[3] * (1 + shift * 4));
            w[4] = (int)(w[4] * (1 + shift * 4));
        }
        if (mult != 1f)
        {
            w[1] = (int)(w[1] * mult);
            w[2] = (int)(w[2] * mult);
            w[3] = (int)(w[3] * mult);
            w[4] = (int)(w[4] * mult);
        }

        long total = 0;
        foreach (var x in w) total += x;
        if (total <= 0) return LootRarity.None;

        long r = Terraria.Main.rand.Next(0, (int)total);
        for (int i = 0; i < w.Length; i++)
        {
            r -= w[i];
            if (r < 0) return (LootRarity)i;
        }
        return LootRarity.None;
    }
}
