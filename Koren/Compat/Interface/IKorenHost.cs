namespace Koren.Compat.Interface;

public interface IKorenHost {
    IKorenLogger KorenLogger { get; }

    string KorenFilePath { get; }

    // Install locations, used by the updater to drop new builds in place.
    // ModsPath holds Koren.Loader.ML.dll; UserLibsPath holds Koren.dll.
    string ModsPath { get; }
    string UserLibsPath { get; }
}
