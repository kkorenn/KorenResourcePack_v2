using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Judgement;

// Persisted config for the judgement popup hider. Defaults match v1's
// Settings.cs: enabled, hiding only the Perfect popup (v1's default mask also
// covered the XPerfect mod's popups, which v2 doesn't integrate with).
public sealed class JudgementPopupHiderSettings : ISettingsFile {
    public bool Enabled = true;
    public int HiddenMask = 1 << (int)HitMargin.Perfect;

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(HiddenMask)] = HiddenMask,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        HiddenMask = IOUtils.Read(token, nameof(HiddenMask), HiddenMask);
    }
}
