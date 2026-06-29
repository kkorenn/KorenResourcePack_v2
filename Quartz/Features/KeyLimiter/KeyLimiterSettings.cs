using Newtonsoft.Json.Linq;
using Quartz.IO;
using Quartz.IO.Interface;

namespace Quartz.Features.KeyLimiter;

// One named allowed-key set. The limiter enforces exactly one profile at a
// time (the active one); the others sit dormant until switched to. Mouse
// buttons are always allowed and never stored.
public sealed class KeyLimiterProfile {
    public string Name = "";
    public int[] Keys = [];
}

// Persisted config for the Key Limiter, ported from the original
// KorenResourcePack. v1 stored a single allowed list; this adds profiles, each
// its own named allowed set, with one active at a time (e.g. a 12-key profile
// and a 16-key profile you switch between). The legacy default set lives in
// the first profile: Q 3 4 T O - = \ Space B H , A LShift RShift Return.
// Lives in UserData/Quartz/KeyLimiter.json.
public sealed class KeyLimiterSettings : ISettingsFile {
    public bool Enabled = true;

    public List<KeyLimiterProfile> Profiles = [
        new KeyLimiterProfile {
            Name = "Profile 1",
            Keys = [
                113, 51, 52, 116, 111, 45, 61, 92,
                32, 98, 104, 46, 97, 304, 273, 13,
            ],
        },
    ];

    public int ActiveProfile = 0;

    // Backward-compatible accessor: every existing call site (the limiter's
    // hot path, the key viewer sync, the settings importer) reads and writes
    // the active profile's keys through this, so they keep working unchanged
    // and stay pointed at whichever profile is current.
    public int[] AllowedKeys {
        get => ActiveProfileOrDefault().Keys;
        set => ActiveProfileOrDefault().Keys = value ?? [];
    }

    public KeyLimiterProfile ActiveProfileOrDefault() {
        if(Profiles == null || Profiles.Count == 0) Profiles = [new KeyLimiterProfile { Name = "Profile 1", Keys = [] }];
        if(ActiveProfile < 0 || ActiveProfile >= Profiles.Count) ActiveProfile = 0;
        return Profiles[ActiveProfile];
    }

    public JToken Serialize() {
        JArray profiles = [];
        foreach(KeyLimiterProfile p in Profiles) {
            profiles.Add(new JObject {
                ["Name"] = p.Name ?? "",
                ["Keys"] = new JArray(p.Keys ?? []),
            });
        }

        return new JObject {
            [nameof(Enabled)] = Enabled,
            [nameof(Profiles)] = profiles,
            [nameof(ActiveProfile)] = ActiveProfile,
        };
    }

    public void Deserialize(JToken token) {
        Enabled = IOUtils.Read(token, nameof(Enabled), Enabled);

        if(token?[nameof(Profiles)] is JArray profArr) {
            List<KeyLimiterProfile> list = [];
            int index = 1;
            foreach(JToken pt in profArr) {
                list.Add(new KeyLimiterProfile {
                    Name = IOUtils.Read(pt, "Name", "Profile " + index),
                    Keys = ReadKeys(pt?["Keys"]),
                });
                index++;
            }
            if(list.Count > 0) Profiles = list;
            ActiveProfile = IOUtils.Read(token, nameof(ActiveProfile), 0);
        } else if(token?["AllowedKeys"] is JArray legacy) {
            // Pre-profile config: fold the single allowed list into profile 0.
            Profiles = [new KeyLimiterProfile { Name = "Profile 1", Keys = ReadKeys(legacy) }];
            ActiveProfile = 0;
        }

        if(Profiles == null || Profiles.Count == 0) Profiles = [new KeyLimiterProfile { Name = "Profile 1", Keys = [] }];
        if(ActiveProfile < 0 || ActiveProfile >= Profiles.Count) ActiveProfile = 0;
    }

    private static int[] ReadKeys(JToken token) {
        if(token is not JArray arr) return [];
        List<int> keys = [];
        foreach(JToken t in arr)
            try { keys.Add((int)t); } catch { }
        return [.. keys];
    }
}
