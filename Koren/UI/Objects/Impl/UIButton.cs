using UnityEngine;
using UnityEngine.UI;
using GTweens.Tweens;
using Koren.Tween;
using GTweens.Easings;
using Koren.Core;

using TMPro;

namespace Koren.UI.Objects.Impl;

public class UIButton : UIObject {
    public Action OnClick { get; set; }
    public TextMeshProUGUI Label { get; }
    public Image Background { get; }

    // Resolved lazily so the colors keep tracking accent palette changes;
    // swap these to give a button a different visual weight (see SetSecondary).
    public Func<Color> RestColor { get; set; } = static () => UIColors.ObjectButton;
    public Func<Color> HoverColor { get; set; } = static () => UIColors.ObjectActiveLightBright;

    private GTween hoverTween;

    public UIButton(
        string id,
        RectTransform rect,
        TextMeshProUGUI label,
        Image background,
        Action onClick
    ) : base(id, rect) {
        Label = label;
        Background = background;
        OnClick = onClick;

        UpdateVisual(true);
    }

    // De-emphasizes the button (panel-colored at rest) for actions that
    // shouldn't compete with a primary button next to them.
    public UIButton SetSecondary() {
        RestColor = static () => UIColors.ObjectBG;
        HoverColor = static () => UIColors.ObjectButton;
        UpdateVisual(true);
        return this;
    }

    public void OnHoverEnter() {
        hoverTween?.Kill();

        hoverTween = Background
            .GTColor(HoverColor(), 0.12f)
            .SetEasing(Easing.OutSine);
        MainCore.TC.Play(hoverTween);
    }

    public void OnHoverExit() {
        hoverTween?.Kill();

        hoverTween = Background
            .GTColor(RestColor(), 0.12f)
            .SetEasing(Easing.OutSine);
        MainCore.TC.Play(hoverTween);
    }

    public void Click(bool invoke = true) {
        if(invoke) {
            OnClick?.Invoke();
        }

        UpdateVisual();
    }

    public void UpdateVisual(bool noAnimate = false) {
        hoverTween?.Kill();

        if(noAnimate) {
            Background.color = RestColor();
            return;
        }

        hoverTween = Background
            .GTColor(RestColor(), 0.2f)
            .SetEasing(Easing.OutSine);
        MainCore.TC.Play(hoverTween);
    }
}
