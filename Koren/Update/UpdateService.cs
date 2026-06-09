using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Koren.Async;
using Koren.Core;
using Newtonsoft.Json.Linq;

namespace Koren.Update;

public enum UpdateStatus {
    Idle,
    Checking,
    UpToDate,
    Available,
    Installing,
    Installed, // downloaded + placed; needs a restart to take effect
    Failed,
}

// A single available release found on GitHub.
public sealed class UpdateInfo {
    public string Tag;          // e.g. "v2.0.0-alpha-2"
    public SemVer Version;
    public string Url;          // release page
    public string KorenDllUrl;  // download URL for Koren.dll
    public string LoaderDllUrl; // download URL for Koren.Loader.ML.dll
}

// Checks GitHub Releases for a newer build on the user's chosen channel and,
// on request, downloads it over the installed DLLs (applied next launch).
//
// NOTE: this needs the releases repo to be PUBLIC. While it's private the
// GitHub API returns 404 and the check just reports a failure — that's
// expected for now. (No token is baked in; auth would leak the repo to anyone
// holding the DLL.)
//
// In-place DLL replacement works on macOS/Linux (the running image is
// independent of the file). On Windows the loaded file is locked, so a swap
// there would need an external bootstrapper.
public static class UpdateService {
    public static UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
    public static UpdateInfo Available { get; private set; }
    public static string Message { get; private set; } = "";

    // Dev-only: when on, a fake update (same version, no real assets) is
    // offered so the update flow can be exercised without a real release.
    public static bool DevSimulate { get; private set; }

    // Raised on the main thread whenever Status / Available changes.
    public static event System.Action OnChanged;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient() {
        try {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        } catch {
            // Some runtimes forbid changing this; the default may still work.
        }

        HttpClient client = new() { Timeout = System.TimeSpan.FromSeconds(20) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KorenResourcePack-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void Set(UpdateStatus status, string message = "") {
        Status = status;
        Message = message ?? "";
        MainThread.Enqueue(() => OnChanged?.Invoke());
    }

    // Kicks off a background check. Safe to call from the main thread.
    public static async void Check() {
        if(Status is UpdateStatus.Checking or UpdateStatus.Installing) {
            return;
        }

        Set(UpdateStatus.Checking);

        try {
            UpdateInfo found = await Task.Run(FetchLatest);
            Available = found;
            Set(found == null ? UpdateStatus.UpToDate : UpdateStatus.Available);
        } catch(System.Exception ex) {
            Available = null;
            Set(UpdateStatus.Failed, "Update check failed.");
            MainCore.Log.Wrn($"[Update] check failed: {ex.Message}");
        }
    }

    private static async Task<UpdateInfo> FetchLatest() {
        string url = $"https://api.github.com/repos/{Info.RepoOwner}/{Info.RepoName}/releases?per_page=30";
        string json = await Http.GetStringAsync(url);
        JArray releases = JArray.Parse(json);

        SemVer current = Info.Current;
        string skipped = MainCore.Conf.SkippedVersion ?? string.Empty;

        UpdateInfo best = null;
        foreach(JToken rel in releases) {
            if((bool?)rel["draft"] == true) {
                continue;
            }

            string tag = (string)rel["tag_name"];
            if(string.IsNullOrEmpty(tag) || tag == skipped) {
                continue;
            }
            if(!SemVer.TryParse(tag, out SemVer v)) {
                continue;
            }
            // Only builds on the chosen channel (or more stable) and strictly
            // newer than what's running.
            if(!MainCore.Conf.AcceptsChannel(v.Channel) || v.CompareTo(current) <= 0) {
                continue;
            }

            string korenUrl = null;
            string loaderUrl = null;
            if(rel["assets"] is JArray assets) {
                foreach(JToken a in assets) {
                    string name = (string)a["name"];
                    if(name == "Koren.dll") {
                        korenUrl = (string)a["browser_download_url"];
                    } else if(name == "Koren.Loader.ML.dll") {
                        loaderUrl = (string)a["browser_download_url"];
                    }
                }
            }
            // A release without both DLLs can't be installed; skip it.
            if(korenUrl == null || loaderUrl == null) {
                continue;
            }

            if(best == null || v.CompareTo(best.Version) > 0) {
                best = new UpdateInfo {
                    Tag = tag,
                    Version = v,
                    Url = (string)rel["html_url"],
                    KorenDllUrl = korenUrl,
                    LoaderDllUrl = loaderUrl,
                };
            }
        }

        return best;
    }

    // Downloads the given release and writes it over the installed DLLs.
    public static async void Install(UpdateInfo info) {
        if(info == null || Status == UpdateStatus.Installing) {
            return;
        }

        // A dev-simulated update has no real assets — go through the motions
        // but don't touch any files.
        if(info.KorenDllUrl == null || info.LoaderDllUrl == null) {
            Available = null;
            Set(UpdateStatus.Installed, "DEV: simulated install — no files changed.");
            return;
        }

        Set(UpdateStatus.Installing);

        try {
            await Task.Run(() => Download(info));
            Available = null;
            Set(UpdateStatus.Installed);
        } catch(System.Exception ex) {
            Set(UpdateStatus.Failed, "Install failed.");
            MainCore.Log.Wrn($"[Update] install failed: {ex.Message}");
        }
    }

    private static async Task Download(UpdateInfo info) {
        string staging = Path.Combine(MainCore.Paths.TempPath, "Update");
        Directory.CreateDirectory(staging);

        string stagedKoren = Path.Combine(staging, "Koren.dll");
        string stagedLoader = Path.Combine(staging, "Koren.Loader.ML.dll");

        // Download to staging first so a failure can't leave a half-written DLL
        // in the live folders.
        File.WriteAllBytes(stagedKoren, await Http.GetByteArrayAsync(info.KorenDllUrl));
        File.WriteAllBytes(stagedLoader, await Http.GetByteArrayAsync(info.LoaderDllUrl));

        File.Copy(stagedKoren, Path.Combine(MainCore.Host.UserLibsPath, "Koren.dll"), true);
        File.Copy(stagedLoader, Path.Combine(MainCore.Host.ModsPath, "Koren.Loader.ML.dll"), true);
    }

    // Remembers this version as skipped so it's no longer offered.
    public static void Skip(UpdateInfo info) {
        if(info == null) {
            return;
        }

        MainCore.Conf.SkippedVersion = info.Tag;
        MainCore.ConfMgr.RequestSave();
        Available = null;
        Set(UpdateStatus.UpToDate);
    }

    // Dev-only: toggle a fake available update (current version, no assets) to
    // exercise the prompt + install flow without a real release.
    public static void SetDevSimulate(bool on) {
        DevSimulate = on;

        if(on) {
            Available = new UpdateInfo {
                Tag = "v" + Info.DisplayVersion,
                Version = Info.Current,
                Url = Info.GithubLink,
                KorenDllUrl = null,
                LoaderDllUrl = null,
            };
            Set(UpdateStatus.Available);
        } else {
            Available = null;
            Set(UpdateStatus.Idle);
        }
    }
}
