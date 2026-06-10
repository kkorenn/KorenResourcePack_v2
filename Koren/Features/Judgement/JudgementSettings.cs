using Newtonsoft.Json.Linq;
using Koren.IO;
using Koren.IO.Interface;

namespace Koren.Features.Judgement;

// Persisted config for the judgement counts overlay (top-center row of
// per-judgement hit counts). Ranges match the original KorenResourcePack:
// OffsetY -100..200, Size 0.3..3, Spacing -20..80. OffsetX is new in v2 —
// the overlay is draggable in Reorganize mode like the other HUD elements.
public sealed class JudgementSettings : ISettingsFile {
    public bool Enabled = true;

    public float OffsetX = 0f;
    public float OffsetY = 0f;
    public float Size = 1f;
    public float Spacing = 0f;

    public JToken Serialize() {
        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(OffsetX)] = OffsetX,
            [nameof(OffsetY)] = OffsetY,
            [nameof(Size)] = Size,
            [nameof(Spacing)] = Spacing,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);
        OffsetX = IOUtils.Read(token, nameof(OffsetX), OffsetX);
        OffsetY = IOUtils.Read(token, nameof(OffsetY), OffsetY);
        Size = IOUtils.Read(token, nameof(Size), Size);
        Spacing = IOUtils.Read(token, nameof(Spacing), Spacing);
    }
}
