using Koren.Features.KeyViewer;
using Koren.UI.Generator;
using UnityEngine;

namespace Koren.UI.Factory.Page;

// Key Viewer settings section for the Overlay tab. Skeleton for the v1 port:
// just the enable toggle for now — it persists but drives nothing until the
// overlay itself is ported.
internal static class PageKeyViewer {
    public static void AppendTo(Transform content) {
        KeyViewerOverlay.EnsureConf();
        KeyViewerSettings conf = KeyViewerOverlay.Conf;
        KeyViewerSettings def = new();

        var sec = GenerateUI.Collapsible(content, "Key Viewer", startExpanded: false);

        GenerateUI.Toggle(
            GenerateUI.Row(sec.Body),
            def.Enabled,
            conf.Enabled,
            v => { conf.Enabled = v; KeyViewerOverlay.Save(); },
            "Enable Key Viewer",
            "keyviewer_enabled"
        ).Rect.AddToolTip(
            "DESC_KEYVIEWER_ENABLED",
            "Shows pressed keys on screen. Not implemented yet — this toggle does nothing for now."
        );
    }
}
