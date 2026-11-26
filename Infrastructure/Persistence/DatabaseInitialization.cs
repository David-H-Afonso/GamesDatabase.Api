using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace GamesDatabase.Api.Infrastructure.Persistence;

/// <summary>
/// Historical migrations start from a pre-existing schema. Only a genuinely
/// empty SQLite database gets the current model plus its migration baseline.
/// Existing databases always use their real pending migrations.
/// </summary>
public static class DatabaseInitialization
{
    public static async Task<bool> InitializeAsync(GamesDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var fresh = false;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT IN ('sqlite_sequence', '__EFMigrationsHistory');";
            var tables = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (tables == 0)
            {
                var history = context.GetService<IHistoryRepository>();
                if ((await history.GetAppliedMigrationsAsync(cancellationToken)).Count != 0)
                    throw new InvalidOperationException("A database with migration history is not an empty installation.");
                await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
                await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
                var version = context.Model.GetProductVersion() ?? "10.0.0";
                foreach (var migration in context.Database.GetMigrations())
                    await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migration, version)), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                fresh = true;
            }
        }
        if (!fresh) await context.Database.MigrateAsync(cancellationToken);
        return fresh;
    }
}
