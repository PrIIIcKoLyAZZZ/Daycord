using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly AppDataPaths _paths;

    public SqliteConnectionFactory(AppDataPaths paths)
    {
        _paths = paths;
        SqliteTypeHandlers.Register();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 15
        };

        var connection = new SqliteConnection(builder.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}