using Daycord.Core.Application.Contracts;

namespace Daycord.Infrastructure.Persistence;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly AppDataPaths _paths;
    private readonly MigrationRunner _migrations;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    public DatabaseInitializer(AppDataPaths paths, MigrationRunner migrations)
    {
        _paths = paths;
        _migrations = migrations;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            _paths.EnsureDataDirectory();
            var databaseExisted = File.Exists(_paths.DatabasePath);
            await _migrations.RunAsync(databaseExisted, cancellationToken);
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}