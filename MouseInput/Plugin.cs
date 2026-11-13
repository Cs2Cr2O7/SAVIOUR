using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using SiNiSistar2.Obj;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sini2Mouse;

[BepInPlugin("sini2.mouseinput", "Sini2 Mouse Input", "1.2.3")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> Enabled;

    public override void Load()
    {
        LogSource = Log;
        Enabled = Config.Bind("Mouse", "Enabled", true, "鼠标攻击、侧键魔法剑、魔法弓指向。游戏中按 F8 切换。移动仍走键盘。");
        Harmony.CreateAndPatchAll(typeof(MousePatches));
        Harmony.CreateAndPatchAll(typeof(BowAimPatches));
        Log.LogInfo("Mouse input 1.2.3. Left=attack, side=magic sword, right=magic bow. Aim animation stays vanilla. F8 toggles.");
    }
}

static class MousePatches
{
    static Lelia _lelia;
    static bool _reported;
    static bool _reportedSlots;

    [HarmonyPatch(typeof(SiNiSistar2.InputManager), nameof(SiNiSistar2.InputManager.InputUpdate))]
    [HarmonyPostfix]
    static void AfterInputUpdate(SiNiSistar2.InputManager __instance)
    {
        Apply(__instance, CurrentLelia());
    }

    [HarmonyPatch(typeof(SiNiObject), nameof(SiNiObject.Update))]
    [HarmonyPrefix]
    static void BeforeObjectUpdate(SiNiObject __instance)
    {
        var lelia = __instance.TryCast<Lelia>();
        if (lelia == null)
            return;
        _lelia = lelia;
        if (lelia.Input != null)
            Apply(lelia.Input, lelia);
    }

    internal static Lelia CurrentLelia()
    {
        if (_lelia == null)
            _lelia = UnityEngine.Object.FindObjectOfType<Lelia>();
        return _lelia;
    }

    static void Apply(SiNiSistar2.InputManager input, Lelia lelia)
    {
        if (input == null)
            return;

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            Plugin.Enabled.Value = !Plugin.Enabled.Value;
            Plugin.LogSource.LogInfo(Plugin.Enabled.Value ? "Mouse input on" : "Mouse input off");
        }

        if (!Plugin.Enabled.Value || lelia == null || input.HasDisableInput())
            return;

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        int swordSlot = SlotOf(lelia.Magic01, lelia.Magic02, lelia.MagicSword);
        int arrowSlot = SlotOf(lelia.Magic01, lelia.Magic02, lelia.MagicArrow);
        if (!_reportedSlots && (lelia.MagicSword != null || lelia.MagicArrow != null))
        {
            _reportedSlots = true;
            Plugin.LogSource.LogInfo("Magic slots: sword=" + swordSlot + " arrow=" + arrowSlot);
        }

        bool attackDown = mouse.leftButton.wasPressedThisFrame;
        bool sideDown = mouse.forwardButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame;
        bool sideHeld = mouse.forwardButton.isPressed || mouse.backButton.isPressed;
        bool bowDown = mouse.rightButton.wasPressedThisFrame;
        bool bowHeld = mouse.rightButton.isPressed;

        if (attackDown)
            input.WasPressedThisFrameAttack = true;

        if (sideDown)
            input.WasPressedThisFrameSword = true;
        PressMagicSlot(input, swordSlot, sideDown, sideHeld);
        PressMagicSlot(input, arrowSlot, bowDown, bowHeld);

        if ((attackDown || sideDown || sideHeld || bowHeld) && MouseWorld.TryGet(lelia.transform.position, out Vector3 world))
            Face(lelia, world);

        if (!_reported)
        {
            _reported = true;
            Plugin.LogSource.LogInfo("Mouse buttons are driving attack, magic sword, and magic bow.");
        }
    }

    static int SlotOf(MagicActionBase slot01, MagicActionBase slot02, Il2CppObjectBase action)
    {
        if (action == null)
            return 0;
        if (slot01 != null && slot01.Pointer == action.Pointer)
            return 1;
        if (slot02 != null && slot02.Pointer == action.Pointer)
            return 2;
        return 0;
    }

    static void PressMagicSlot(SiNiSistar2.InputManager input, int slot, bool down, bool held)
    {
        if (slot == 1)
        {
            if (down)
                input.WasPressedThisFrameMagic01 = true;
            if (held)
                input.IsPressedMagic01 = true;
        }
        else if (slot == 2 && held)
        {
            input.IsPressedMagic02 = true;
        }
    }

    static void Face(Lelia lelia, Vector3 world)
    {
        var direction = lelia.Direction;
        if (direction == null)
            return;
        if (Mathf.Abs(world.x - lelia.transform.position.x) < 0.15f)
            return;
        direction.TurnToPosition(world, true, false);
    }
}

static class BowAimPatches
{
    [HarmonyPatch(typeof(MagicArrow), nameof(MagicArrow._CreateArrow))]
    [HarmonyPrefix]
    static void RedirectArrow(ref Vector3 from, ref Vector3 to)
    {
        if (!Plugin.Enabled.Value)
            return;
        if (!MouseWorld.TryGet(from, out Vector3 world))
            return;
        Vector3 delta = world - from;
        delta.z = 0f;
        float distance = delta.magnitude;
        if (distance < 0.05f)
            return;
        float shotLength = (to - from).magnitude;
        if (shotLength < 0.2f)
            shotLength = distance;
        to = from + delta / distance * shotLength;
    }
}

static class MouseWorld
{
    public static bool TryGet(Vector3 depthReference, out Vector3 world)
    {
        world = depthReference;
        Mouse mouse = Mouse.current;
        Camera camera = Camera.main;
        if (mouse == null || camera == null)
            return false;

        Vector3 screen = mouse.position.ReadValue();
        float depth = camera.WorldToScreenPoint(depthReference).z;
        if (depth < 0.01f)
            depth = 0.01f;
        screen.z = depth;
        world = camera.ScreenToWorldPoint(screen);
        world.z = depthReference.z;
        return true;
    }
}
