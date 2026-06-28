using System;
using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;
using UnityEngine;

namespace Quartz.Features.AutoDeafen;

// Persisted config for Auto Deafen, ported from the original
// KorenResourcePack (defaults match v1's Settings.cs). The access token is
// the user's own Discord OAuth token for their own Discord application —
// stored locally like v1 did. Lives in UserData/Quartz/AutoDeafen.json.
public sealed class AutoDeafenSettings : ISettingsFile {
    public bool Enabled = false;
    public float DeafenAtPercent = 5f;
    public bool OnlyFromStart = true;

    // === Mode ===
    // "shortcut" (default) simulates the user's Discord deafen global shortcut
    // by injecting the configured key chord — no Discord app/token needed, but
    // the key injection is Windows-only. "bot" drives Discord's local RPC
    // socket with the user's own OAuth app to set the deaf state directly and
    // works on every platform. AutoDeafen forces "bot" off Windows regardless
    // of this value (see AutoDeafen.EffectiveMode), so macOS users always get
    // the working path even with the default selected.
    public const string ModeShortcut = "shortcut";
    public const string ModeBot = "bot";
    public string Mode = ModeShortcut;
    public bool IsShortcut => string.Equals(Mode, ModeShortcut, StringComparison.OrdinalIgnoreCase);

    // === Shortcut mode (Windows) ===
    // The chord the mod taps to toggle Discord's deafen — match it to the
    // global shortcut set in Discord's keybind settings. Default Ctrl+Shift+D,
    // matching v1.
    public bool ShortcutCtrl = true;
    public bool ShortcutShift = true;
    public bool ShortcutAlt = false;
    public bool ShortcutMeta = false;
    public int ShortcutKey = (int)KeyCode.D;

    // === Bot mode (Discord RPC + OAuth) ===
    public string DiscordClientId = "";
    public string DiscordAccessToken = "";

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(DeafenAtPercent)] = DeafenAtPercent,
            [nameof(OnlyFromStart)] = OnlyFromStart,
            [nameof(Mode)] = Mode,
            [nameof(ShortcutCtrl)] = ShortcutCtrl,
            [nameof(ShortcutShift)] = ShortcutShift,
            [nameof(ShortcutAlt)] = ShortcutAlt,
            [nameof(ShortcutMeta)] = ShortcutMeta,
            [nameof(ShortcutKey)] = ShortcutKey,
            [nameof(DiscordClientId)] = DiscordClientId,
            [nameof(DiscordAccessToken)] = DiscordAccessToken,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        DeafenAtPercent = IOUtils.Read(token, nameof(DeafenAtPercent), DeafenAtPercent);
        OnlyFromStart = IOUtils.Read(token, nameof(OnlyFromStart), OnlyFromStart);
        Mode = IOUtils.Read(token, nameof(Mode), Mode);
        ShortcutCtrl = IOUtils.Read(token, nameof(ShortcutCtrl), ShortcutCtrl);
        ShortcutShift = IOUtils.Read(token, nameof(ShortcutShift), ShortcutShift);
        ShortcutAlt = IOUtils.Read(token, nameof(ShortcutAlt), ShortcutAlt);
        ShortcutMeta = IOUtils.Read(token, nameof(ShortcutMeta), ShortcutMeta);
        ShortcutKey = IOUtils.Read(token, nameof(ShortcutKey), ShortcutKey);
        DiscordClientId = IOUtils.Read(token, nameof(DiscordClientId), DiscordClientId);
        DiscordAccessToken = IOUtils.Read(token, nameof(DiscordAccessToken), DiscordAccessToken);
    }
}
