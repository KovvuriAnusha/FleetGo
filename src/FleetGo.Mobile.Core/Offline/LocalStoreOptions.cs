namespace FleetGo.Mobile.Core.Offline;

/// <summary>
/// Where the local database lives. The MAUI head supplies the platform's application data
/// directory (<c>FileSystem.AppDataDirectory</c>); tests supply a temporary file. Keeping the
/// path an injected value rather than a static lookup is what lets the store run in a plain
/// net10.0 test project with no device attached.
/// </summary>
/// <param name="DatabasePath">Full path to the SQLite file. Its directory must already exist.</param>
public sealed record LocalStoreOptions(string DatabasePath)
{
    /// <summary>The conventional file name, so every platform puts it in the same place under its own app data directory.</summary>
    public const string DefaultFileName = "fleetgo.local.db3";
}
