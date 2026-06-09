using MelonLoader;
using MelonLoader.Utils;
using Koren.Core;
using Koren.Compat.Interface;

// MelonInfo's version must be a compile-time constant, so it uses the numeric
// core version; the full channel + build string lives in Info.DisplayVersion
// (shown in-game and used for update checks).
[assembly: MelonInfo(typeof(Koren.Loader.ML.Loader), Info.Name, Info.Version, Info.Author, Info.GithubLink)]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]

namespace Koren.Loader.ML;

// MelonLoader entry point. Ships under Mods/; references Koren.dll in UserLibs.
// Acts as the host bridge (IKorenHost/IKorenLogger) into the runtime.
public class Loader : MelonMod, IKorenHost, IKorenLogger {

    public IKorenLogger KorenLogger => this;

    public string KorenFilePath => MelonEnvironment.UserDataDirectory;
    public string ModsPath => MelonEnvironment.ModsDirectory;
    public string UserLibsPath => MelonEnvironment.UserLibsDirectory;

    public override void OnInitializeMelon() {
        MainCore.Initialize(this);
    }

    public override void OnDeinitializeMelon() {
        MainCore.Dispose();
    }

    public override void OnUpdate() {
        MainCore.Tick();
    }

    public void KorenMsg(string msg) => MelonLogger.Msg(msg);
    public void KorenWrn(string msg) => MelonLogger.Warning(msg);
    public void KorenErr(string msg) => MelonLogger.Error(msg);
}
