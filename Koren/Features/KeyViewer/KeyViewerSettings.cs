using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.KeyViewer;

// Persisted config for the key viewer overlay. Skeleton for the v1 port —
// only the master toggle for now (default matches v1's Enabled = true);
// the rest of v1's KeyViewerSettings lands with the actual overlay.
// Lives in UserData/Koren/KeyViewer.json.
public sealed class KeyViewerSettings : ISettingsFile {
    public bool Enabled = true;

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
    }
}
