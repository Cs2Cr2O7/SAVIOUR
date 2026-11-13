using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sini2Mouse;

[BepInPlugin("sini2.mouseinput", "Sini2 Mouse Input", "1.1.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> Enabled;

    public override void Load()
    {
        LogSource = Log;
        Enabled = Config.Bind("Mouse", "Enabled", true, "鼠标攻击和魔法。游戏中按 F8 切换，并写入这个配置。移动仍走键盘。");
        Harmony.CreateAndPatchAll(typeof(MousePatches));
        Log.LogInfo("Mouse buttons ready. Left=attack, right=magic1, middle=magic2. Movement stays on the keyboard. F8 toggles.");
    }
}

static class MousePatches
{
    static SiNiSistar2.Obj.Lelia _lelia;
    static bool _reported;

    [HarmonyPatch(typeof(SiNiSistar2.InputManager), nameof(SiNiSistar2.InputManager.InputUpdate))]
    [HarmonyPostfix]
    static void AfterInputUpdate(SiNiSistar2.InputManager __instance)
    {
        Apply(__instance, CurrentLelia());
    }

    [HarmonyPatch(typeof(SiNiSistar2.Obj.SiNiObject), nameof(SiNiSistar2.Obj.SiNiObject.Update))]
    [HarmonyPrefix]
    static void BeforeObjectUpdate(SiNiSistar2.Obj.SiNiObject __instance)
    {
        var lelia = __instance.TryCast<SiNiSistar2.Obj.Lelia>();
        if (lelia == null)
            return;
        _lelia = lelia;
        if (lelia.Input != null)
            Apply(lelia.Input, lelia);
    }

    static SiNiSistar2.Obj.Lelia CurrentLelia()
    {
        if (_lelia == null)
            _lelia = Object.FindObjectOfType<SiNiSistar2.Obj.Lelia>();
        return _lelia;
    }

    static void Apply(SiNiSistar2.InputManager input, SiNiSistar2.Obj.Lelia lelia)
    {
        if (input == null)
            return;

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            Plugin.Enabled.Value = !Plugin.Enabled.Value;
            Plugin.LogSource.LogInfo(Plugin.Enabled.Value ? "Mouse buttons on" : "Mouse buttons off");
        }

        if (!Plugin.Enabled.Value || lelia == null || input.HasDisableInput())
            return;

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
            input.WasPressedThisFrameAttack = true;

        if (mouse.rightButton.wasPressedThisFrame)
            input.WasPressedThisFrameMagic01 = true;
        if (mouse.rightButton.isPressed)
            input.IsPressedMagic01 = true;

        if (mouse.middleButton.isPressed)
            input.IsPressedMagic02 = true;

        if (!_reported)
        {
            _reported = true;
            Plugin.LogSource.LogInfo("Mouse buttons are driving attack and magic.");
        }
    }
}
