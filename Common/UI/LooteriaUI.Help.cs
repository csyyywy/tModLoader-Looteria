using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace Looteria.Common.UI;

/// <summary>说明页（Wiki 式：目录选文档 → 分页查看）。内容直接读 docs/ 的 .md。</summary>
public partial class LooteriaUIState
{
    private void BuildHelp()
    {
        int top = 8;
        AddSectionTitle(_content, T("HelpTitle"), C_Cyan, ref top);
        top += 6;

        // 文档目录（2 行 × 3 列按钮）
        for (int i = 0; i < HelpContent.Docs.Length; i++)
        {
            int idx = i;
            var b = new UITextPanel<string>(HelpContent.Docs[i].Label, 0.72f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(8f + (i % 3) * 188f, 0f),
                Width = new StyleDimension(180f, 0f),
                Height = new StyleDimension(28f, 0f)
            };
            b.BackgroundColor = _helpDoc == i ? C_Selected : new Color(44, 47, 66);
            b.BorderColor = _helpDoc == i ? C_Accent : new Color(60, 64, 84);
            b.WithFadedMouseOver();
            b.OnLeftClick += (_, _) => { _helpDoc = idx; _helpPage = 0; Rebuild(); };
            _content.Append(b);
            if (i % 3 == 2) top += 32;
        }
        top += 36;

        // 读取选中文档 → 显示行（去语法：标题/列表/表格/引用）
        var rows = new List<(string Text, float Scale, Color Color)>();
        float maxW = _content.GetDimensions().Width - 60f;
        var mod = global::Looteria.Looteria.Instance;
        if (mod != null)
        {
            bool skipH1 = true;
            foreach (var ln in MdReader.Read(mod, HelpContent.Docs[_helpDoc].File))
            {
                if (skipH1 && ln.Kind == 0) { skipH1 = false; continue; } // 首行 H1 已由目录按钮展示
                skipH1 = false;
                string text = ln.Kind == 2 ? "• " + ln.Text : ln.Text;
                Color color = ln.Kind is 0 or 1 ? C_Accent : ln.Kind == 4 ? C_Dim : Color.LightGray;
                float scale = ln.Kind is 0 or 1 ? 0.9f : 0.8f;
                WrapRows(text, scale, color, rows, maxW);
            }
        }

        // 分页：每页行数随高度自适应（一页页看，不糊块）
        int pageSize = Math.Max(8, (int)(_content.GetDimensions().Height - 300f) / 20);
        int pages = Math.Max(1, (rows.Count + pageSize - 1) / pageSize);
        _helpPage = Math.Clamp(_helpPage, 0, pages - 1);
        int start = _helpPage * pageSize;
        int end = Math.Min(rows.Count, start + pageSize);
        if (rows.Count == 0)
        {
            AddLabel(_content, T("HelpEmpty"), ref top, 0.8f, Color.Gray);
            top += 24;
        }
        for (int i = start; i < end; i++)
        {
            var r = rows[i];
            AddLabel(_content, r.Text, ref top, r.Scale, r.Color);
            top += (int)(20 * r.Scale) + 2;
            if (r.Scale > 0.85f) top += 4; // 标题行后留白
        }
        top += 6;

        // 翻页控件
        var prev = new UITextPanel<string>($"◀ {T("HelpPrev")}", 0.72f)
        { Top = new StyleDimension(top, 0f), Left = new StyleDimension(8f, 0f), Width = new StyleDimension(120f, 0f), Height = new StyleDimension(28f, 0f) };
        prev.BackgroundColor = new Color(50, 52, 70);
        prev.WithFadedMouseOver();
        prev.OnLeftClick += (_, _) => { if (_helpPage > 0) { _helpPage--; Rebuild(); } };
        _content.Append(prev);

        _content.Append(new UIText($"{_helpPage + 1}/{pages}", 0.8f)
        { Top = new StyleDimension(top + 4, 0f), Left = new StyleDimension(140f, 0f), TextColor = C_Cyan });

        var next = new UITextPanel<string>($"{T("HelpNext")} ▶", 0.72f)
        { Top = new StyleDimension(top, 0f), Left = new StyleDimension(210f, 0f), Width = new StyleDimension(120f, 0f), Height = new StyleDimension(28f, 0f) };
        next.BackgroundColor = new Color(50, 52, 70);
        next.WithFadedMouseOver();
        next.OnLeftClick += (_, _) => { if (_helpPage < pages - 1) { _helpPage++; Rebuild(); } };
        _content.Append(next);
    }

    /// <summary>字符级自动换行，把一行文字折成多行加入 rows。</summary>
    private static void WrapRows(string text, float scale, Color color, List<(string, float, Color)> rows, float maxW)
    {
        var font = FontAssets.MouseText.Value;
        foreach (var para in text.Split('\n'))
        {
            string line = "";
            foreach (char c in para)
            {
                if (font.MeasureString(line + c).X * scale > maxW && line.Length > 0)
                {
                    rows.Add((line, scale, color));
                    line = c.ToString();
                }
                else line += c;
            }
            if (line.Length > 0) rows.Add((line, scale, color));
        }
    }
}
