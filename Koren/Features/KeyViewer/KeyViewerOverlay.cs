using Koren.Core;
using Koren.IO;

namespace Koren.Features.KeyViewer;

// Key viewer overlay, ported from the original KorenResourcePack
// (src/KeyViewer). Skeleton: config plumbing only — the toggle in the
// Overlay tab persists, but nothing renders or hooks input yet.
public static class KeyViewerOverlay {
    public static SettingsFile<KeyViewerSettings> ConfMgr { get; private set; }
    public static KeyViewerSettings Conf => ConfMgr?.Data;

    public static void EnsureConf() {
        if(ConfMgr != null) {
            return;
        }

        ConfMgr = new SettingsFile<KeyViewerSettings>(
            Path.Combine(MainCore.Paths.RootPath, "KeyViewer.json")
        );
        ConfMgr.Load();
    }

    public static void Save() => ConfMgr?.RequestSave();
}
