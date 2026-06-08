using HarmonyLib;
using Koren.Core;
using UnityEngine;

namespace Koren.Features.Combo;

// Tracks the live Perfect-Combo count and the pulse animation timer. The
// Harmony patches here are scanned automatically by HarmonyService.PatchAll.
//
// State is purely in-memory — combo doesn't survive across game launches.
// Reset on every level entry/exit so each play starts from 0.
internal static class Combo {
    internal static int Count;
    internal static float PulseStartTime = -1f;

    internal static void Reset() {
        Count = 0;
        PulseStartTime = -1f;
    }

    // Single-curve pulse modeled on Bismuth's settings. Phase = elapsed /
    // duration, clamped to [0, 1]. Pulse intensity follows sin(π·phase) so it
    // smoothly rises from 0 to 1 (peak) at phase 0.5 and falls back to 0.
    // Returns (countScaleMultiplier, labelKickPixels).
    public static (float countScale, float labelKickY) EvaluatePulse(
        float duration,
        float countScaleDelta,
        float labelOffsetY
    ) {
        if(PulseStartTime < 0f || duration <= 0f) {
            return (1f, 0f);
        }

        float elapsed = Time.realtimeSinceStartup - PulseStartTime;
        if(elapsed >= duration) {
            PulseStartTime = -1f;
            return (1f, 0f);
        }

        float phase = elapsed / duration;
        float pulse = Mathf.Sin(phase * Mathf.PI);
        return (1f + countScaleDelta * pulse, labelOffsetY * pulse);
    }

    [HarmonyPatch(typeof(scnGame), "Play")]
    private static class ResetOnRunStartPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Reset();
        }
    }

    [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
    private static class ResetOnRunExitPatch {
        private static void Postfix() {
            if(!MainCore.IsModEnabled) {
                return;
            }
            Reset();
        }
    }

    [HarmonyPatch(typeof(scrMarginTracker), "AddHit", typeof(HitMargin))]
    private static class AddHitPatch {
        private static void Postfix(HitMargin hit) {
            if(!MainCore.IsModEnabled) {
                return;
            }

            // ComboOverlay owns the CountAuto setting. If its config hasn't
            // loaded yet (early boot edge case), default to counting Auto.
            bool countAuto = ComboOverlay.ConfMgr == null || ComboOverlay.Conf.CountAuto;

            bool incPerfect = hit == HitMargin.Perfect;
            bool incAuto = countAuto && hit == HitMargin.Auto;

            if(incPerfect || incAuto) {
                Count++;
                PulseStartTime = Time.realtimeSinceStartup;
            } else if(countAuto || hit != HitMargin.Auto) {
                Count = 0;
                PulseStartTime = -1f;
            }
        }
    }
}
