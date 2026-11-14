using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using SiNiSistar2.Manager;
using SiNiSistar2.Obj;
using SiNiSistar2.UI.Pause;
using SiNiSistar2.UI.ToggleParts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Sini2Mouse;

[BepInPlugin("sini2.mouseinput", "Sini2 Mouse Input", "1.2.4")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> Enabled;

    public override void Load()
    {
        LogSource = Log;
        Enabled = Config.Bind("Mouse", "Enabled", true, "鼠标攻击、侧键魔法剑、魔法弓指向，以及菜单和对话点击。游戏中按 F8 切换。移动仍走键盘。");
        Harmony.CreateAndPatchAll(typeof(MousePatches));
        Harmony.CreateAndPatchAll(typeof(BowAimPatches));
        ClassInjector.RegisterTypeInIl2Cpp<UiMouseDriver>();
        var driver = new GameObject("Sini2UiMouse");
        UnityEngine.Object.DontDestroyOnLoad(driver);
        driver.AddComponent<UiMouseDriver>();
        Log.LogInfo("Mouse input 1.2.4. Left=attack, side=magic sword, right=magic bow. Hover keeps one row lit. F8 toggles.");
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
        UiMouse.Tick();
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
        UiMouse.Tick();
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

        if (!Plugin.Enabled.Value || lelia == null || input.HasDisableInput() || UiMouse.BlocksGameplay)
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

public class UiMouseDriver : MonoBehaviour
{
    public UiMouseDriver(System.IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        UiMouse.Tick();
    }

    public void LateUpdate()
    {
        UiMouse.ApplyCursor();
        UiMouse.ReapplyPointerHold();
    }
}

static class UiMouse
{
    const string MousePosition = "<Mouse>/position";
    const string MouseLeft = "<Mouse>/leftButton";
    const string MouseRight = "<Mouse>/rightButton";
    const string MouseScroll = "<Mouse>/scroll";

    static int _frame = -1;
    static bool _failed;
    static bool _loggedHook;
    static bool _loggedMenu;
    static bool _loggedDialogue;
    static bool _loggedSubmitDisabled;
    static bool _addedSubmit;
    static bool _addedCancel;
    static bool _addedPoint;
    static bool _addedClick;
    static bool _addedScroll;
    static InputAction _ownedPoint;
    static InputAction _ownedClick;
    static InputAction _ownedScroll;
    static bool _cursorForced;
    static bool _savedVisible;
    static CursorLockMode _savedLock;
    static bool _pointerHeld;
    static bool _clickWasEnabled;
    static bool _loggedManual;
    static bool _loggedManualFail;
    static bool _directClick;
    static string _manualListName;
    static System.IntPtr _cursorToggle;
    static Toggle _cursorRow;

    public static bool BlocksGameplay { get; private set; }
    public static bool WantsCursor { get; private set; }

    public static void Tick()
    {
        if (_frame == Time.frameCount || _failed)
            return;
        _frame = Time.frameCount;

        try
        {
            bool enabled = Plugin.Enabled.Value;
            bool menuOpen = false;
            bool selecting = false;
            bool dramaOpen = false;
            InputAction submit = null;
            InputAction cancel = null;
            ReadManagers(ref dramaOpen, ref submit, ref cancel);
            if (enabled)
                ScanMenus(ref menuOpen, ref selecting);

            BlocksGameplay = enabled && (menuOpen || dramaOpen);
            WantsCursor = BlocksGameplay;

            if (!enabled)
            {
                SetOwnedBinding(submit, MouseLeft, false, ref _addedSubmit);
                SetOwnedBinding(cancel, MouseRight, false, ref _addedCancel);
                HoldModulePointer(false);
                ReleaseModuleBindings();
                return;
            }

            EnsureModule();
            bool menuDrive = DriveOpenMenus();
            HoldModulePointer(menuDrive);
            bool advanceDialogue = dramaOpen && !selecting;
            bool wantSubmit = advanceDialogue || (menuDrive && !_directClick);
            SetOwnedBinding(submit, MouseLeft, wantSubmit, ref _addedSubmit);
            SetOwnedBinding(cancel, MouseRight, false, ref _addedCancel);
            if (wantSubmit && submit != null && !submit.enabled && !_loggedSubmitDisabled)
            {
                _loggedSubmitDisabled = true;
                Plugin.LogSource.LogInfo("UISubmit is disabled while the pointer wants to confirm.");
            }

            if (advanceDialogue && !_loggedDialogue)
            {
                _loggedDialogue = true;
                Plugin.LogSource.LogInfo("Left click advances dialogue while no menu choice is selected.");
            }
            if (selecting && !_loggedMenu)
            {
                _loggedMenu = true;
                Plugin.LogSource.LogInfo("Mouse hover and left click drive the open menu. Right click cancels.");
            }
        }
        catch (System.Exception ex)
        {
            _failed = true;
            BlocksGameplay = false;
            WantsCursor = false;
            Plugin.LogSource.LogError("UI mouse stopped: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static void ApplyCursor()
    {
        if (_failed)
            return;
        if (WantsCursor && !_cursorForced)
        {
            _savedVisible = Cursor.visible;
            _savedLock = Cursor.lockState;
            _cursorForced = true;
        }
        if (WantsCursor)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }
        if (_cursorForced)
        {
            Cursor.visible = _savedVisible;
            Cursor.lockState = _savedLock;
            _cursorForced = false;
        }
    }

    static void ScanMenus(ref bool menuOpen, ref bool selecting)
    {
        var lists = UnityEngine.Object.FindObjectsOfType<ToggleListUI>();
        if (lists == null)
            return;
        for (int i = 0; i < lists.Length; i++)
        {
            var list = lists[i];
            if (!IsShownMenu(list))
                continue;
            menuOpen = true;
            if (list.IsSelectState)
                selecting = true;
            EnsureRaycaster(list);
        }
    }

    static bool DriveOpenMenus()
    {
        _directClick = false;
        var lists = UnityEngine.Object.FindObjectsOfType<ToggleListUI>();
        ToggleListUI active = PickActiveMenu(lists);
        if (active == null)
            return false;

        _directClick = UsesDirectClick(active);
        if (CancelRequested(active))
            RequestCancel(active);
        if (Mouse.current == null)
            return true;

        Vector2 screen = Mouse.current.position.ReadValue();
        ToggleListUI list = null;
        Toggle toggle = null;
        int seenToggles = 0;
        if (!TryRaycastToggle(lists, screen, out list, out toggle))
        {
            float bestArea = float.MaxValue;
            int bestDepth = -1;
            for (int i = 0; i < lists.Length; i++)
            {
                var candidate = lists[i];
                if (!IsShownMenu(candidate))
                    continue;
                ConsiderToggles(candidate, candidate.UsingToggles, screen, ref list, ref toggle, ref bestArea, ref bestDepth, ref seenToggles);
                var children = candidate.GetComponentsInChildren<Toggle>(false);
                ConsiderToggles(candidate, children, screen, ref list, ref toggle, ref bestArea, ref bestDepth, ref seenToggles);
            }
        }

        if (list == null || toggle == null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame && _manualListName != "miss")
            {
                _manualListName = "miss";
                Plugin.LogSource.LogInfo("Menu click did not hit a row. list=" + active.GetIl2CppType().Name + " toggles=" + seenToggles);
            }
            return true;
        }

        _directClick = UsesDirectClick(list);
        string name = list.GetIl2CppType().Name;
        if (_manualListName != name)
        {
            _manualListName = name;
            if (!_loggedManual)
            {
                _loggedManual = true;
                Plugin.LogSource.LogInfo("Menu row hit on " + name + ". Hover moves the cursor. Left click uses that row.");
            }
        }

        try
        {
            MoveHighlight(list, toggle);
        }
        catch (System.Exception ex)
        {
            if (!_loggedManualFail)
            {
                _loggedManualFail = true;
                Plugin.LogSource.LogWarning("Menu row drive failed: " + ex.Message);
            }
        }
        return true;
    }

    static ToggleListUI PickActiveMenu(ToggleListUI[] lists)
    {
        if (lists == null)
            return null;
        ToggleListUI best = null;
        int bestScore = int.MinValue;
        int bestDepth = -1;
        for (int i = 0; i < lists.Length; i++)
        {
            var list = lists[i];
            if (!IsShownMenu(list))
                continue;
            int score = list.IsSelectState ? 2 : 1;
            int depth = Depth(list.transform);
            if (score > bestScore || (score == bestScore && depth > bestDepth))
            {
                best = list;
                bestScore = score;
                bestDepth = depth;
            }
        }
        return best;
    }

    static bool CancelRequested(ToggleListUI list)
    {
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool right = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        if (right)
            return true;
        return escape && UsesDirectClick(list);
    }

    static void RequestCancel(ToggleListUI list)
    {
        if (list.m_DisableCancel)
            return;
        list.IsCanceled = true;
        if (!list.IsSelectState)
            list.ForceCloseFlag = true;
    }

    static void MoveHighlight(ToggleListUI list, Toggle toggle)
    {
        bool direct = UsesDirectClick(list);
        bool changed = toggle.Pointer != _cursorToggle;
        if (changed)
        {
            _cursorToggle = toggle.Pointer;
            if (direct)
                MoveCursorOnly(list, toggle);
            else
                MoveChoiceCursor(list, toggle);
        }

        if (direct && Mouse.current.leftButton.wasPressedThisFrame)
            ActivateDirect(list, toggle);
    }

    static void MoveCursorOnly(ToggleListUI list, Toggle toggle)
    {
        var previous = _cursorRow;
        list.OnCursorToggle = toggle;
        _cursorRow = toggle;
        if (previous != null && previous.Pointer != toggle.Pointer)
            list.PlayOneToggleAnimation(previous);
        list.PlayOneToggleAnimation(toggle);
    }

    static void MoveChoiceCursor(ToggleListUI list, Toggle toggle)
    {
        if (toggle.group != null)
            toggle.group.NotifyToggleOn(toggle, false);
        else if (!toggle.isOn)
            toggle.SetIsOnWithoutNotify(true);

        list.OnCursorToggle = toggle;
        var eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject != toggle.gameObject)
            eventSystem.SetSelectedGameObject(toggle.gameObject);
        list.PlayOneToggleAnimation(toggle);
    }

    static void ActivateDirect(ToggleListUI list, Toggle toggle)
    {
        var video = list.TryCast<VideoUI>();
        if (video != null)
        {
            if (SameToggle(toggle, video.m_Resolution))
                video.OnInputHorizontal(toggle, 1);
            else if (SameToggle(toggle, video.m_SetDefault))
                ConfirmRow(list, toggle);
            else
                toggle.isOn = !toggle.isOn;
            return;
        }

        var audio = list.TryCast<AudioUI>();
        if (audio != null)
        {
            audio.OnInputHorizontal(toggle, 1);
            return;
        }

        var play = list.TryCast<GamePlayUI>();
        if (play != null)
        {
            if (SameToggle(toggle, play.m_SetDefault))
                ConfirmRow(list, toggle);
            else
                toggle.isOn = !toggle.isOn;
            return;
        }

        ConfirmRow(list, toggle);
    }

    static void ConfirmRow(ToggleListUI list, Toggle toggle)
    {
        list.OnCursorToggle = toggle;
        list.SelectedToggle = toggle;
        if (list.IsSelectState)
            list.ForceNext();
    }

    static bool UsesDirectClick(ToggleListUI list)
    {
        string name = list.GetIl2CppType().Name;
        return name == "VideoUI"
            || name == "AudioUI"
            || name == "GamePlayUI"
            || name == "ControllerUI"
            || name == "KeyboardUI";
    }

    static bool TryRaycastToggle(ToggleListUI[] lists, Vector2 screen, out ToggleListUI list, out Toggle toggle)
    {
        list = null;
        toggle = null;
        var eventSystem = EventSystem.current;
        if (eventSystem == null || lists == null)
            return false;

        var data = new PointerEventData(eventSystem);
        data.position = screen;
        var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        eventSystem.RaycastAll(data, results);
        for (int i = 0; i < results.Count; i++)
        {
            var target = results[i].gameObject;
            if (target == null)
                continue;
            var found = target.GetComponentInParent<Toggle>();
            if (found == null || !found.isActiveAndEnabled)
                continue;
            var owner = found.GetComponentInParent<ToggleListUI>();
            if (!IsShownMenu(owner))
                continue;
            list = owner;
            toggle = found;
            return true;
        }
        return false;
    }

    static bool IsShownMenu(ToggleListUI list)
    {
        if (list == null || !list.isActiveAndEnabled || (!list.IsOpen && !list.IsSelectState))
            return false;
        Transform cursor = list.transform;
        while (cursor != null)
        {
            var group = cursor.GetComponent<CanvasGroup>();
            if (group != null && group.alpha <= 0.01f)
                return false;
            cursor = cursor.parent;
        }
        return true;
    }

    static void ConsiderToggles(ToggleListUI list, Toggle[] toggles, Vector2 screen, ref ToggleListUI bestList, ref Toggle bestToggle, ref float bestArea, ref int bestDepth, ref int seen)
    {
        if (toggles == null)
            return;
        for (int i = 0; i < toggles.Length; i++)
            ConsiderToggle(list, toggles[i], screen, ref bestList, ref bestToggle, ref bestArea, ref bestDepth, ref seen);
    }

    static void ConsiderToggles(ToggleListUI list, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Toggle> toggles, Vector2 screen, ref ToggleListUI bestList, ref Toggle bestToggle, ref float bestArea, ref int bestDepth, ref int seen)
    {
        if (toggles == null)
            return;
        for (int i = 0; i < toggles.Length; i++)
            ConsiderToggle(list, toggles[i], screen, ref bestList, ref bestToggle, ref bestArea, ref bestDepth, ref seen);
    }

    static void ConsiderToggle(ToggleListUI list, Toggle toggle, Vector2 screen, ref ToggleListUI bestList, ref Toggle bestToggle, ref float bestArea, ref int bestDepth, ref int seen)
    {
        if (toggle == null || !toggle.isActiveAndEnabled)
            return;
        var owner = toggle.GetComponentInParent<ToggleListUI>();
        if (owner == null || owner.Pointer != list.Pointer)
            return;
        seen++;
        if (!TryToggleArea(toggle, screen, out float area))
            return;
        int depth = Depth(toggle.transform);
        if (depth > bestDepth || (depth == bestDepth && area < bestArea))
        {
            bestDepth = depth;
            bestArea = area;
            bestList = list;
            bestToggle = toggle;
        }
    }

    static bool TryToggleArea(Toggle toggle, Vector2 screen, out float area)
    {
        area = float.MaxValue;
        bool hit = false;
        var rects = toggle.GetComponentsInChildren<RectTransform>(false);
        if (rects == null)
            return false;
        for (int i = 0; i < rects.Length; i++)
        {
            var rect = rects[i];
            if (rect == null || !ContainsScreen(rect, screen))
                continue;
            var size = rect.rect.size;
            float candidate = Mathf.Abs(size.x * size.y);
            if (candidate < 4f)
                continue;
            hit = true;
            if (candidate < area)
                area = candidate;
        }
        return hit;
    }

    static bool ContainsScreen(RectTransform rect, Vector2 screen)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            camera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        if (RectContains(rect, screen, camera))
            return true;
        return camera != null && RectContains(rect, screen, null);
    }

    static bool RectContains(RectTransform rect, Vector2 screen, Camera camera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out var local))
            return false;
        var area = rect.rect;
        area.xMin -= 16f;
        area.xMax += 16f;
        area.yMin -= 12f;
        area.yMax += 12f;
        return area.Contains(local);
    }

    static int Depth(Transform transform)
    {
        int depth = 0;
        while (transform != null)
        {
            depth++;
            transform = transform.parent;
        }
        return depth;
    }

    static bool SameToggle(Toggle left, Toggle right)
    {
        if (left == null || right == null)
            return false;
        return left.Pointer == right.Pointer;
    }

    public static void ReapplyPointerHold()
    {
        if (_pointerHeld)
            HoldModulePointer(true);
    }

    static void HoldModulePointer(bool hold)
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        var module = eventSystem != null ? eventSystem.GetComponent<InputSystemUIInputModule>() : null;
        if (!hold)
        {
            if (_pointerHeld && module != null)
                SetActionEnabled(module.leftClick, _clickWasEnabled);
            _pointerHeld = false;
            return;
        }
        if (module == null || module.leftClick == null || module.leftClick.action == null)
            return;
        if (!_pointerHeld)
        {
            _clickWasEnabled = module.leftClick.action.enabled;
            _pointerHeld = true;
        }
        if (module.leftClick.action.enabled)
            module.leftClick.action.Disable();
    }

    static bool IsActionEnabled(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.enabled;
    }

    static void SetActionEnabled(InputActionReference reference, bool enabled)
    {
        var action = reference != null ? reference.action : null;
        if (action == null)
            return;
        if (enabled && !action.enabled)
            action.Enable();
        else if (!enabled && action.enabled)
            action.Disable();
    }

    static void ReadManagers(ref bool dramaOpen, ref InputAction submit, ref InputAction cancel)
    {
        var managers = ManagerList.Instance;
        if (managers == null || managers.IsForbiddenManagerAccess)
            return;
        var drama = ManagerList.Drama;
        dramaOpen = drama != null && drama.IsOpened;
        var input = ManagerList.Input;
        if (input == null)
            return;
        submit = input.UISubmit;
        cancel = input.UICancel;
    }

    static void EnsureRaycaster(ToggleListUI list)
    {
        var canvas = list.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.GetComponent<GraphicRaycaster>() != null)
            return;
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        Plugin.LogSource.LogInfo("Added a GraphicRaycaster to " + canvas.name);
    }

    static void EnsureModule()
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null)
            return;
        var module = eventSystem.GetComponent<InputSystemUIInputModule>();
        bool addedModule = false;
        if (module == null)
        {
            module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            addedModule = true;
        }
        module.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
        EnsurePointerAction(module.point, MousePosition, "Vector2", "Sini2UIPoint", ref _ownedPoint, action => module.point = action, ref _addedPoint);
        EnsurePointerAction(module.leftClick, MouseLeft, "Button", "Sini2UIClick", ref _ownedClick, action => module.leftClick = action, ref _addedClick);
        EnsurePointerAction(module.scrollWheel, MouseScroll, "Vector2", "Sini2UIScroll", ref _ownedScroll, action => module.scrollWheel = action, ref _addedScroll);
        if (!_loggedHook)
        {
            _loggedHook = true;
            Plugin.LogSource.LogInfo("UI pointer ready. point=" + Describe(module.point)
                + " click=" + Describe(module.leftClick)
                + " scroll=" + Describe(module.scrollWheel)
                + (addedModule ? " module=added" : " module=existing"));
        }
    }

    static string Describe(InputActionReference reference)
    {
        var action = reference != null ? reference.action : null;
        if (action == null)
            return "missing";
        return action.name + (action.enabled ? " enabled" : " disabled");
    }

    static void EnsurePointerAction(InputActionReference reference, string path, string controlType, string ownedName, ref InputAction owned, System.Action<InputActionReference> assign, ref bool addedBinding)
    {
        var action = reference != null ? reference.action : null;
        if (action == null)
        {
            if (owned == null)
            {
                owned = new InputAction(ownedName, InputActionType.PassThrough, path, null, null, controlType);
                owned.Enable();
            }
            if (!owned.enabled)
                owned.Enable();
            assign(InputActionReference.Create(owned));
            return;
        }
        if (!addedBinding && !HasBinding(action, path))
        {
            action.AddBinding(path);
            addedBinding = true;
        }
    }

    static void ReleaseModuleBindings()
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null)
            return;
        var module = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null)
            return;
        if (module.point != null)
            SetOwnedBinding(module.point.action, MousePosition, false, ref _addedPoint);
        if (module.leftClick != null)
            SetOwnedBinding(module.leftClick.action, MouseLeft, false, ref _addedClick);
        if (module.scrollWheel != null)
            SetOwnedBinding(module.scrollWheel.action, MouseScroll, false, ref _addedScroll);
        if (_ownedPoint != null)
            _ownedPoint.Disable();
        if (_ownedClick != null)
            _ownedClick.Disable();
        if (_ownedScroll != null)
            _ownedScroll.Disable();
    }

    static void SetOwnedBinding(InputAction action, string path, bool want, ref bool added)
    {
        if (action == null)
            return;
        if (want)
        {
            if (added || HasBinding(action, path))
                return;
            action.AddBinding(path);
            added = true;
            return;
        }
        if (!added)
            return;
        if (HasBinding(action, path))
            action.ChangeBindingWithPath(path).Erase();
        added = false;
    }

    static bool HasBinding(InputAction action, string path)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (PathEquals(binding.effectivePath, path) || PathEquals(binding.path, path) || PathEquals(binding.overridePath, path))
                return true;
        }
        return false;
    }

    static bool PathEquals(string candidate, string path)
    {
        return !string.IsNullOrEmpty(candidate) && string.Equals(candidate, path, System.StringComparison.OrdinalIgnoreCase);
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
