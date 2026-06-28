using HarmonyLib;
using Quartz.Async;
using Quartz.Core;
using Quartz.Features.Status;
using Quartz.IO;
using MonsterLove.StateMachine;
using UnityEngine;

namespace Quartz.Features.AutoDeafen;

// Auto-deafens Discord once a run passes the configured progress, and
// undeafens on death/finish/leave — ported from the original
// KorenResourcePack. Works through Discord's local RPC socket with the
// user's own OAuth app (client id + token set up from the Gameplay tab).
//
// State machine mirrors v1: a per-frame tick computes the desired deaf state
// from live progress; death/finish latches "stay undeafened" until the next
// run start so the tick can't immediately re-deafen a finished run; "only
// from start" uses the game's checkpointsUsed signal captured on the first
// ticked frame of the run.
public static class AutoDeafen {
    public static SettingsFile<AutoDeafenSettings> ConfMgr { get; private set; }
    public static AutoDeafenSettings Conf => ConfMgr?.Data;

    private static DiscordRpc rpc;
    private static string configClientId;
    private static string configToken;
    private static bool desiredDeaf;
    private static string status = "off";

    private static bool suppressUntilRestart;
    private static bool runStartCaptured;
    private static bool startedFromFirstTile;

    private const string TutorialUrl = "https://www.youtube.com/watch?v=1q4gB0ArypQ";

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<AutoDeafenSettings>(
            Path.Combine(MainCore.Paths.RootPath, "AutoDeafen.json")
        );
        ConfMgr.Load();
        EnsureTicker();
    }

    public static void Save() => ConfMgr?.RequestSave();

    // Shortcut key injection is Windows-only, so everywhere else the bot path is
    // forced regardless of the saved Mode (the Gameplay tab reflects this).
    public static bool ShortcutSupported =>
        Application.platform == RuntimePlatform.WindowsPlayer
        || Application.platform == RuntimePlatform.WindowsEditor;

    public static string EffectiveMode =>
        ShortcutSupported && Conf != null && Conf.IsShortcut
            ? AutoDeafenSettings.ModeShortcut
            : AutoDeafenSettings.ModeBot;

    public static string Status {
        get {
            if(EffectiveMode == AutoDeafenSettings.ModeShortcut) {
                string shortcut = "shortcut " + ChordText();
                return desiredDeaf ? shortcut + " / deaf" : shortcut;
            }

            string rpcStatus = rpc != null ? rpc.Status : status;
            string oauthStatus = DiscordOAuthServer.Status;
            if(!string.IsNullOrEmpty(Trim(Conf?.DiscordAccessToken)) && !DiscordOAuthServer.Running) {
                oauthStatus = "authorized";
            }
            string combined = oauthStatus + " / " + rpcStatus;
            return desiredDeaf ? combined + " / deaf" : combined;
        }
    }

    // Human-readable chord ("Ctrl+Shift+D") for the status readout.
    public static string ChordText() {
        if(Conf == null) {
            return "";
        }
        System.Text.StringBuilder sb = new();
        if(Conf.ShortcutCtrl) { sb.Append("Ctrl+"); }
        if(Conf.ShortcutShift) { sb.Append("Shift+"); }
        if(Conf.ShortcutAlt) { sb.Append("Alt+"); }
        if(Conf.ShortcutMeta) { sb.Append(Keybind.IsMac ? "Cmd+" : "Win+"); }
        sb.Append(Keybind.KeyName((KeyCode)Conf.ShortcutKey));
        return sb.ToString();
    }

    private static void Tick(float progress01) {
        EnsureConf();

        if(!MainCore.IsModEnabled || !Conf.Enabled) {
            Stop();
            status = "off";
            return;
        }

        Conf.DeafenAtPercent = Mathf.Clamp(Conf.DeafenAtPercent, 0f, 100f);

        if(EffectiveMode == AutoDeafenSettings.ModeShortcut) {
            // Shortcut mode needs no Discord app or token — tear down anything
            // left running from bot mode so a runtime switch doesn't leak a
            // socket or listener.
            if(rpc != null) {
                StopRpc();
            }
            if(DiscordOAuthServer.Running) {
                DiscordOAuthServer.Stop();
            }
        } else {
            if(string.IsNullOrWhiteSpace(Conf.DiscordAccessToken)) {
                StopRpc();
                status = "waiting for authorization";
                return;
            }

            // Compare the client-id and token directly instead of building a
            // combined key string every tick (the concat was a per-frame
            // allocation just to detect a settings edit). Trim() returns the same
            // instance when there's nothing to trim, so this normally allocates
            // nothing.
            string clientId = DiscordOAuthServer.ClientId;
            string token = Trim(Conf.DiscordAccessToken);
            if(rpc == null
               || !string.Equals(configClientId, clientId, StringComparison.Ordinal)
               || !string.Equals(configToken, token, StringComparison.Ordinal)) {
                Restart(clientId, token);
            }
        }

        // Capture the game's authoritative first-tile signal once per run —
        // sampling progress<=0 was fragile on long levels.
        if(progress01 >= 0f && !runStartCaptured) {
            startedFromFirstTile = ProgressTracker.IsFirstTileRunStart();
            runStartCaptured = true;
        }

        bool eligibleStart = !Conf.OnlyFromStart || (runStartCaptured && startedFromFirstTile);
        bool shouldDeaf = !suppressUntilRestart
            && progress01 >= 0f
            && InRealPlay()
            && eligibleStart
            && Mathf.Clamp01(progress01) * 100f >= Conf.DeafenAtPercent;

        if(shouldDeaf != desiredDeaf) {
            desiredDeaf = shouldDeaf;
            ApplyDeaf(shouldDeaf);
            MainCore.Log.Msg("[AutoDeafen] desired deaf = " + shouldDeaf);
        }
    }

    // Drive the deaf state through whichever backend the effective mode uses.
    // Bot mode sets the state absolutely over RPC; shortcut mode taps the
    // configured chord, which Discord treats as a toggle — so the same chord is
    // sent for deaf and undeaf, and desiredDeaf tracking keeps them paired.
    private static void ApplyDeaf(bool deaf) {
        if(EffectiveMode == AutoDeafenSettings.ModeShortcut) {
            NativeKeySender.SendChord(
                Conf.ShortcutCtrl, Conf.ShortcutShift, Conf.ShortcutAlt, Conf.ShortcutMeta,
                (KeyCode)Conf.ShortcutKey
            );
        } else {
            rpc?.SetDeaf(deaf);
        }
    }

    // A run (re)started — allow deafening again and re-measure where it began.
    private static void OnRunReset() {
        suppressUntilRestart = false;
        runStartCaptured = false;
        Undeafen();
    }

    // Run ended (death / finish) — undeafen and stay undeafened until the
    // next run start, so the per-frame tick can't immediately re-deafen.
    private static void OnRunEnded() {
        suppressUntilRestart = true;
        Undeafen();
    }

    // Run left the screen (scene change) — undeafen without touching the
    // latch or the captured start.
    private static void OnRunHide() {
        Undeafen();
    }

    private static void Undeafen() {
        if(!desiredDeaf) {
            return;
        }
        desiredDeaf = false;
        try { ApplyDeaf(false); } catch { }
    }

    public static void Stop() {
        // Release any active deafen first (while rpc is still live for bot mode,
        // or by tapping the toggle once for shortcut mode) before tearing down.
        Undeafen();
        if(rpc != null) {
            StopRpc();
        }
        DiscordOAuthServer.Stop();
        desiredDeaf = false;
        configClientId = null;
        configToken = null;
        suppressUntilRestart = false;
        runStartCaptured = false;
    }

    public static void OpenAuthorizeUrl() {
        EnsureConf();
        DiscordOAuthServer.OpenAuthorizeUrl();
    }

    public static void OpenTutorial() => DiscordOAuthServer.OpenUrl(TutorialUrl);

    public static string AuthorizeUrl() {
        EnsureConf();
        return DiscordOAuthServer.AuthorizeUrl();
    }

    public static void Unlink() {
        Stop();
        EnsureConf();
        Conf.DiscordAccessToken = "";
        ConfMgr.Save();
        status = "unlinked";
    }

    private static bool InRealPlay() {
        try { return scnGame.instance != null; }
        catch { return false; }
    }

    private static void Restart(string clientId, string token) {
        StopRpc();
        configClientId = clientId;
        configToken = token;
        status = "starting";
        rpc = new DiscordRpc(clientId, token);
        rpc.Start();
    }

    private static void StopRpc() {
        if(rpc == null) {
            return;
        }
        try { rpc.SetDeaf(false); } catch { }
        try { rpc.Stop(); } catch { }
        rpc = null;
        desiredDeaf = false;
        configClientId = null;
        configToken = null;
    }

    internal static void SaveAccessToken(string token) {
        if(string.IsNullOrEmpty(token) || Conf == null) {
            return;
        }

        // OAuth callback runs on its listener thread. Keep config mutation and
        // serialization on Unity's main thread like every settings UI path.
        MainThread.Enqueue(() => {
            if(Conf == null || string.Equals(Conf.DiscordAccessToken, token, StringComparison.Ordinal)) {
                return;
            }
            Conf.DiscordAccessToken = token;
            try { ConfMgr.Save(); } catch { }
        });
    }

    private static string Trim(string value) => (value ?? "").Trim();

    // ===== per-frame ticker =====

    private static Ticker ticker;

    private static void EnsureTicker() {
        if(ticker != null || MainCore.Root == null) {
            return;
        }
        ticker = MainCore.Root.AddComponent<Ticker>();
    }

    private sealed class Ticker : MonoBehaviour {
        private void Update() {
            float progress = GameStats.InGame ? Mathf.Clamp01(GameStats.Progress) : -1f;
            Tick(progress);
        }
    }

    // ===== run-state patches =====

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class RunStartPatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled) {
                OnRunReset();
            }
        }
    }

    [HarmonyPatch(typeof(StateBehaviour), "ChangeState", new[] { typeof(Enum) })]
    private static class RunEndPatch {
        private static void Postfix(Enum newState) {
            if(!MainCore.IsModEnabled || newState is not States state) {
                return;
            }

            if(state == States.Fail2 || state == States.Won) {
                OnRunEnded();
            }
        }
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class RunHidePatch {
        private static void Postfix() {
            if(MainCore.IsModEnabled) {
                OnRunHide();
            }
        }
    }
}
