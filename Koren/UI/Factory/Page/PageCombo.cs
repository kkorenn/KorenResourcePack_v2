using Koren.Core;
using Koren.Features.Combo;
using Koren.Resource;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace Koren.UI.Factory.Page;

// Settings page for the standalone Perfect-Combo HUD. Mirrors the Bismuth
// settings tree: Combo Display > {Label{Shadow}, Count{Shadow}, Animations,
// Color}. Nested collapsibles are just child Collapsibles inside another
// Collapsible's Body.
internal static class PageCombo {
    public static void Create(RectTransform parent) {
        ComboOverlay.EnsureConf();
        ComboSettings conf = ComboOverlay.Conf;
        ComboSettings def = new();

        GameObject pad = new("Pad");
        pad.transform.SetParent(parent, false);

        RectTransform padRect = pad.AddComponent<RectTransform>();
        padRect.anchorMin = Vector2.zero;
        padRect.anchorMax = Vector2.one;
        padRect.pivot = new Vector2(0.5f, 0.5f);
        padRect.offsetMin = new Vector2(18f, 18f);
        padRect.offsetMax = new Vector2(-18f, -18f);

        GameObject viewport = new("Viewport");
        viewport.transform.SetParent(pad.transform, false);

        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0.5f, 0.5f);

        viewport.AddComponent<EmptyGraphic>().raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        GameObject content = new("Content");
        content.transform.SetParent(viewport.transform, false);

        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pad.AddComponent<UIScrollController>().SetContent(contentRect, viewportRect);

        void Save() => ComboOverlay.Save();
        void Apply() => ComboOverlay.Apply();

        // === Combo Display (root section) ===
        var display = GenerateUI.Collapsible(content.transform, "Combo Display", startExpanded: true);

        GenerateUI.Toggle(
            GenerateUI.Row(display.Body),
            def.Enabled, conf.Enabled,
            v => { conf.Enabled = v; Apply(); Save(); },
            "Enabled", "combo_enabled"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(display.Body),
            def.CountAuto, conf.CountAuto,
            v => { conf.CountAuto = v; Save(); },
            "Count Auto Tiles", "combo_countauto"
        );

        AddSlider(display.Body, "Y Offset", "combo_yoffset",
            def.TopOffset, 0f, 600f, conf.TopOffset, "0 px", 1f,
            v => conf.TopOffset = v, Apply, Save);

        AddSlider(display.Body, "Size", "combo_master_size",
            def.MasterSize, 0.25f, 3f, conf.MasterSize, "0.00 x", 0.01f,
            v => conf.MasterSize = v, Apply, Save);

        // === Label (nested) ===
        var labelSec = GenerateUI.Collapsible(display.Body, "Label", startExpanded: false);

        UIInput captionInput = GenerateUI.Input(
            GenerateUI.Row(labelSec.Body),
            def.LabelText, conf.LabelText,
            v => { conf.LabelText = v; Apply(); Save(); },
            "Text", MainCore.Spr.Get(UISprite.Text128),
            "combo_labeltext"
        );
        captionInput.InputField.characterLimit = 32;

        AddSlider(labelSec.Body, "Size", "combo_label_size",
            def.LabelSize, 0.25f, 3f, conf.LabelSize, "0.00 x", 0.01f,
            v => conf.LabelSize = v, Apply, Save);

        AddSlider(labelSec.Body, "Y Offset", "combo_label_yoffset",
            def.LabelYOffset, 0f, 200f, conf.LabelYOffset, "0 px", 1f,
            v => conf.LabelYOffset = v, Apply, Save);

        // Label Shadow (nested in Label)
        var labelShadow = GenerateUI.Collapsible(labelSec.Body, "Shadow", startExpanded: false);

        AddSlider(labelShadow.Body, "X", "combo_label_shadow_x",
            def.LabelShadowX, -10f, 10f, conf.LabelShadowX, "0.0 px", 0.1f,
            v => conf.LabelShadowX = v, Apply, Save);

        AddSlider(labelShadow.Body, "Y", "combo_label_shadow_y",
            def.LabelShadowY, -10f, 10f, conf.LabelShadowY, "0.0 px", 0.1f,
            v => conf.LabelShadowY = v, Apply, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(labelShadow.Body),
            def.GetLabelShadowColor(), conf.GetLabelShadowColor(),
            c => { conf.SetLabelShadowColor(c); Apply(); },
            c => { conf.SetLabelShadowColor(c); Apply(); Save(); },
            "Color", "combo_label_shadow_color"
        );

        // === Count (nested) ===
        var countSec = GenerateUI.Collapsible(display.Body, "Count", startExpanded: false);

        AddSlider(countSec.Body, "Size", "combo_count_size",
            def.CountSize, 0.25f, 3f, conf.CountSize, "0.00 x", 0.01f,
            v => conf.CountSize = v, Apply, Save);

        AddSlider(countSec.Body, "Thickness", "combo_count_thickness",
            def.CountThickness, -0.5f, 0.5f, conf.CountThickness, "0.00", 0.01f,
            v => conf.CountThickness = v, Apply, Save);

        var countShadow = GenerateUI.Collapsible(countSec.Body, "Shadow", startExpanded: false);

        AddSlider(countShadow.Body, "X", "combo_count_shadow_x",
            def.CountShadowX, -10f, 10f, conf.CountShadowX, "0.0 px", 0.1f,
            v => conf.CountShadowX = v, Apply, Save);

        AddSlider(countShadow.Body, "Y", "combo_count_shadow_y",
            def.CountShadowY, -10f, 10f, conf.CountShadowY, "0.0 px", 0.1f,
            v => conf.CountShadowY = v, Apply, Save);

        GenerateUI.ColorPicker(
            GenerateUI.Row(countShadow.Body),
            def.GetCountShadowColor(), conf.GetCountShadowColor(),
            c => { conf.SetCountShadowColor(c); Apply(); },
            c => { conf.SetCountShadowColor(c); Apply(); Save(); },
            "Color", "combo_count_shadow_color"
        );

        // === Animations (nested) ===
        var anim = GenerateUI.Collapsible(display.Body, "Animations", startExpanded: false);

        AddSlider(anim.Body, "Pulse Duration", "combo_pulse_duration",
            def.PulseDuration, 0f, 1f, conf.PulseDuration, "0.00 s", 0.01f,
            v => conf.PulseDuration = v, null, Save);

        AddSlider(anim.Body, "Label Pulse Offset Y", "combo_pulse_label_offset",
            def.LabelPulseOffsetY, 0f, 60f, conf.LabelPulseOffsetY, "0 px", 1f,
            v => conf.LabelPulseOffsetY = v, null, Save);

        AddSlider(anim.Body, "Count Pulse Scale", "combo_pulse_count_scale",
            def.CountPulseScale, 0f, 1f, conf.CountPulseScale, "0.00 x", 0.01f,
            v => conf.CountPulseScale = v, null, Save);

        // === Color (nested) ===
        var color = GenerateUI.Collapsible(display.Body, "Color", startExpanded: false);

        AddSlider(color.Body, "Max Combo", "combo_max",
            def.MaxCombo, 1f, 10000f, conf.MaxCombo, "0", 1f,
            v => conf.MaxCombo = Mathf.RoundToInt(v), null, Save);

        GenerateUI.Toggle(
            GenerateUI.Row(color.Body),
            def.SolidColor, conf.SolidColor,
            v => { conf.SolidColor = v; Save(); },
            "Solid Color", "combo_solidcolor"
        );

        GenerateUI.Toggle(
            GenerateUI.Row(color.Body),
            def.PerfectColorEnabled, conf.PerfectColorEnabled,
            v => { conf.PerfectColorEnabled = v; Save(); },
            "Perfect Color (at 100%)", "combo_perfectcolor_enabled"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(color.Body),
            def.GetStop0(), conf.GetStop0(),
            c => { conf.SetStop0(c); },
            c => { conf.SetStop0(c); Save(); },
            "Stop 0", "combo_stop0"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(color.Body),
            def.GetStop1(), conf.GetStop1(),
            c => { conf.SetStop1(c); },
            c => { conf.SetStop1(c); Save(); },
            "Stop 1", "combo_stop1"
        );

        GenerateUI.ColorPicker(
            GenerateUI.Row(color.Body),
            def.GetPerfectColor(), conf.GetPerfectColor(),
            c => { conf.SetPerfectColor(c); },
            c => { conf.SetPerfectColor(c); Save(); },
            "Perfect Color", "combo_perfectcolor"
        );

        GenerateUI.Button(
            GenerateUI.Row(content.transform),
            () => ComboOverlay.ResetPosition(),
            "Reset Position",
            "combo_resetposition"
        );
    }

    // Shared slider helper: stamps a slider row, formats its readout, snaps
    // values to `step`, and routes both live and complete callbacks.
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
