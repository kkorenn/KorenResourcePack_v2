using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;
using UnityEngine;

namespace Koren.Features.Combo;

// Persisted config for the standalone Perfect-Combo HUD. Modeled on the
// settings tree from sbrothers7/Bismuth:
//
//   Combo Display
//     Y Offset, Size, Count Auto Tiles
//     Label
//       Text, Size, Y Offset
//       Shadow: X, Y, Color
//     Count
//       Size
//       Shadow: X, Y, Color
//     Animations
//       Pulse Duration, Label Pulse Offset Y, Count Pulse Scale
//     Color
//       Max Combo, Solid Color toggle, Perfect Color toggle,
//       Stop 0 / Stop 1 (linear gradient endpoints)
public sealed class ComboSettings : ISettingsFile {
    public bool Enabled = true;

    // === Display ===
    public bool CountAuto = false;
    public float TopOffset = 130f;
    public float OffsetX = 0f;
    public float MasterSize = 1.0f;

    // === Label ===
    public string LabelText = "Perfect Combo";
    public float LabelSize = 1.0f;
    public float LabelYOffset = 65f;
    public float LabelShadowX = 2.5f;
    public float LabelShadowY = -2.5f;
    public float LabelShadowR = 0f, LabelShadowG = 0f, LabelShadowB = 0f, LabelShadowA = 1f;

    // === Count ===
    public float CountSize = 1.0f;
    // TMP face dilate — makes the value strokes thicker. Range roughly
    // -0.5 (thinner) to 0.5 (much thicker); 0 = native font weight.
    public float CountThickness = 0f;
    public float CountShadowX = 4.0f;
    public float CountShadowY = -4.0f;
    public float CountShadowR = 0f, CountShadowG = 0f, CountShadowB = 0f, CountShadowA = 1f;

    // === Animations ===
    public float PulseDuration = 0.15f;
    public float LabelPulseOffsetY = 10f;
    public float CountPulseScale = 0.25f;

    // === Color ===
    public int MaxCombo = 1000;
    public bool SolidColor = false;
    public bool PerfectColorEnabled = false;
    // Defaults port the original KRP combo palette: low = white, high = soft
    // red-pink. Stop 0 is also used as the Solid Color when SolidColor=true.
    public float Stop0R = 1.00f, Stop0G = 1.00f, Stop0B = 1.00f, Stop0A = 1f;
    public float Stop1R = 1.00f, Stop1G = 0.22f, Stop1B = 0.22f, Stop1A = 1f;
    // Used at count == MaxCombo when PerfectColorEnabled. Matches the soft-
    // red accent UIColors.SoftRed.
    public float PerfectR = 0.886f, PerfectG = 0.404f, PerfectB = 0.427f, PerfectA = 1f;

    public Color GetLabelShadowColor() => new(
        Mathf.Clamp01(LabelShadowR), Mathf.Clamp01(LabelShadowG),
        Mathf.Clamp01(LabelShadowB), Mathf.Clamp01(LabelShadowA));

    public void SetLabelShadowColor(Color c) {
        LabelShadowR = Mathf.Clamp01(c.r); LabelShadowG = Mathf.Clamp01(c.g);
        LabelShadowB = Mathf.Clamp01(c.b); LabelShadowA = Mathf.Clamp01(c.a);
    }

    public Color GetCountShadowColor() => new(
        Mathf.Clamp01(CountShadowR), Mathf.Clamp01(CountShadowG),
        Mathf.Clamp01(CountShadowB), Mathf.Clamp01(CountShadowA));

    public void SetCountShadowColor(Color c) {
        CountShadowR = Mathf.Clamp01(c.r); CountShadowG = Mathf.Clamp01(c.g);
        CountShadowB = Mathf.Clamp01(c.b); CountShadowA = Mathf.Clamp01(c.a);
    }

    public Color GetStop0() => new(
        Mathf.Clamp01(Stop0R), Mathf.Clamp01(Stop0G),
        Mathf.Clamp01(Stop0B), Mathf.Clamp01(Stop0A));

    public void SetStop0(Color c) {
        Stop0R = Mathf.Clamp01(c.r); Stop0G = Mathf.Clamp01(c.g);
        Stop0B = Mathf.Clamp01(c.b); Stop0A = Mathf.Clamp01(c.a);
    }

    public Color GetStop1() => new(
        Mathf.Clamp01(Stop1R), Mathf.Clamp01(Stop1G),
        Mathf.Clamp01(Stop1B), Mathf.Clamp01(Stop1A));

    public void SetStop1(Color c) {
        Stop1R = Mathf.Clamp01(c.r); Stop1G = Mathf.Clamp01(c.g);
        Stop1B = Mathf.Clamp01(c.b); Stop1A = Mathf.Clamp01(c.a);
    }

    public Color GetPerfectColor() => new(
        Mathf.Clamp01(PerfectR), Mathf.Clamp01(PerfectG),
        Mathf.Clamp01(PerfectB), Mathf.Clamp01(PerfectA));

    public void SetPerfectColor(Color c) {
        PerfectR = Mathf.Clamp01(c.r); PerfectG = Mathf.Clamp01(c.g);
        PerfectB = Mathf.Clamp01(c.b); PerfectA = Mathf.Clamp01(c.a);
    }

    // Linear interpolation between Stop0 and Stop1 by count/MaxCombo.
    // SolidColor → Stop0 only. PerfectColor (when enabled and ratio >= 1) → override.
    public Color ResolveValueColor(int count) {
        if(PerfectColorEnabled && MaxCombo > 0 && count >= MaxCombo) {
            return GetPerfectColor();
        }
        if(SolidColor) {
            return GetStop0();
        }
        float t = MaxCombo > 0 ? Mathf.Clamp01((float)count / MaxCombo) : 0f;
        return Color.Lerp(GetStop0(), GetStop1(), t);
    }

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(CountAuto)] = CountAuto,
            [nameof(TopOffset)] = TopOffset,
            [nameof(OffsetX)] = OffsetX,
            [nameof(MasterSize)] = MasterSize,
            [nameof(LabelText)] = LabelText,
            [nameof(LabelSize)] = LabelSize,
            [nameof(LabelYOffset)] = LabelYOffset,
            [nameof(LabelShadowX)] = LabelShadowX,
            [nameof(LabelShadowY)] = LabelShadowY,
            [nameof(LabelShadowR)] = LabelShadowR,
            [nameof(LabelShadowG)] = LabelShadowG,
            [nameof(LabelShadowB)] = LabelShadowB,
            [nameof(LabelShadowA)] = LabelShadowA,
            [nameof(CountSize)] = CountSize,
            [nameof(CountThickness)] = CountThickness,
            [nameof(CountShadowX)] = CountShadowX,
            [nameof(CountShadowY)] = CountShadowY,
            [nameof(CountShadowR)] = CountShadowR,
            [nameof(CountShadowG)] = CountShadowG,
            [nameof(CountShadowB)] = CountShadowB,
            [nameof(CountShadowA)] = CountShadowA,
            [nameof(PulseDuration)] = PulseDuration,
            [nameof(LabelPulseOffsetY)] = LabelPulseOffsetY,
            [nameof(CountPulseScale)] = CountPulseScale,
            [nameof(MaxCombo)] = MaxCombo,
            [nameof(SolidColor)] = SolidColor,
            [nameof(PerfectColorEnabled)] = PerfectColorEnabled,
            [nameof(Stop0R)] = Stop0R, [nameof(Stop0G)] = Stop0G,
            [nameof(Stop0B)] = Stop0B, [nameof(Stop0A)] = Stop0A,
            [nameof(Stop1R)] = Stop1R, [nameof(Stop1G)] = Stop1G,
            [nameof(Stop1B)] = Stop1B, [nameof(Stop1A)] = Stop1A,
            [nameof(PerfectR)] = PerfectR, [nameof(PerfectG)] = PerfectG,
            [nameof(PerfectB)] = PerfectB, [nameof(PerfectA)] = PerfectA,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        CountAuto = IOUtils.Read(token, nameof(CountAuto), CountAuto);
        TopOffset = IOUtils.Read(token, nameof(TopOffset), TopOffset);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        MasterSize = IOUtils.Read(token, nameof(MasterSize), MasterSize);
        LabelText = IOUtils.Read(token, nameof(LabelText), LabelText);
        LabelSize = IOUtils.Read(token, nameof(LabelSize), LabelSize);
        LabelYOffset = IOUtils.Read(token, nameof(LabelYOffset), LabelYOffset);
        LabelShadowX = IOUtils.Read(token, nameof(LabelShadowX), LabelShadowX);
        LabelShadowY = IOUtils.Read(token, nameof(LabelShadowY), LabelShadowY);
        LabelShadowR = IOUtils.Read(token, nameof(LabelShadowR), LabelShadowR);
        LabelShadowG = IOUtils.Read(token, nameof(LabelShadowG), LabelShadowG);
        LabelShadowB = IOUtils.Read(token, nameof(LabelShadowB), LabelShadowB);
        LabelShadowA = IOUtils.Read(token, nameof(LabelShadowA), LabelShadowA);
        CountSize = IOUtils.Read(token, nameof(CountSize), CountSize);
        CountThickness = IOUtils.Read(token, nameof(CountThickness), CountThickness);
        CountShadowX = IOUtils.Read(token, nameof(CountShadowX), CountShadowX);
        CountShadowY = IOUtils.Read(token, nameof(CountShadowY), CountShadowY);
        CountShadowR = IOUtils.Read(token, nameof(CountShadowR), CountShadowR);
        CountShadowG = IOUtils.Read(token, nameof(CountShadowG), CountShadowG);
        CountShadowB = IOUtils.Read(token, nameof(CountShadowB), CountShadowB);
        CountShadowA = IOUtils.Read(token, nameof(CountShadowA), CountShadowA);
        PulseDuration = IOUtils.Read(token, nameof(PulseDuration), PulseDuration);
        LabelPulseOffsetY = IOUtils.Read(token, nameof(LabelPulseOffsetY), LabelPulseOffsetY);
        CountPulseScale = IOUtils.Read(token, nameof(CountPulseScale), CountPulseScale);
        MaxCombo = IOUtils.Read(token, nameof(MaxCombo), MaxCombo);
        SolidColor = IOUtils.Read(token, nameof(SolidColor), SolidColor);
        PerfectColorEnabled = IOUtils.Read(token, nameof(PerfectColorEnabled), PerfectColorEnabled);
        Stop0R = IOUtils.Read(token, nameof(Stop0R), Stop0R);
        Stop0G = IOUtils.Read(token, nameof(Stop0G), Stop0G);
        Stop0B = IOUtils.Read(token, nameof(Stop0B), Stop0B);
        Stop0A = IOUtils.Read(token, nameof(Stop0A), Stop0A);
        Stop1R = IOUtils.Read(token, nameof(Stop1R), Stop1R);
        Stop1G = IOUtils.Read(token, nameof(Stop1G), Stop1G);
        Stop1B = IOUtils.Read(token, nameof(Stop1B), Stop1B);
        Stop1A = IOUtils.Read(token, nameof(Stop1A), Stop1A);
        PerfectR = IOUtils.Read(token, nameof(PerfectR), PerfectR);
        PerfectG = IOUtils.Read(token, nameof(PerfectG), PerfectG);
        PerfectB = IOUtils.Read(token, nameof(PerfectB), PerfectB);
        PerfectA = IOUtils.Read(token, nameof(PerfectA), PerfectA);
    }
}
