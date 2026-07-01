using Quartz.Core;
using Quartz.IO;
using MonsterLove.StateMachine;
using SkyHook;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Quartz.Features.KeyLimiter;

// Only counts the allowed keys as gameplay hits, ported from the original
// KorenResourcePack's KeyLimiter (itself modeled on fangshenghan's
// KeyboardChatterBlocker mod, which bundles a key limiter with the chatter
// blocker). Mouse buttons are always allowed. Enforcement only happens during
// PlayerControl, so menus/editor typing are untouched.
//
// The actual blocking lives in ChatterBlocker's patches (one shared
// CountValidKeysPressed prefix and one SkyHook prefix handle both features,
// like v1): this class owns the allowed set, the player-control check and the
// add/remove key capture mode.
public static class KeyLimiter {
    public static SettingsFile<KeyLimiterSettings> ConfMgr { get; private set; }
    public static KeyLimiterSettings Conf => ConfMgr?.Data;

    // Fired when the allowed set or capture state changes, so the Gameplay
    // page can refresh its key list live.
    public static event Action Changed;

    public static void EnsureConf() {
        if(ConfMgr != null) return;

        ConfMgr = new SettingsFile<KeyLimiterSettings>(Path.Combine(MainCore.Paths.RootPath, "KeyLimiter.json"));
        ConfMgr.Load();
        EnsureTicker();
    }

    public static void Save() => ConfMgr?.RequestSave();

    public static bool IsEnabled() {
        EnsureConf();
        return MainCore.IsModEnabled && Conf.Enabled;
    }

    // Capture mode suspends enforcement, like v1's KeyCapture did — otherwise
    // adding a key would require pressing a currently-blocked key mid-play.
    public static bool IsActive() => IsEnabled() && !IsCapturing;

    // ===== player-control state =====
    //
    // The SkyHook callback runs off the main thread, where Unity API access
    // (and scrController) is off-limits; it reads a volatile snapshot the
    // main-thread ticker refreshes every frame, exactly like v1.

    private static int cachedPlayerControlFrame = -1;
    private static bool cachedPlayerControl;
    private static int cachedPlayerControlForHooks;

    public static bool InPlayerControl() {
        int frame = Time.frameCount;
        if(cachedPlayerControlFrame == frame) return cachedPlayerControl;

        cachedPlayerControlFrame = frame;
        SetCachedPlayerControl(false);
        try {
            scrController controller = scrController.instance;
            if(controller == null) return false;
            if(controller.paused || !controller.gameworld) return false;
            SetCachedPlayerControl(((StateBehaviour)controller).stateMachine.GetState() is States state
                && state == States.PlayerControl);
            return cachedPlayerControl;
        } catch {
            SetCachedPlayerControl(false);
            return false;
        }
    }

    public static bool InPlayerControlCached() => Volatile.Read(ref cachedPlayerControlForHooks) != 0;

    private static void SetCachedPlayerControl(bool value) {
        cachedPlayerControl = value;
        Volatile.Write(ref cachedPlayerControlForHooks, value ? 1 : 0);
    }

    // ===== allowed keys =====

    private static readonly HashSet<int> cachedAllowedKeys = [];
    private static int[] cachedAllowedSource;
    private static int cachedAllowedLength = -1;

    public static bool IsAllowedKey(KeyCode key) {
        int[] allowed = Conf?.AllowedKeys;
        if(allowed == null) return false;

        if(!ReferenceEquals(allowed, cachedAllowedSource) || allowed.Length != cachedAllowedLength) {
            cachedAllowedKeys.Clear();
            for(int i = 0; i < allowed.Length; i++) {
                cachedAllowedKeys.Add((int)NormalizeKey((KeyCode)allowed[i]));
            }

            cachedAllowedSource = allowed;
            cachedAllowedLength = allowed.Length;
        }

        return cachedAllowedKeys.Contains((int)NormalizeKey(key));
    }

    public static bool IsMouseKey(KeyCode key) => key is >= KeyCode.Mouse0 and <= KeyCode.Mouse6;

    public static bool ShouldBlockKey(KeyCode key)
        => IsActive() && InPlayerControl() && !IsMouseKey(key) && !IsAllowedKey(key);

    public static void ToggleAllowedKey(KeyCode key) {
        EnsureConf();

        key = NormalizeKey(key);
        if(key == KeyCode.None || IsMouseKey(key)) return;

        List<int> keys = [.. Conf.AllowedKeys];
        if(!keys.Remove((int)key)) keys.Add((int)key);

        Conf.AllowedKeys = [.. keys];
        PersistChange();
    }

    // Wholesale replacement of the allowed list — used by the key viewer's
    // "sync to key limiter" option.
    public static void SetAllowedKeys(int[] keys) {
        EnsureConf();

        Conf.AllowedKeys = keys ?? [];
        PersistChange();
    }

    // ===== profiles =====
    //
    // The limiter enforces exactly one profile at a time (the active one),
    // each its own named allowed set — e.g. a 12-key and a 16-key layout you
    // switch between. All the allowed-key helpers above operate on the active
    // profile via Conf.AllowedKeys, so switching just repoints them.

    public static IReadOnlyList<KeyLimiterProfile> Profiles {
        get { EnsureConf(); return Conf.Profiles; }
    }

    public static int ActiveProfileIndex {
        get { EnsureConf(); return Conf.ActiveProfile; }
    }

    public static void SwitchProfile(int index) {
        EnsureConf();
        if(index < 0 || index >= Conf.Profiles.Count || index == Conf.ActiveProfile) return;

        // A pending key capture targets the old profile's list — drop it.
        CancelCapture();
        Conf.ActiveProfile = index;
        PersistChange();
    }

    // Adds an empty profile and makes it active so it can be configured.
    public static void AddProfile() {
        EnsureConf();
        Conf.Profiles.Add(new KeyLimiterProfile {
            Name = "Profile " + (Conf.Profiles.Count + 1),
            Keys = [],
        });
        Conf.ActiveProfile = Conf.Profiles.Count - 1;
        PersistChange();
    }

    // Removes the active profile. The last profile can't be removed — the
    // limiter always needs one set to enforce.
    public static void RemoveActiveProfile() {
        EnsureConf();
        if(Conf.Profiles.Count <= 1) return;

        CancelCapture();
        Conf.Profiles.RemoveAt(Conf.ActiveProfile);
        if(Conf.ActiveProfile >= Conf.Profiles.Count) Conf.ActiveProfile = Conf.Profiles.Count - 1;
        PersistChange();
    }

    public static void RenameActiveProfile(string name) {
        EnsureConf();
        Conf.ActiveProfileOrDefault().Name = name ?? "";
        PersistChange();
    }

    // ===== key normalization (ported from v1's KeyCodeCompat) =====

    // v1 stored async keys as 0x1000 + Windows virtual-key in old configs.
    private const int LegacyAsyncKeyOffset = 0x1000;
    private const int LegacyAsyncKeyMax = LegacyAsyncKeyOffset + 0xFF;

    public static KeyCode NormalizeKey(KeyCode key) {
        key = NormalizeLegacyAsyncKey(key);
        if(key == KeyCode.AltGr) return KeyCode.RightAlt;
        // Unity's legacy Input reports the numpad Enter as Return and can't tell
        // them apart, while the SkyHook hook gives the distinct KeypadEnter.
        // Fold them together so an allowed-key set captured one way still matches
        // a press detected the other way (the numpad Enter was getting blocked).
        if(key == KeyCode.KeypadEnter) return KeyCode.Return;
        return key;
    }

    // DM Note presets store some keys as raw Windows virtual-key codes instead of
    // names — notably the Korean Hangul / Right-Alt key, which Windows reports as
    // VK 0x15 (=21). Interpret a numeric key token as a VK code first; only fall
    // back to a raw KeyCode cast when it isn't a known virtual key, so a bare "21"
    // resolves to KeyCode.RightAlt rather than the undefined (KeyCode)21 (which
    // renders as "21" and never registers under Input.GetKey).
    public static KeyCode NormalizeNumericKey(int numeric) {
        if(numeric >= 0 && numeric <= 0xFF) {
            KeyCode vk = WindowsVirtualKeyToUnityKey((ushort)numeric);
            if(vk != KeyCode.None) return vk;
        }
        return NormalizeKey((KeyCode)numeric);
    }

    private static KeyCode NormalizeLegacyAsyncKey(KeyCode key) {
        int raw = (int)key;
        if(raw < LegacyAsyncKeyOffset || raw > LegacyAsyncKeyMax) return key;

        KeyCode mapped = WindowsVirtualKeyToUnityKey((ushort)(raw - LegacyAsyncKeyOffset));
        return mapped == KeyCode.None ? key : mapped;
    }

    // ===== async (SkyHook) key mapping, ported from v1 =====

    // Switch on the enum value, not label.ToString(): Mono's Enum.ToString does
    // a reflective name lookup and allocates a fresh string every call, and this
    // runs on the SkyHook hook thread for every key edge.
    public static bool IsMouseLabel(KeyLabel label) => label is
        KeyLabel.MouseLeft or KeyLabel.MouseRight or KeyLabel.MouseMiddle or KeyLabel.MouseX1 or KeyLabel.MouseX2;

    public static bool ShouldBlockAsyncKeyFromHook(ushort key, KeyLabel label) {
        if(!IsActive() || !InPlayerControlCached() || IsMouseLabel(label)) return false;

        KeyCode unityKey = HookKeyToPhysicalUnityKey(key, label);
        if(IsMouseKey(unityKey)) return false;
        if(unityKey != KeyCode.None && IsAllowedKey(unityKey)) return false;

        KeyCode mappedKey = SkyHookKeyMapper.SkyHookKeyToUnityKey(label);
        if(mappedKey == KeyCode.None && IsAllowedGenericModifierVirtualKey(key)) return false;

        return mappedKey == KeyCode.None || !IsAllowedKey(mappedKey);
    }

    private static bool IsAllowedGenericModifierVirtualKey(ushort key) {
        switch(key) {
            case 0x10:
                return IsAllowedKey(KeyCode.LeftShift) || IsAllowedKey(KeyCode.RightShift);
            case 0x11:
                return IsAllowedKey(KeyCode.LeftControl) || IsAllowedKey(KeyCode.RightControl);
            case 0x12:
                return IsAllowedKey(KeyCode.LeftAlt) || IsAllowedKey(KeyCode.RightAlt)
                    || IsAllowedKey(KeyCode.AltGr);
            default:
                return false;
        }
    }

    // ===== hook-fed held-key state (for the key viewer) =====
    //
    // Unity's legacy Input is blind to a few physical keys — notably the Korean
    // Hangul (한/영) and Hanja keys, which Windows reports as VK 0x15 / 0x19
    // rather than the Right-Alt / Right-Control virtual keys Input.GetKey
    // watches, so a viewer box bound to RightAlt/RightControl never lit. The
    // SkyHook hook does see them (it's how the limiter blocks them), so the
    // chatter-blocker hook prefix forwards those edges here and the viewer
    // consults this as a fallback when Input.GetKey comes up empty.
    //
    // Written from the SkyHook thread (NoteHookEvent) and read from the main
    // thread (HookKeyHeld), so both held collections are lock-guarded (on the
    // hookHeldUntil object). Two schemes, picked per platform at the press edge:
    //
    //  - Reliable-release platforms (macOS/Linux, where libuiohook pairs every
    //    KeyPressed with a KeyReleased): the press marks the key held in
    //    hookHeldKeys and it stays lit until the real release edge clears it. A
    //    fixed expiry can't be used here — RightAlt/RightControl are real
    //    modifiers that DON'T auto-repeat, so the old refresh-on-repeat window
    //    expired (HookHeldWindowMs) while the key was still physically held,
    //    dropping the box after a fraction of a second.
    //  - Windows: the right-Ctrl/right-Alt positions are the Korean Hangul/Hanja
    //    toggle keys (VK 0x15 / 0x19), which emit a press but no reliable
    //    release. Those use the expiring window so a missing release can't leave
    //    a box stuck lit; each auto-repeat refreshes it. (Real RightAlt on a
    //    non-Korean Windows layout is seen by Unity's Input directly, so it never
    //    reaches this fallback.)
    private const int HookHeldWindowMs = 250;
    private static readonly Dictionary<KeyCode, int> hookHeldUntil = new();
    // Sticky held keys (reliable-release platforms): held until the release edge.
    private static readonly HashSet<KeyCode> hookHeldKeys = new();

    // Volatile mirror of (any hook key currently held), maintained under the
    // lock. Both collections are only ever populated by SkyHook edges for keys
    // Unity's Input can't see, so they stay empty for the common case. This flag
    // lets the viewer/capture fallback skip the lock entirely when no hook-only
    // keys are held.
    private static volatile bool hookActive;

    // Keys whose held state must come from the SkyHook hook because Unity's
    // legacy Input can't be trusted for them:
    //  - RightAlt / RightControl on every platform: the Korean Hangul / Hanja
    //    keys surface here and Input never sees them.
    //  - The other modifiers (Shift / Control / Alt) on macOS/Linux: Input
    //    delivers no down-edge for them and some builds don't report held state
    //    either, so the hook is their only dependable source (this is what let
    //    Left Shift finally be captured into the allow-list). On Windows Input
    //    reports these directly, so they're left un-tracked to avoid needless
    //    hook lock traffic.
    private static bool IsHookOnlyKey(KeyCode key) {
        if(key is KeyCode.RightAlt or KeyCode.RightControl) return true;
        return !IsWindowsRuntime() && key is
            KeyCode.LeftShift or KeyCode.RightShift or KeyCode.LeftControl or KeyCode.LeftAlt;
    }

    public static void NoteHookEvent(KeyCode key, bool pressed) {
        if(!IsHookOnlyKey(key)) return;
        lock(hookHeldUntil) {
            if(pressed) {
                // IsWindowsRuntime() reads Application.platform — already done on
                // this same hook thread by HookKeyToPhysicalUnityKey for every
                // edge, so consulting it here is consistent and safe.
                if(IsWindowsRuntime()) {
                    hookHeldUntil[key] = Environment.TickCount + HookHeldWindowMs;
                } else {
                    hookHeldKeys.Add(key);
                }
            } else {
                hookHeldKeys.Remove(key);
                hookHeldUntil.Remove(key);
            }
            hookActive = hookHeldKeys.Count > 0 || hookHeldUntil.Count > 0;
        }
    }

    public static bool HookKeyHeld(KeyCode key) {
        if(key == KeyCode.None) return false;
        // macOS: read the real physical state for the Unity-blind right modifiers,
        // bypassing the SkyHook edge stream entirely (see MacModifierHeld). The
        // sticky/expiring hook state below can't hold these — the native hook
        // emits a spurious KeyReleased ~1s into a hold, dropping them mid-press.
        bool? mac = MacModifierHeld(key);
        if(mac.HasValue) return mac.Value;
        // Windows: real RightAlt/RightControl read via GetAsyncKeyState. Only a
        // POSITIVE overrides — on a negative we fall through to the hook window
        // below so the Korean Hangul/Hanja keys (same physical position, a
        // different virtual key GetAsyncKeyState can't see as RMENU/RCONTROL)
        // keep their flash. Unity's Input is blind to a held right modifier here
        // too, so without this it fell to the window, which expired ~1s in.
        if(WinModifierDown(key)) return true;
        // Lock-free fast reject for the overwhelmingly common no-hook-keys-held
        // case (volatile read, no lock acquired per un-pressed key per frame).
        if(!hookActive) return false;
        lock(hookHeldUntil) {
            // Sticky press (reliable-release platforms): held until the release.
            if(hookHeldKeys.Contains(key)) return true;
            // Expiring window (Windows Hangul/Hanja). Unchecked (until - now)
            // keeps the right sign across the ~49-day Environment.TickCount wrap.
            return hookHeldUntil.TryGetValue(key, out int until)
                && unchecked(until - Environment.TickCount) > 0;
        }
    }

    // ===== macOS physical key-state poll =====
    //
    // On macOS the SkyHook edge stream can't be trusted for RightAlt/RightControl:
    // Unity's Input is blind to them, AND the native hook emits a spurious
    // KeyReleased about a second into a sustained hold (a key-repeat-delay
    // artifact), so no amount of edge bookkeeping keeps the box lit while the key
    // is physically down. CGEventSourceKeyState reads the window server's current
    // key state directly — no edges, no repeats — so it always reflects the true
    // hold. Called on the main thread only (every HookKeyHeld caller runs there).
    // Wrapped so a missing framework/symbol degrades to the hook state, never a
    // crash. The DllImport is only ever INVOKED on macOS (MacModifierHeld gates on
    // IsMacRuntime first), so the extern is inert on Windows/Linux.
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceKeyState(int stateID, ushort keyCode);

    private const int MacHidStateID = 1; // kCGEventSourceStateHIDSystemState
    private static bool macKeyStateUnavailable;
    private static int isMacRuntime = -1; // -1 unknown, 0 no, 1 yes (main-thread cached)

    private static bool IsMacRuntime() {
        if(isMacRuntime < 0) {
            isMacRuntime = Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor ? 1 : 0;
        }
        return isMacRuntime == 1;
    }

    // macOS virtual keycode for a Unity-blind right modifier, or -1 for any other
    // key — the fast exit, since this runs for every un-pressed viewer box/frame.
    private static int MacModifierVK(KeyCode key) => key switch {
        KeyCode.RightAlt => 0x3D,      // kVK_RightOption
        KeyCode.RightControl => 0x3E,  // kVK_RightControl
        _ => -1,
    };

    // Physical held state if key is a natively-pollable macOS modifier; null if it
    // isn't one, we're not on macOS, or the native call is unavailable — the
    // caller then falls back to the hook-fed state.
    private static bool? MacModifierHeld(KeyCode key) {
        int vk = MacModifierVK(key);
        if(vk < 0 || macKeyStateUnavailable || !IsMacRuntime()) return null;
        try {
            return CGEventSourceKeyState(MacHidStateID, (ushort)vk);
        } catch {
            macKeyStateUnavailable = true; // framework/symbol missing — stop trying
            return null;
        }
    }

    // ===== Windows physical key-state poll =====
    //
    // Same failure as macOS, different OS: Unity's Input doesn't report a held
    // RightAlt/RightControl on Windows, so the viewer fell back to the hook
    // window, which expired about a second into a hold (modifier keys don't
    // auto-repeat, so nothing refreshed it) and dropped the box while the key was
    // still down. GetAsyncKeyState reads the real physical state (high bit = down
    // now). Main-thread only; wrapped so it degrades to the hook state if the
    // call is ever unavailable. The DllImport is only INVOKED on Windows
    // (WinModifierDown gates on IsWindowsRuntime), so it's inert on macOS/Linux.
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool winKeyStateUnavailable;

    // True only when key is a real Windows right modifier that's physically down
    // right now. False otherwise (not that key, not Windows, up, or unavailable)
    // so the caller falls through to the hook window for Hangul/Hanja.
    private static bool WinModifierDown(KeyCode key) {
        int vk = key switch {
            KeyCode.RightAlt => 0xA5,     // VK_RMENU
            KeyCode.RightControl => 0xA3, // VK_RCONTROL
            _ => 0,
        };
        if(vk == 0 || winKeyStateUnavailable || !IsWindowsRuntime()) return false;
        try {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        } catch {
            winKeyStateUnavailable = true; // symbol missing — stop trying
            return false;
        }
    }

    public static KeyCode HookKeyToPhysicalUnityKey(ushort key, KeyLabel label) {
        KeyCode labelKey = SkyHookKeyMapper.SkyHookKeyToUnityKey(label);
        if(IsNumpadOrArrowKey(labelKey)) return labelKey;

        if(IsWindowsRuntime()) {
            KeyCode hookKey = WindowsVirtualKeyToUnityKey(key);
            if(hookKey != KeyCode.None) return hookKey;
        }

        KeyCode mapped = AsyncLabelToPhysicalUnityKey(label);
        if(mapped != KeyCode.None) return mapped;

        return KeyCode.None;
    }

    private static bool IsNumpadOrArrowKey(KeyCode key) => key is
        KeyCode.UpArrow or KeyCode.DownArrow or KeyCode.LeftArrow or KeyCode.RightArrow or
        KeyCode.Keypad0 or KeyCode.Keypad1 or KeyCode.Keypad2 or KeyCode.Keypad3 or KeyCode.Keypad4 or
        KeyCode.Keypad5 or KeyCode.Keypad6 or KeyCode.Keypad7 or KeyCode.Keypad8 or KeyCode.Keypad9 or
        KeyCode.KeypadPeriod or KeyCode.KeypadDivide or KeyCode.KeypadMultiply or KeyCode.KeypadMinus or
        KeyCode.KeypadPlus or KeyCode.KeypadEnter;

    private static bool IsWindowsRuntime() {
        RuntimePlatform platform = Application.platform;
        return platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
    }

    // KeyLabel -> KeyCode is a pure mapping but the resolver below does a
    // label.ToString() (Mono Enum.ToString allocates) plus string parsing on every
    // key edge. This runs on the SkyHook hook thread for every press/release, so on
    // macOS/Linux (where the Windows VK fast path in HookKeyToPhysicalUnityKey is
    // skipped) it churned a short string per edge. Memoize: KeyLabel has a small,
    // fixed member set, so the cache saturates almost immediately. Accessed only
    // from the single SkyHook hook thread (both callers run there, serialized), so
    // a plain Dictionary needs no synchronization.
    private static readonly Dictionary<KeyLabel, KeyCode> asyncLabelCache = new();

    private static KeyCode AsyncLabelToPhysicalUnityKey(KeyLabel label) {
        if(asyncLabelCache.TryGetValue(label, out KeyCode cached)) return cached;
        KeyCode resolved = ResolveAsyncLabelToPhysicalUnityKey(label);
        asyncLabelCache[label] = resolved;
        return resolved;
    }

    private static KeyCode ResolveAsyncLabelToPhysicalUnityKey(KeyLabel label) {
        string name = label.ToString();

        if(name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z')
            return (KeyCode)((int)KeyCode.A + (name[0] - 'A'));

        if(name.Length == 6 && name.StartsWith("Alpha") && name[5] >= '0' && name[5] <= '9')
            return (KeyCode)((int)KeyCode.Alpha0 + (name[5] - '0'));

        if(name.Length >= 2 && name[0] == 'F'
            && int.TryParse(name[1..], out int functionKey) && functionKey >= 1 && functionKey <= 15)
            return (KeyCode)((int)KeyCode.F1 + (functionKey - 1));

        if(name.Length == 7 && name.StartsWith("Keypad") && name[6] >= '0' && name[6] <= '9')
            return (KeyCode)((int)KeyCode.Keypad0 + (name[6] - '0'));

        return name switch {
            "Escape" => KeyCode.Escape,
            "Grave" => KeyCode.BackQuote,
            "Minus" => KeyCode.Minus,
            "Equal" => KeyCode.Equals,
            "Backspace" => KeyCode.Backspace,
            "Tab" => KeyCode.Tab,
            "LeftBrace" => KeyCode.LeftBracket,
            "RightBrace" => KeyCode.RightBracket,
            "BackSlash" => KeyCode.Backslash,
            "CapsLock" => KeyCode.CapsLock,
            "Semicolon" => KeyCode.Semicolon,
            "Apostrophe" => KeyCode.Quote,
            "Enter" => KeyCode.Return,
            "LShift" or "LeftShift" => KeyCode.LeftShift,
            "RShift" or "RightShift" => KeyCode.RightShift,
            "Comma" => KeyCode.Comma,
            "Dot" => KeyCode.Period,
            "Slash" => KeyCode.Slash,
            "LControl" or "LCtrl" or "LeftControl" or "LeftCtrl" => KeyCode.LeftControl,
            "RControl" or "RCtrl" or "RightControl" or "RightCtrl" or "Hanja" => KeyCode.RightControl,
            "Super" => KeyCode.LeftCommand,
            "LWin" or "LeftWin" or "LeftWindows" => KeyCode.LeftWindows,
            "RWin" or "RightWin" or "RightWindows" => KeyCode.RightWindows,
            "LAlt" => KeyCode.LeftAlt,
            "RAlt" or "AltGr" or "Hangul" => KeyCode.RightAlt,
            "Space" => KeyCode.Space,
            "PrintScreen" => KeyCode.Print,
            "ScrollLock" => KeyCode.ScrollLock,
            "PauseBreak" => KeyCode.Pause,
            "Insert" => KeyCode.Insert,
            "Home" => KeyCode.Home,
            "PageUp" => KeyCode.PageUp,
            "Delete" => KeyCode.Delete,
            "End" => KeyCode.End,
            "PageDown" => KeyCode.PageDown,
            "ArrowUp" => KeyCode.UpArrow,
            "ArrowLeft" => KeyCode.LeftArrow,
            "ArrowDown" => KeyCode.DownArrow,
            "ArrowRight" => KeyCode.RightArrow,
            "NumLock" => KeyCode.Numlock,
            "KeypadSlash" => KeyCode.KeypadDivide,
            "KeypadAsterisk" => KeyCode.KeypadMultiply,
            "KeypadMinus" => KeyCode.KeypadMinus,
            "KeypadDot" => KeyCode.KeypadPeriod,
            "KeypadPlus" => KeyCode.KeypadPlus,
            "KeypadEnter" => KeyCode.KeypadEnter,
            "Application" or "Apps" or "Menu" => KeyCode.Menu,
            "MouseLeft" => KeyCode.Mouse0,
            "MouseRight" => KeyCode.Mouse1,
            "MouseMiddle" => KeyCode.Mouse2,
            "MouseX1" => KeyCode.Mouse3,
            "MouseX2" => KeyCode.Mouse4,
            _ => SkyHookKeyMapper.SkyHookKeyToUnityKey(label),
        };
    }

    private static KeyCode WindowsVirtualKeyToUnityKey(ushort key) => key switch {
        0x15 or 0xA5 => KeyCode.RightAlt,
        0x19 or 0xA3 => KeyCode.RightControl,
        0x10 or 0xA0 => KeyCode.LeftShift,
        0x11 or 0xA2 => KeyCode.LeftControl,
        0x12 or 0xA4 => KeyCode.LeftAlt,
        >= 0x30 and <= 0x39 => (KeyCode)((int)KeyCode.Alpha0 + (key - 0x30)),
        >= 0x41 and <= 0x5A => (KeyCode)((int)KeyCode.A + (key - 0x41)),
        >= 0x60 and <= 0x69 => (KeyCode)((int)KeyCode.Keypad0 + (key - 0x60)),
        >= 0x70 and <= 0x7E => (KeyCode)((int)KeyCode.F1 + (key - 0x70)),
        0x5D => KeyCode.Menu,
        0x08 => KeyCode.Backspace,
        0x09 => KeyCode.Tab,
        0x0D => KeyCode.Return,
        0x13 => KeyCode.Pause,
        0x14 => KeyCode.CapsLock,
        0x1B => KeyCode.Escape,
        0x20 => KeyCode.Space,
        0x21 => KeyCode.PageUp,
        0x22 => KeyCode.PageDown,
        0x23 => KeyCode.End,
        0x24 => KeyCode.Home,
        0x25 => KeyCode.LeftArrow,
        0x26 => KeyCode.UpArrow,
        0x27 => KeyCode.RightArrow,
        0x28 => KeyCode.DownArrow,
        0x2C => KeyCode.Print,
        0x2D => KeyCode.Insert,
        0x2E => KeyCode.Delete,
        0x5B => KeyCode.LeftWindows,
        0x5C => KeyCode.RightWindows,
        0x6A => KeyCode.KeypadMultiply,
        0x6B => KeyCode.KeypadPlus,
        0x6D => KeyCode.KeypadMinus,
        0x6E => KeyCode.KeypadPeriod,
        0x6F => KeyCode.KeypadDivide,
        0x90 => KeyCode.Numlock,
        0x91 => KeyCode.ScrollLock,
        0xA1 => KeyCode.RightShift,
        0xBA => KeyCode.Semicolon,
        0xBB => KeyCode.Equals,
        0xBC => KeyCode.Comma,
        0xBD => KeyCode.Minus,
        0xBE => KeyCode.Period,
        0xBF => KeyCode.Slash,
        0xC0 => KeyCode.BackQuote,
        0xDB => KeyCode.LeftBracket,
        0xDC => KeyCode.Backslash,
        0xDD => KeyCode.RightBracket,
        0xDE => KeyCode.Quote,
        _ => KeyCode.None,
    };

    // ===== capture mode =====
    //
    // Single-key capture like v1's KrpPages: the button arms it, the next
    // key pressed is reported and capture ends. Escape (or clicking the
    // button again) cancels.

    public static bool IsCapturing { get; private set; }

    private static Action<KeyCode> captureOnKey;
    private static Action captureOnEnded;

    public static void StartCapture(Action<KeyCode> onKey, Action onEnded) {
        CancelCapture();

        IsCapturing = true;
        captureOnKey = onKey;
        captureOnEnded = onEnded;
        // Suppresses the menu-toggle keybind while listening, same flag the
        // Settings rebind widget uses.
        Keybind.Capturing = true;
        Changed?.Invoke();
    }

    public static void CancelCapture() => EndCapture(KeyCode.None);

    private static void EndCapture(KeyCode key) {
        if(!IsCapturing) return;

        IsCapturing = false;
        Keybind.Capturing = false;

        Action<KeyCode> onKey = captureOnKey;
        Action onEnded = captureOnEnded;
        captureOnKey = null;
        captureOnEnded = null;

        if(key != KeyCode.None && key != KeyCode.Escape) onKey?.Invoke(key);
        onEnded?.Invoke();
        Changed?.Invoke();
    }

    public static void ClearAllowedKeys() {
        EnsureConf();
        Conf.AllowedKeys = [];
        PersistChange();
    }

    // Rebinds one allowed-list entry in place (keeps its position). If the
    // new key is already allowed, the old entry is just removed instead of
    // duplicating.
    public static void ReplaceAllowedKey(KeyCode oldKey, KeyCode newKey) {
        EnsureConf();

        oldKey = NormalizeKey(oldKey);
        newKey = NormalizeKey(newKey);
        if(newKey == KeyCode.None || IsMouseKey(newKey)) return;

        List<int> keys = [.. Conf.AllowedKeys];
        int index = keys.IndexOf((int)oldKey);
        if(index < 0) {
            ToggleAllowedKey(newKey);
            return;
        }

        if(keys.Contains((int)newKey)) {
            keys.RemoveAt(index);
        } else {
            keys[index] = (int)newKey;
        }

        Conf.AllowedKeys = [.. keys];
        PersistChange();
    }

    // ===== per-frame ticker =====

    // Capture consults the hook-held state for exactly the keys the hook tracks
    // (RightAlt/RightControl everywhere, plus the other modifiers on macOS/Linux)
    // so keys Unity's Input can't see — notably Left Shift on macOS — can still
    // be added to the allow-list.
    private static bool IsHookOnlyModifier(KeyCode key) => IsHookOnlyKey(key);

    private static void PersistChange() {
        Save();
        Changed?.Invoke();
    }

    private static Ticker ticker;

    private static void EnsureTicker() {
        if(ticker != null || MainCore.Root == null) return;
        ticker = MainCore.Root.AddComponent<Ticker>();
    }

    // Candidate keys for capture: every keyboard KeyCode (mouse and joystick
    // ranges skipped — mouse is always allowed, joysticks aren't keys).
    private static KeyCode[] captureCandidates;

    private static KeyCode[] CaptureCandidates {
        get {
            if(captureCandidates != null) return captureCandidates;

            List<KeyCode> list = [];
            foreach(KeyCode key in Enum.GetValues(typeof(KeyCode))) {
                if(key == KeyCode.None || IsMouseKey(key) || key >= KeyCode.JoystickButton0) continue;
                list.Add(key);
            }
            captureCandidates = [.. list];
            return captureCandidates;
        }
    }

    // Refreshes the off-thread player-control snapshot every frame (v1 did
    // this from Main.Update) and runs capture-mode key polling. Held-state
    // edge detection instead of GetKeyDown: macOS doesn't deliver down-edges
    // for modifier keys, but held state reads fine.
    private sealed class Ticker : MonoBehaviour {
        private readonly HashSet<KeyCode> prevHeld = [];
        private bool wasCapturing;

        private void Update() {
            InPlayerControl();

            if(!IsCapturing) {
                wasCapturing = false;
                if(prevHeld.Count > 0) prevHeld.Clear();
                return;
            }

            // First frame of a capture: remember what's already held so a
            // key the user hadn't released yet isn't captured instantly.
            bool priming = !wasCapturing;
            wasCapturing = true;

            KeyCode[] candidates = CaptureCandidates;
            for(int i = 0; i < candidates.Length; i++) {
                KeyCode key = candidates[i];
                bool held;
                try { held = UnityEngine.Input.GetKey(key); }
                catch { continue; }

                // Unity's legacy Input is blind to the Korean Hangul/Hanja keys,
                // which surface as RightAlt/RightControl — on a Korean layout the
                // right-Ctrl/Alt position IS the Hanja/Hangul key. Without this the
                // capture loop never sees them, so they can't be added to the
                // allowed list. Fall back to the SkyHook-fed held state (the only
                // path that sees them, still forwarded during capture), mirroring
                // the KeyViewer's KeyHeld. Scoped to those modifiers so normal keys
                // and the NumLock-off numpad keep using Input alone.
                if(!held && IsHookOnlyModifier(key)) held = HookKeyHeld(key);

                if(held && !priming && !prevHeld.Contains(key)) {
                    prevHeld.Add(key);
                    EndCapture(key);
                    return;
                }

                if(held) prevHeld.Add(key);
                else prevHeld.Remove(key);
            }
        }
    }
}
