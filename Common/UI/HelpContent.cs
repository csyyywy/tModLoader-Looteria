namespace Looteria.Common.UI;

/// <summary>
/// 游戏内说明 = 直接读取 docs/ 文件夹里的 .md（与仓库文档同源，改文档即改游戏内说明）。
/// Label 只是列表标题；内容全部来自对应 md 文件。
/// </summary>
public static class HelpContent
{
    public static readonly (string File, string Label)[] Docs =
    {
        ("docs/README.md", "总览 · 快速开始"),
        ("docs/说明-稀有度与词缀.md", "稀有度与词缀"),
        ("docs/说明-抽奖.md", "抽奖"),
        ("docs/说明-宝石与插槽.md", "宝石与插槽"),
        ("docs/说明-货币与秘境.md", "货币与秘境"),
        ("docs/说明-兼容与配置.md", "兼容与配置"),
    };
}
