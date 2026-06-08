using System.Globalization;
using Koren.Core;
using Koren.Features.Status;
using Koren.IO;
using Koren.Resource;
using Koren.UI;
using Koren.UI.Utility;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.Features.Combo;

// Standalone Perfect-Combo HUD. Top-center panel with a small caption above
// and a pulse-animated value below. Visual model matches Bismuth's combo
// display: separate Label / Count blocks, each with its own size and TMP
// underlay shadow; value color resolved per-frame from a 2-stop gradient
// driven by Combo.Count / MaxCombo (with a Solid Color override and an
// optional "Perfect Color" at 100%).
public static class ComboOverlay {
    public static SettingsFile<ComboSettings> ConfMgr { get; private set; }
    public static ComboSettings Conf => ConfMgr.Data;

    // Internal font bases. Bismuth-style settings expose multipliers (1.00x)
    // rather than absolute pixel sizes; final pixel size = base × master × per-
    // element size.
    private const float CaptionBaseSize = 30f;
    private const float ValueBaseSize = 96f;

    // Maps the user's "pixel-ish" shadow offsets to TMP underlay units (em-
    // relative). Bismuth's UI lets the user think in pixels; ~0.01 underlay ≈
    // 1px on a ~100px font, which is close enough to match the visual.
    private const float ShadowToUnderlay = 0.01f;

    private static GameObject canvasObj;
    private static RectTransform panel;
    private static TextMeshProUGUI caption;
    private static RectTransform captionRect;
    private static TextMeshProUGUI value;
    private static RectTransform valueRect;
    private static GameObject dragObj;
    private static Updater updater;

    // Cached "rest" y for the caption — Apply() writes it; the per-frame
    // pulse adds `labelKick` on top, then re-reads this so it doesn't drift.
    private static float captionRestY;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<ComboSettings>(
            Path.Combine(MainCore.Paths.RootPath, "Combo.json")
        );
        ConfMgr.Load();
    }

    public static void Initialize(GameObject root) {
        if(canvasObj != null) {
            return;
        }

        EnsureConf();

        canvasObj = new GameObject("KorenComboCanvas");
        canvasObj.transform.SetParent(root.transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32757;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject panelObj = new("ComboPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        panel = panelObj.AddComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.sizeDelta = new Vector2(600f, 240f);

        // Value — centered at panel's anchor area; pivot center so the scale
        // pulse grows symmetrically.
        GameObject valueObj = new("Value");
        valueObj.transform.SetParent(panel, false);
        valueRect = valueObj.AddComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0.5f, 1f);
        valueRect.anchorMax = new Vector2(0.5f, 1f);
        valueRect.pivot = new Vector2(0.5f, 0.5f);
        valueRect.sizeDelta = new Vector2(500f, 160f);

        value = valueObj.AddComponent<TextMeshProUGUI>();
        value.font = FontManager.Current;
        value.alignment = TextAlignmentOptions.Center;
        value.fontStyle = FontStyles.Bold;
        value.raycastTarget = false;

        // Caption — pivot bottom-center so LabelYOffset reads as "px above
        // the value's center".
        GameObject captionObj = new("Caption");
        captionObj.transform.SetParent(panel, false);
        captionRect = captionObj.AddComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0.5f, 1f);
        captionRect.anchorMax = new Vector2(0.5f, 1f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.sizeDelta = new Vector2(500f, 48f);

        caption = captionObj.AddComponent<TextMeshProUGUI>();
        caption.font = FontManager.Current;
        caption.alignment = TextAlignmentOptions.Bottom;
        caption.fontStyle = FontStyles.Bold;
        caption.raycastTarget = false;

        // Drag overlay.
        GameObject drag = new("Drag");
        dragObj = drag;
        drag.transform.SetParent(panel, false);
        RectTransform dragRect = drag.AddComponent<RectTransform>();
        dragRect.anchorMin = Vector2.zero;
        dragRect.anchorMax = Vector2.one;
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        drag.AddComponent<EmptyGraphic>().raycastTarget = true;
        drag.AddComponent<DragHandler>();
        drag.SetActive(false);

        updater = canvasObj.AddComponent<Updater>();

        Apply();
    }

    public static void Apply() {
        if(panel == null) {
            return;
        }

        panel.anchoredPosition = new Vector2(Conf.OffsetX, -Conf.TopOffset);
        panel.localScale = Vector3.one * Mathf.Max(0.01f, Conf.MasterSize);

        if(caption != null && captionRect != null) {
            caption.text = Conf.LabelText;
            caption.fontSize = CaptionBaseSize * Mathf.Max(0.01f, Conf.LabelSize);
            captionRestY = Conf.LabelYOffset;
            captionRect.anchoredPosition = new Vector2(0f, -captionRestY);
            ApplyShadow(caption, Conf.LabelShadowX, Conf.LabelShadowY, Conf.GetLabelShadowColor());
        }

        if(value != null && valueRect != null) {
            value.fontSize = ValueBaseSize * Mathf.Max(0.01f, Conf.CountSize);
            // Fixed reference position for the value within the panel; the
            // caption is offset relative to *this*, not to the panel.
            valueRect.anchoredPosition = new Vector2(0f, -ValueRestY);
            ApplyShadow(value, Conf.CountShadowX, Conf.CountShadowY, Conf.GetCountShadowColor());
            ApplyThickness(value, Conf.CountThickness);
        }

        if(captionRect != null) {
            // Caption sits above the value's center by LabelYOffset px.
            captionRestY = ValueRestY - Conf.LabelYOffset;
            captionRect.anchoredPosition = new Vector2(0f, -captionRestY);
        }
    }

    // The value's vertical position inside the panel is fixed; LabelYOffset
    // is then measured upward from this reference. Means dragging the panel
    // moves the whole stack and adjusting LabelYOffset only moves the label.
    private const float ValueRestY = 100f;

    private static void ApplyThickness(TextMeshProUGUI text, float dilate) {
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }
        mat.SetFloat("_FaceDilate", Mathf.Clamp(dilate, -1f, 1f));
    }

    private static void ApplyShadow(TextMeshProUGUI text, float x, float y, Color color) {
        Material mat = text.fontMaterial;
        if(mat == null) {
            return;
        }

        bool any = color.a > 0.001f && (Mathf.Abs(x) > 0.001f || Mathf.Abs(y) > 0.001f);
        if(any) {
            mat.EnableKeyword("UNDERLAY_ON");
        } else {
            mat.DisableKeyword("UNDERLAY_ON");
        }

        mat.SetColor("_UnderlayColor", color);
        mat.SetFloat("_UnderlayOffsetX", x * ShadowToUnderlay);
        mat.SetFloat("_UnderlayOffsetY", y * ShadowToUnderlay);
        mat.SetFloat("_UnderlaySoftness", 0f);
        mat.SetFloat("_UnderlayDilate", 0f);
    }

    public static void Save() => ConfMgr?.Save();

    public static void ResetPosition() {
        ComboSettings def = new();
        Conf.OffsetX = def.OffsetX;
        Conf.TopOffset = def.TopOffset;
        Apply();
        Save();
    }

    public static void Dispose() {
        if(canvasObj == null) {
            return;
        }

        ConfMgr?.Save();

        Object.Destroy(canvasObj);
        canvasObj = null;
        panel = null;
        caption = null;
        captionRect = null;
        value = null;
        valueRect = null;
        dragObj = null;
        updater = null;
    }

    private sealed class Updater : MonoBehaviour {
        private int lastDrawnCount = -1;

        private void Update() {
            if(panel == null) {
                return;
            }

            bool isReorganizing = UICore.IsOpen && UICore.CurrentMenuState == (int)OriginalMenuState.Reorganize;
            bool show = (Conf.Enabled && GameStats.InGame) || isReorganizing;
            if(panel.gameObject.activeSelf != show) {
                panel.gameObject.SetActive(show);
            }

            if(dragObj != null && dragObj.activeSelf != isReorganizing) {
                dragObj.SetActive(isReorganizing);
            }

            if(!show) {
                return;
            }

            Conf.OffsetX = panel.anchoredPosition.x;
            Conf.TopOffset = -panel.anchoredPosition.y;

            int count = isReorganizing && !GameStats.InGame ? 0 : Combo.Count;
            if(count != lastDrawnCount) {
                lastDrawnCount = count;
                value.text = count.ToString(CultureInfo.InvariantCulture);
            }

            value.color = Conf.ResolveValueColor(count);

            var (countScale, labelKick) = Combo.EvaluatePulse(
                Conf.PulseDuration,
                Conf.CountPulseScale,
                Conf.LabelPulseOffsetY
            );

            value.transform.localScale = new Vector3(countScale, countScale, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -captionRestY + labelKick);
        }
    }
}
