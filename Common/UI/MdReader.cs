using System.Collections.Generic;
using System.IO;
using Terraria.ModLoader;

namespace Looteria.Common.UI;

/// <summary>md 行：Kind 0=h1 1=h2/h3 2=列表项 3=正文 4=引用。</summary>
public readonly record struct MdLine(string Text, int Kind);

/// <summary>把 .md 转成轻量显示行（去语法：标题/列表/表格/引用/加粗/代码）。</summary>
public static class MdReader
{
    public static List<MdLine> Read(Mod mod, string path)
    {
        var list = new List<MdLine>();
        if (!mod.FileExists(path)) return list;
        using var reader = new StreamReader(mod.GetFileStream(path));
        string? line;
        while ((line = reader.ReadLine()) != null) Add(list, line);
        return list;
    }

    private static void Add(List<MdLine> list, string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0 || (t.StartsWith('|') && t.Contains("---"))) return; // 空行 / 表头分隔行
        int kind = 3;
        string txt = t;
        if (t.StartsWith("###")) { kind = 1; txt = t[3..].Trim(); }
        else if (t.StartsWith("##")) { kind = 1; txt = t[2..].Trim(); }
        else if (t.StartsWith('#')) { kind = 0; txt = t[1..].Trim(); }
        else if (t.StartsWith('>')) { kind = 4; txt = t[1..].Trim(); }
        else if (t.StartsWith("- ") || t.StartsWith("* ")) { kind = 2; txt = t[2..].Trim(); }
        else if (t.StartsWith('|')) txt = t.Trim('|').Replace("|", " · "); // 表格行 → 可读行
        txt = txt.Replace("**", "").Replace("`", "");
        list.Add(new MdLine(txt, kind));
    }
}
