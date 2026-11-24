using FiapX.Notification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace FiapX.Notification.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MigrationTests(ApiFixture fixture)
{
    /// <summary>
    /// Database:MigrateOnStartup with several replicas starting together: EF Core 9+ takes a migration lock
    /// (NpgsqlMigrationDatabaseLock), so concurrent MigrateAsync calls apply the migrations exactly once.
    /// </summary>
    [Fact]
    public async Task ConcurrentMigrations_OnAFreshDatabase_ApplyOnce()
    {
        var database = $"migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.Postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(fixture.PostgresConnectionString(database))
            .UseSnakeCaseNamingConvention()
            .Options;

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = new NotificationDbContext(options);
            await db.Database.MigrateAsync();
        }));

        await using var check = new NotificationDbContext(options);
        (await check.Database.GetAppliedMigrationsAsync()).Count().ShouldBe(check.Database.GetMigrations().Count());
        (await check.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
