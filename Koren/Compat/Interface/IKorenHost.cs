namespace Koren.Compat.Interface;

public interface IKorenHost {
    IKorenLogger KorenLogger { get; }

    string KorenFilePath { get; }

    // Install locations, used by the updater to drop new builds in place.
    // ModsPath holds Koren.dll (single-DLL layout); UserLibsPath is only
    // touched to clean up the old two-DLL install.
    string ModsPath { get; }
    string UserLibsPath { get; }
}
