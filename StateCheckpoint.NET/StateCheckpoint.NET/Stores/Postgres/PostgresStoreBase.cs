using Npgsql;

namespace StateCheckpoint.NET.Stores.Postgres;

/// <summary>
/// Abstract base class for PostgreSQL stores.
/// Supports connection string OR an externally managed NpgsqlDataSource.
/// </summary>
public abstract class PostgresStoreBase : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;

    /// <summary>
    /// Initializes the store with a connection string.
    /// The store will create and manage its own NpgsqlDataSource.
    /// </summary>
    protected PostgresStoreBase(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _ownsDataSource = true;
    }

    /// <summary>
    /// Initializes the store with an existing NpgsqlDataSource.
    /// The store will NOT dispose the DataSource (the caller owns it).
    /// </summary>
    protected PostgresStoreBase(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _ownsDataSource = false;
    }

    /// <summary>
    /// Gets an open PostgreSQL connection from the DataSource pool.
    /// </summary>
    protected async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        return await _dataSource.OpenConnectionAsync(ct);
    }

    /// <summary>
    /// Disposes the underlying NpgsqlDataSource ONLY if this store created it.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (_ownsDataSource)
        {
            await _dataSource.DisposeAsync();
        }
    }
}