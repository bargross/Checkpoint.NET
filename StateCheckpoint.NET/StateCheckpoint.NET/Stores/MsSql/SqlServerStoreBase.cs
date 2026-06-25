using Microsoft.Data.SqlClient;

namespace StateCheckpoint.NET.Stores.Mysql;

/// <summary>
/// Abstract base class for SQL Server stores.
/// Manages connection lifecycle and lazy opening.
/// </summary>
public abstract class SqlServerStoreBase : IAsyncDisposable
{
    private readonly string _connectionString = string.Empty;
    private SqlConnection? _connection;
    private readonly bool _ownsConnection;

    /// <summary>
    /// Initializes the store with a connection string.
    /// The store will create and manage its own SqlConnection.
    /// </summary>
    protected SqlServerStoreBase(string connectionString)
    {
        _connectionString = connectionString;
        _ownsConnection = true;
    }

    /// <summary>
    /// Initializes the store with an existing SqlConnection.
    /// The store will NOT dispose the connection (the caller owns it).
    /// </summary>
    protected SqlServerStoreBase(SqlConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = false;
    }

    /// <summary>
    /// Gets an open SQL Server connection. Opens it lazily if not already open.
    /// </summary>
    protected async Task<SqlConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            if (_connection == null && _ownsConnection)
            {
                _connection = new SqlConnection(_connectionString);
            }

            if (_connection != null && _connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync(ct);
            }
        }

        return _connection!;
    }

    /// <summary>
    /// Disposes the underlying SqlConnection ONLY if this store created it.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (_ownsConnection && _connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}