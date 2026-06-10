using HarmonyLib;
using Koren.Core;
using Koren.IO;
using UnityEngine;

namespace Koren.Features.Judgement;

// Hides selected judgement popups (the "Perfect!"-style text that pops at the
// planet on every hit), ported from v1's Tweaks.HideJudgementPopups. Which
// judgements are hidden is a per-HitMargin bitmask. The prefix moves the
// popup offscreen before Show positions it; the postfix then kills the
// instance so the pooled copy can't flash back at its old position.
public static class JudgementPopupHider {
    public static SettingsFile<JudgementPopupHiderSettings> ConfMgr { get; private set; }
    public static JudgementPopupHiderSettings Conf => ConfMgr?.Data;

    // Mask bits follow HitMargin order; 12 covers every vanilla judgement.
    public const int JudgementCount = 12;

    private static readonly Vector3 HiddenPosition = new(123456f, 123456f, 123456f);

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<JudgementPopupHiderSettings>(
            Path.Combine(MainCore.Paths.RootPath, "JudgementPopupHider.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();

    private static bool Enabled {
        get {
            EnsureConf();
            return MainCore.IsModEnabled && Conf.Enabled;
        }
    }

    private static bool ShouldHide(scrHitTextMesh hitText) {
        if(!Enabled || hitText == null) {
            return false;
        }

        int bit = (int)hitText.hitMargin;
        return bit >= 0 && bit < JudgementCount && (Conf.HiddenMask & (1 << bit)) != 0;
    }

    [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
    private static class HitTextShowPatch {
        private static void Prefix(scrHitTextMesh __instance, ref Vector3 position, ref Vector3 borderOffset, ref float scale) {
            if(!ShouldHide(__instance)) {
                return;
            }

            position = HiddenPosition;
            borderOffset = Vector3.zero;
            scale = 0f;
        }

        private static void Postfix(scrHitTextMesh __instance) {
            if(!ShouldHide(__instance)) {
                return;
            }

            __instance.dead = true;
            if(__instance.text != null) {
                __instance.text.text = "";
            }
            __instance.transform.localPosition = HiddenPosition;
            __instance.transform.localScale = Vector3.zero;
            __instance.gameObject.SetActive(false);
        }
    }
}
