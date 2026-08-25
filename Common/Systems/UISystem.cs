using Terraria.ModLoader;

namespace Looteria.Common.Systems;

/// <summary>热键注册（掠夺面板 + 角色属性面板开关）。</summary>
public class UISystem : ModSystem
{
    public static ModKeybind PanelKeybind { get; private set; } = null!;
    public static ModKeybind CharSheetKeybind { get; private set; } = null!;

    /// <summary>掠夺面板是否打开（IngameFancyUI 无 CurrentState 属性，自行跟踪）。</summary>
    public static bool PanelOpen;

    /// <summary>角色属性面板是否打开。</summary>
    public static bool CharSheetOpen;

    public override void Load()
    {
        // 本地化键：Mods.Looteria.Keybind.LooteriaPanel / LooteriaCharSheet
        PanelKeybind = KeybindLoader.RegisterKeybind(Mod, "LooteriaPanel", "P");
        CharSheetKeybind = KeybindLoader.RegisterKeybind(Mod, "LooteriaCharSheet", "C");
    }

    public override void Unload()
    {
        PanelKeybind = null!;
        CharSheetKeybind = null!;
        PanelOpen = false;
        CharSheetOpen = false;
    }
}
