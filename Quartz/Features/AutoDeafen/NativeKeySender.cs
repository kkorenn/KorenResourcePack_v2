using System;
using System.Runtime.InteropServices;
using Quartz.Core;
using UnityEngine;
// KeyLimiter is both a namespace and the class inside it; alias the class so the
// NormalizeKey call below doesn't bind to the namespace.
using KeyLimiterFeature = Quartz.Features.KeyLimiter.KeyLimiter;

namespace Quartz.Features.AutoDeafen;

// Injects a system-wide key chord (modifiers + key) so Discord picks it up via
// its own global "toggle deafen" shortcut. One SendChord call is a single tap
// (press + release of the whole chord), which flips Discord's deafen state.
//
// Ported from the original KorenResourcePack, trimmed to the Windows backend:
// shortcut mode is offered on Windows only (AutoDeafen forces bot mode
// elsewhere), so the macOS/Linux injectors v1 carried would never run here.
// Any non-Windows call no-ops with a single log line.
internal static class NativeKeySender {
    private static bool unsupportedLogged;

    internal static void SendChord(bool ctrl, bool shift, bool alt, bool meta, KeyCode key) {
        key = KeyLimiterFeature.NormalizeKey(key);
        if(key == KeyCode.None) {
            return;
        }

        try {
            switch(Application.platform) {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    SendWindows(ctrl, shift, alt, meta, key);
                    break;
                default:
                    LogUnsupported("platform " + Application.platform);
                    break;
            }
        } catch(Exception ex) {
            LogUnsupported(ex.Message);
        }
    }

    private static void LogUnsupported(string detail) {
        if(unsupportedLogged) {
            return;
        }
        unsupportedLogged = true;
        MainCore.Log.Msg("[AutoDeafen] key send unavailable (" + detail + ").");
    }

    // ===== Windows (user32) =====

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12;   // Alt
    private const byte VK_LWIN = 0x5B;

    private static void SendWindows(bool ctrl, bool shift, bool alt, bool meta, KeyCode key) {
        byte vk = WinVk(key);
        if(vk == 0) {
            LogUnsupported("no VK for " + key);
            return;
        }

        if(ctrl) { keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); }
        if(shift) { keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero); }
        if(alt) { keybd_event(VK_MENU, 0, 0, UIntPtr.Zero); }
        if(meta) { keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero); }

        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        if(meta) { keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
        if(alt) { keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
        if(shift) { keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
        if(ctrl) { keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
    }

    private static byte WinVk(KeyCode k) {
        if(k >= KeyCode.A && k <= KeyCode.Z) { return (byte)(0x41 + (k - KeyCode.A)); }
        if(k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) { return (byte)(0x30 + (k - KeyCode.Alpha0)); }
        if(k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) { return (byte)(0x60 + (k - KeyCode.Keypad0)); }
        if(k >= KeyCode.F1 && k <= KeyCode.F15) { return (byte)(0x70 + (k - KeyCode.F1)); }
        return k switch {
            KeyCode.Space => 0x20,
            KeyCode.Return => 0x0D,
            KeyCode.Tab => 0x09,
            KeyCode.Backspace => 0x08,
            KeyCode.Escape => 0x1B,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.Home => 0x24,
            KeyCode.End => 0x23,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.UpArrow => 0x26,
            KeyCode.DownArrow => 0x28,
            KeyCode.LeftArrow => 0x25,
            KeyCode.RightArrow => 0x27,
            KeyCode.BackQuote => 0xC0,
            KeyCode.Minus => 0xBD,
            KeyCode.Equals => 0xBB,
            KeyCode.LeftBracket => 0xDB,
            KeyCode.RightBracket => 0xDD,
            KeyCode.Semicolon => 0xBA,
            KeyCode.Quote => 0xDE,
            KeyCode.Comma => 0xBC,
            KeyCode.Period => 0xBE,
            KeyCode.Slash => 0xBF,
            KeyCode.Backslash => 0xDC,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.KeypadPeriod => 0x6E,
            _ => 0,
        };
    }
}
