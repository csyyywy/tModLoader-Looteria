using Terraria.ModLoader;

namespace Looteria.Common.Systems;

/// <summary>热键注册（掠夺面板开关）。</summary>
public class UISystem : ModSystem
{
    public static ModKeybind PanelKeybind { get; private set; } = null!;

    /// <summary>面板是否打开（IngameFancyUI 无 CurrentState 属性，自行跟踪）。</summary>
    public static bool PanelOpen;

    public override void Load()
    {
        // 本地化键：Mods.Looteria.Keybind.LooteriaPanel
        PanelKeybind = KeybindLoader.RegisterKeybind(Mod, "LooteriaPanel", "P");
    }

    public override void Unload()
    {
        PanelKeybind = null!;
        PanelOpen = false;
    }
}
