using GTweens.Builders;
using GTweens.Easings;
using GTweens.Extensions;
using GTweens.Tweens;
using Koren.Async;
using Koren.Core;
using Koren.IO;
using Koren.Localization;
using Koren.Resource;
using Koren.Tween;
using Koren.UI.Generator;
using Koren.UI.Objects.Impl;
using Koren.UI.Utility;
using Koren.Update;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace Koren.UI.Factory.Page;

internal static class PageSettings {
    private static readonly Dictionary<TextLocalization, (GameObject LabelRow, GameObject MainRow)> objects = [];
    private static UIDropDown<string> languageDropdown;

    // Update UI, refreshed from UpdateService.OnChanged.
    private static UIButton updateCheckButton;
    private static TextMeshProUGUI updateStatusText;
    private static GameObject updateActionRow;
    private static TextMeshProUGUI updateVersionText;
    private static bool updateHooked;

    public static void Create(RectTransform parent) {
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

        CoreSettings defSet = new();

        var inputRow = GenerateUI.Row(content.transform);
        var findInput =
        GenerateUI.Input(
            inputRow,
            null,
            null,
            value => {
                bool isBlank = string.IsNullOrWhiteSpace(value);
                Dictionary<GameObject, bool> labelActivationMap = [];

                foreach(var pair in objects) {
                    if(pair.Value.LabelRow != null) {
                        labelActivationMap[pair.Value.LabelRow] = isBlank;
                    }
                }

                string normalizedQuery = UICore.NormalizeString(value);

                foreach(var pair in objects) {
                    TextLocalization labelLoc = pair.Key;
                    var (labelRow, mainRow) = pair.Value;

                    if(labelRow == null || mainRow == null) {
                        continue;
                    }

                    string normalizedTarget = labelLoc != null ? UICore.NormalizeString(labelLoc.Value) : string.Empty;

                    bool isMainMatch = isBlank || (!string.IsNullOrEmpty(normalizedTarget) && normalizedTarget.Contains(normalizedQuery));

                    mainRow.SetActive(isMainMatch);

                    if(isMainMatch) {
                        labelActivationMap[labelRow] = true;
                    }
                }

                foreach(var kvp in labelActivationMap) {
                    kvp.Key.SetActive(kvp.Value);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            },
            "Find",
            MainCore.Spr.Get(UISprite.MagnifyingGlass128),
            "search_find"
        );
        findInput.Placeholder.gameObject.AddComponent<TextLocalization>().Init("FIND", "Find");
        findInput.InputField.characterLimit = 22;

        var langLabelRow = GenerateUI.Row(content.transform);
        var langText = GenerateUI.AddTextH1(langLabelRow);
        var langTextTr = langText.gameObject.AddComponent<TextLocalization>().Init("LANGUAGE", "Language");

        string[] langs = [.. MainCore.Tr.GetLanguages().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        var langRow = GenerateUI.Row(content.transform);
        languageDropdown = GenerateUI.DropDown(
            langRow,
            null,
            MainCore.Tr.Language,
            langs,
            lang => {
                if(lang == Translator.FALLBACK_LANGUAGE) {
                    return "DEFAULT";
                }

                string native = MainCore.Tr.GetForLanguage(
                    "0NATIVELANG",
                    lang,
                    lang
                );

                return $"{native} ({lang})";
            },
            value => {
                MainCore.Tr.Language = value;
                MainCore.Conf.Language = value;
                MainCore.ConfMgr.RequestSave();
                TextLocalization.RefreshAll();
            },
            "language_dropdown"
        );

        UIButton langBtn = GenerateUI.Button(
            langRow,
            () => { },
            "Reload",
            "language_reload"
        );
        langBtn.OnClick = async () => {
            languageDropdown.SetExpanded(false);
            languageDropdown.SetBlocked(true);
            langBtn.SetBlocked(true);
            langBtn.Label.text = "...";
            _ = Task.Run(async () => {
                await MainCore.Tr.Load(MainCore.Paths.LangPath);
                MainThread.Enqueue(() => {
                    languageDropdown.SetBlocked(false);
                    langBtn.SetBlocked(false);
                    TextLocalization.RefreshAll();
                });
            });
        };
        {
            var br = langBtn.Rect;
            br.pivot = new(1f, 1f);
            br.anchorMin = new(1f, 1f);
            br.anchorMax = new(1f, 1f);
            br.sizeDelta = new(114f, 50f);
            br.offsetMax = Vector2.zero;
        }
        langBtn.Label.gameObject.AddComponent<TextLocalization>().Init("RELOAD", "Reload");

        objects[langTextTr] = (langLabelRow.gameObject, langRow.gameObject);

        var overlayerText = GenerateUI.AddTextH1(GenerateUI.Row(content.transform));
        var overlayerTextTr = overlayerText.gameObject.AddComponent<TextLocalization>().Init("OVERLAYER", "Koren");

        var startupRow = GenerateUI.Row(content.transform);
        UIToggle startupToggle = GenerateUI.Toggle(
            startupRow,
            defSet.ShowOnStartup,
            MainCore.Conf.ShowOnStartup,
            toggle => {
                MainCore.Conf.ShowOnStartup = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Show KorenResourcePack Settings at Startup",
            "show_on_startup"
        );
        var startupToggleTr = startupToggle.Label.gameObject.AddComponent<TextLocalization>().Init("SHOW_OVERLAYER_PANEL_AT_STARTUP", "Show KorenResourcePack Settings at Startup");
        objects[startupToggleTr] = (overlayerText.gameObject, startupRow.gameObject);

        var keybindRow = GenerateUI.Row(content.transform);
        var keybindLabel = GenerateUI.KeyBind(
            keybindRow,
            (Keybind.KeyModifier)MainCore.Conf.ToggleModifier,
            (KeyCode)MainCore.Conf.ToggleKey,
            (mod, key) => {
                MainCore.Conf.ToggleModifier = (int)mod;
                MainCore.Conf.ToggleKey = (int)key;
                MainCore.ConfMgr.RequestSave();
            },
            "Toggle Menu Keybind",
            "toggle_keybind"
        );
        var keybindTr = keybindLabel.gameObject.AddComponent<TextLocalization>().Init("TOGGLE_KEYBIND", "Toggle Menu Keybind");
        objects[keybindTr] = (overlayerText.gameObject, keybindRow.gameObject);

        var tooltipRow = GenerateUI.Row(content.transform);
        UIToggle tooltipToggle = GenerateUI.Toggle(
            tooltipRow,
            defSet.Tooltip,
            MainCore.Conf.Tooltip,
            toggle => {
                Tooltip.Hide();
                MainCore.Conf.Tooltip = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Show Tooltip",
            "show_tooltip"
        );
        tooltipToggle.Rect.AddToolTip(
            "DESC_SHOW_TOOLTIP",
            "This is a Tooltip!"
        );
        var tooltipToggleTr = tooltipToggle.Label.gameObject.AddComponent<TextLocalization>().Init("SHOW_TOOLTIP", "Show Tooltip");
        objects[tooltipToggleTr] = (overlayerText.gameObject, tooltipRow.gameObject);

        var middleClickRow = GenerateUI.Row(content.transform);
        UIToggle middleClickToggle = GenerateUI.Toggle(
            middleClickRow,
            defSet.MiddleClickToDefault,
            MainCore.Conf.MiddleClickToDefault,
            toggle => {
                MainCore.Conf.MiddleClickToDefault = toggle;
                MainCore.ConfMgr.RequestSave();
            },
            "Middle-click to set as default",
            "middle_click_default"
        );
        middleClickToggle.Rect.AddToolTip(
            "DESC_MIDDLE_CLICK_TO_SET_AS_DEFAULT",
            "Setting that restores an item to its default value when you middle-click on it.\nYou can identify it by a small dot at the top-left of the item"
        );
        var middleClickToggleTr = middleClickToggle.Label.gameObject.AddComponent<TextLocalization>().Init("MIDDLE_CLICK_TO_SET_AS_DEFAULT", "Middle-click to set as default");
        objects[middleClickToggleTr] = (overlayerText.gameObject, middleClickRow.gameObject);

        static float uiScaleFilter(float v) {
            v = Mathf.Round(v * 100f) / 100f;
            return Mathf.Clamp(v, 0.8f, 1.6f);
        }
        var uiScaleRow = GenerateUI.Row(content.transform);
        UISlider uiScale = GenerateUI.Slider(
            uiScaleRow,
            1f,
            0.8f,
            1.6f,
            MainCore.Conf.UIScale,
            uiScaleFilter,
            null,
            null,
            "UI Scale",
            "ui_scale"
        );
        uiScale.Format = "0.00x";
        uiScale.OnChanged = value => MainCore.Conf.UIScale = value;
        GTween scaleSeq = null;
        uiScale.OnComplete = value => {
            MainCore.Conf.UIScale = value;
            MainCore.ConfMgr.RequestSave();

            scaleSeq?.Kill();

            float scaleStart = UICore.PanelScale;
            Vector2 targetSize = UICore.DefaultPanelSize;
            UICore.LastPanelSize = targetSize;

            scaleSeq = GTweenSequenceBuilder.New()
                .Append(
                    GTweenExtensions.Tween(
                        () => scaleStart,
                        x => UICore.PanelScale = x,
                        value,
                        0.4f
                    ).SetEasing(Easing.OutExpo)
                )
                .Join(
                    UICore.Panel.GTSizeDelta(targetSize, 0.4f)
                        .SetEasing(Easing.OutExpo)
                )
                .Build();

            MainCore.TC.Play(scaleSeq);
        };
        var uiScaleTr = uiScale.Label.gameObject.AddComponent<TextLocalization>().Init("UI_SCALE", "UI Scale");

        objects[uiScaleTr] = (overlayerText.gameObject, uiScaleRow.gameObject);

        var scrollRow = GenerateUI.Row(content.transform);
        UISlider scrollSpeed = GenerateUI.Slider(
            scrollRow,
            80f,
            20f,
            300f,
            MainCore.Conf.ScrollSpeed,
            Mathf.Round,
            v => MainCore.Conf.ScrollSpeed = v,
            v => { MainCore.Conf.ScrollSpeed = v; MainCore.ConfMgr.RequestSave(); },
            "Scroll Speed",
            "scroll_speed"
        );
        scrollSpeed.Format = "0 px";
        var scrollTr = scrollSpeed.Label.gameObject.AddComponent<TextLocalization>().Init("SCROLL_SPEED", "Scroll Speed");
        objects[scrollTr] = (overlayerText.gameObject, scrollRow.gameObject);

        var accentRow = GenerateUI.Row(content.transform);
        UIColorPicker accentPicker = GenerateUI.ColorPicker(
            accentRow,
            new Color(1f, 0.6f, 0.6f, 1f),
            MainCore.Conf.GetAccentColor(),
            c => UICore.SetAccentColor(c, false),
            c => UICore.SetAccentColor(c, true),
            "Accent Color",
            "accent_color",
            false
        );
        accentPicker.Rect.AddToolTip(
            "DESC_ACCENT_COLOR",
            "Recolors the whole Koren UI. Middle-click to reset."
        );
        var accentTr = accentPicker.Label.gameObject.AddComponent<TextLocalization>().Init("ACCENT_COLOR", "Accent Color");
        objects[accentTr] = (overlayerText.gameObject, accentRow.gameObject);

        var updatesLabelRow = GenerateUI.Row(content.transform);
        var updatesText = GenerateUI.AddTextH1(updatesLabelRow);
        var updatesTextTr = updatesText.gameObject.AddComponent<TextLocalization>().Init("UPDATES", "Updates");

        ReleaseChannel[] channels = [
            ReleaseChannel.Stable,
            ReleaseChannel.ReleaseCandidate,
            ReleaseChannel.Beta,
            ReleaseChannel.Alpha,
        ];
        var channelRow = GenerateUI.Row(content.transform);
        UIDropDown<ReleaseChannel> channelDropdown = GenerateUI.DropDown(
            channelRow,
            ReleaseChannel.Stable,
            MainCore.Conf.GetUpdateChannel(),
            channels,
            ch => ch switch {
                ReleaseChannel.Stable => "Stable",
                ReleaseChannel.ReleaseCandidate => "Release Candidate",
                ReleaseChannel.Beta => "Beta",
                ReleaseChannel.Alpha => "Alpha",
                _ => ch.ToString(),
            },
            ch => {
                MainCore.Conf.UpdateChannel = (int)ch;
                MainCore.ConfMgr.RequestSave();
            },
            "update_channel"
        );
        channelDropdown.Rect.AddToolTip(
            "DESC_UPDATE_CHANNEL",
            "Which builds to receive when updating. Alpha includes every build; each step up is more stable, with Stable being only final releases."
        );
        objects[updatesTextTr] = (updatesLabelRow.gameObject, channelRow.gameObject);

        var updateCheckRow = GenerateUI.Row(content.transform);
        updateCheckButton = GenerateUI.Button(
            updateCheckRow,
            () => UpdateService.Check(),
            "Check for Updates",
            "update_check"
        );
        updateCheckButton.Label.gameObject.AddComponent<TextLocalization>().Init("CHECK_FOR_UPDATES", "Check for Updates");

        var updateStatusRow = GenerateUI.Row(content.transform);
        updateStatusText = GenerateUI.AddText(updateStatusRow);
        updateStatusText.text = "";

        var updateActionRect = GenerateUI.Row(content.transform);
        updateActionRow = updateActionRect.gameObject;
        updateVersionText = GenerateUI.AddText(updateActionRect);

        UIButton installButton = GenerateUI.Button(
            updateActionRect,
            () => UpdateService.Install(UpdateService.Available),
            "Install",
            "update_install"
        );
        {
            var r = installButton.Rect;
            r.pivot = new(1f, 0.5f);
            r.anchorMin = new(1f, 0.5f);
            r.anchorMax = new(1f, 0.5f);
            r.sizeDelta = new(120f, 40f);
            r.anchoredPosition = new(-12f, 0f);
        }

        UIButton skipButton = GenerateUI.Button(
            updateActionRect,
            () => UpdateService.Skip(UpdateService.Available),
            "Skip",
            "update_skip"
        );
        {
            var r = skipButton.Rect;
            r.pivot = new(1f, 0.5f);
            r.anchorMin = new(1f, 0.5f);
            r.anchorMax = new(1f, 0.5f);
            r.sizeDelta = new(90f, 40f);
            r.anchoredPosition = new(-140f, 0f);
        }

        if(!updateHooked) {
            UpdateService.OnChanged += RefreshUpdates;
            updateHooked = true;
        }
        RefreshUpdates();

        var fontLabelRow = GenerateUI.Row(content.transform);
        var fontText = GenerateUI.AddTextH1(fontLabelRow);
        var fontTextTr = fontText.gameObject.AddComponent<TextLocalization>().Init("FONT", "Font");

        var fontRow = GenerateUI.Row(content.transform);
        GenerateUI.DropDown(
            fontRow,
            FontManager.DefaultName,
            FontManager.CurrentName,
            FontManager.GetAvailableFonts(),
            name => name,
            name => FontManager.SetFont(name, true),
            "font_dropdown"
        );
        objects[fontTextTr] = (fontLabelRow.gameObject, fontRow.gameObject);
    }

    // Pulls the latest UpdateService state into the update row. Runs on the
    // main thread (UpdateService raises OnChanged via MainThread).
    internal static void RefreshUpdates() {
        if(updateStatusText == null || updateActionRow == null) {
            return;
        }

        UpdateStatus status = UpdateService.Status;
        UpdateInfo info = UpdateService.Available;
        bool available = status == UpdateStatus.Available && info != null;

        updateStatusText.text = status switch {
            UpdateStatus.Checking => "Checking for updates…",
            UpdateStatus.UpToDate => "You're up to date.",
            UpdateStatus.Available => "Update available:",
            UpdateStatus.Installing => "Downloading update…",
            UpdateStatus.Installed => "Update installed — restart the game to apply.",
            UpdateStatus.Failed => UpdateService.Message,
            _ => "",
        };

        updateActionRow.SetActive(available);
        if(available) {
            updateVersionText.text = $"v{Info.DisplayVersion}   →   {info.Tag}";
        }

        if(updateCheckButton != null) {
            updateCheckButton.SetBlocked(status is UpdateStatus.Checking or UpdateStatus.Installing);
        }
    }

    internal static void OnTranslatorLoadEnd() {
        string[] langs = [.. MainCore.Tr.GetLanguages().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];

        languageDropdown.SetValues(langs);
        languageDropdown.Set(
            string.IsNullOrWhiteSpace(MainCore.Conf.Language)
                ? Translator.FALLBACK_LANGUAGE
                : MainCore.Conf.Language,
            false
        );
    }
}