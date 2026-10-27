using FleetGo.Mobile.Core.Offline;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// A real <see cref="SqliteLocalFleetStore"/> on a throwaway file.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the real store rather than an in-memory fake of it. The things most likely to
/// be wrong here are SQL, schema and transaction behaviour - exactly what a hand-written fake
/// would paper over - and sqlite-net's bundled engine runs on the test host as happily as on a
/// phone, so there is no reason to test against anything else.
/// </para>
/// <para>
/// A file rather than <c>:memory:</c> because one of the properties worth proving is that the
/// queue survives the process: an in-memory database disappears with its connection, which is
/// the opposite of what the outbox is for.
/// </para>
/// </remarks>
internal sealed class LocalStoreHarness : IAsyncDisposable
{
    private readonly string _directory;

    private LocalStoreHarness(string directory, SqliteLocalFleetStore store)
    {
        _directory = directory;
        Store = store;
        DatabasePath = Path.Combine(directory, LocalStoreOptions.DefaultFileName);
    }

    public SqliteLocalFleetStore Store { get; }

    public string DatabasePath { get; }

    public static async Task<LocalStoreHarness> CreateAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fleetgo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        SqliteLocalFleetStore store = new(new LocalStoreOptions(Path.Combine(directory, LocalStoreOptions.DefaultFileName)));
        await store.InitializeAsync();

        return new LocalStoreHarness(directory, store);
    }

    /// <summary>Closes this store and opens a second one on the same file - a stand-in for restarting the app.</summary>
    public async Task<SqliteLocalFleetStore> ReopenAsync()
    {
        await Store.DisposeAsync();

        SqliteLocalFleetStore reopened = new(new LocalStoreOptions(DatabasePath));
        await reopened.InitializeAsync();
        return reopened;
    }

    public async ValueTask DisposeAsync()
    {
        await Store.DisposeAsync();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test host that still has the file mapped should not fail the test run over a
            // temp directory.
        }
    }
}
