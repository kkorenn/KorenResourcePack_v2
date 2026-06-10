using Koren.Features.Judgement;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using UnityEngine;

namespace Koren.UI.Factory.Page;

// Judgement counts section for the Overlay tab — the original
// KorenResourcePack judgement overlay (top-center row of colored per-
// judgement hit counts). Slider ranges match v1.
internal static class PageJudgement {
    public static void AppendTo(Transform content) {
        JudgementOverlay.EnsureConf();
        JudgementSettings conf = JudgementOverlay.Conf;
        JudgementSettings def = new();

        void Save() => JudgementOverlay.Save();
        void Apply() => JudgementOverlay.Apply();

        var sec = GenerateUI.Collapsible(content, "Judgement", startExpanded: false);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; Apply(); Save(); },
            "Enable Judgement Counts",
            "judgement_enabled"
        ).Rect.AddToolTip(
            "DESC_JUDGEMENT_ENABLED",
            "Top-center row of hit counts per judgement, from Overload through Perfect to Miss."
        );

        AddSlider(sec.Body, "Vertical Offset", "judgement_offsety",
            def.OffsetY, -100f, 200f, conf.OffsetY, "0 px", 1f,
            v => conf.OffsetY = v, Apply, Save);

        AddSlider(sec.Body, "Size", "judgement_size",
            def.Size, 0.3f, 3f, conf.Size, "0.00 x", 0.01f,
            v => conf.Size = v, Apply, Save);

        AddSlider(sec.Body, "Spacing", "judgement_spacing",
            def.Spacing, -20f, 80f, conf.Spacing, "0 px", 1f,
            v => conf.Spacing = v, Apply, Save);
    }

    private static void AddSlider(
        Transform body, string label, string id,
        float defVal, float min, float max, float val,
        string format, float step,
        System.Action<float> setter,
        System.Action live, System.Action save
    ) {
        float Snap(float v) {
            float snapped = Mathf.Round(v / step) * step;
            return Mathf.Clamp(snapped, min, max);
        }

        UISlider s = GenerateUI.Slider(
            GenerateUI.Row(body),
            defVal, min, max, val,
            Snap, null, null,
            label, id
        );
        s.Format = format;
        s.OnChanged = v => { setter(v); live?.Invoke(); };
        s.OnComplete = v => { setter(v); live?.Invoke(); save?.Invoke(); };
    }
}
