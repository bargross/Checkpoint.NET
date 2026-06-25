using System.Data;
using StateCheckpoint.NET.Stores.Mysql;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace StateCheckpoint.NET.Tests.Stores.SqlServer;

[Collection("NonParallel")]
public class SqlServerStoreBaseTests : IAsyncLifetime
{
    private string? _connectionString;

    private class TestableSqlServerStore : SqlServerStoreBase
    {
        public TestableSqlServerStore(string connectionString) : base(connectionString) { }
        public TestableSqlServerStore(SqlConnection connection) : base(connection) { }

        public new async Task<SqlConnection> GetConnectionAsync(CancellationToken ct = default)
            => await base.GetConnectionAsync(ct);
    }

    public async Task InitializeAsync()
    {
        _connectionString = await SqlServerTestHarness.GetConnectionStringAsync();
    }

    public async Task DisposeAsync()
    {
        await SqlServerTestHarness.DisposeAsync();
    }

    [Fact]
    public async Task GetConnectionAsync_OpensConnectionLazily()
    {
        var store = new TestableSqlServerStore(_connectionString!);

        var connection = await store.GetConnectionAsync();
        connection.State.Should().Be(ConnectionState.Open);

        await store.DisposeAsync();
    }

    [Fact]
    public async Task GetConnectionAsync_ReusesExistingOpenConnection()
    {
        var store = new TestableSqlServerStore(_connectionString!);

        var connection1 = await store.GetConnectionAsync();
        var connection2 = await store.GetConnectionAsync();

        connection1.Should().BeSameAs(connection2);

        await store.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_WithExistingConnection_UsesProvidedConnection()
    {
        using var existingConn = new SqlConnection(_connectionString!);
        await existingConn.OpenAsync();

        var store = new TestableSqlServerStore(existingConn);
        var retrievedConn = await store.GetConnectionAsync();

        retrievedConn.Should().BeSameAs(existingConn);
        await store.DisposeAsync();
        existingConn.State.Should().Be(ConnectionState.Open);
        await existingConn.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenOwnsConnection_ClosesConnection()
    {
        var store = new TestableSqlServerStore(_connectionString!);
        var connection = await store.GetConnectionAsync();
        connection.State.Should().Be(ConnectionState.Open);

        await store.DisposeAsync();

        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotOwnsConnection_DoesNotCloseConnection()
    {
        using var existingConn = new SqlConnection(_connectionString!);
        await existingConn.OpenAsync();
        var store = new TestableSqlServerStore(existingConn);

        await store.DisposeAsync();

        existingConn.State.Should().Be(ConnectionState.Open);
        await existingConn.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var store = new TestableSqlServerStore(_connectionString!);
        var act = async () =>
        {
            await store.DisposeAsync();
            await store.DisposeAsync();
        };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetConnectionAsync_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var store = new TestableSqlServerStore(_connectionString!);

        var act = () => store.GetConnectionAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Constructor_WithConnectionString_DoesNotOpenConnectionImmediately()
    {
        var store = new TestableSqlServerStore(_connectionString!);
        var field = typeof(SqlServerStoreBase).GetField("_connection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var connectionField = field?.GetValue(store) as SqlConnection;
        connectionField.Should().BeNull("the connection should not be created until GetConnectionAsync is called.");

        await store.DisposeAsync();
    }
}