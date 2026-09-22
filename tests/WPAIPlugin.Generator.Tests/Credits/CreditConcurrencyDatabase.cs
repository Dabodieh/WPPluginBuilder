using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.InMemory.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Generator.Tests.Credits;

// EF InMemory 8 inserts ledger rows before checking modified account tokens,
// and has no rollback. For these concurrency tests only, check the account
// first within the provider's same SaveChanges unit. Real EF token checking
// and contention remain unchanged; no synthetic exception or extra lock.
// Production Npgsql supplies transaction rollback without this test adapter.
#pragma warning disable EF1001
public sealed class CreditConcurrencyDatabase(
    DatabaseDependencies dependencies, IInMemoryStoreCache storeCache,
    IDbContextOptions options, IDesignTimeModel model,
    IUpdateAdapterFactory adapterFactory, IDiagnosticsLogger<DbLoggerCategory.Update> logger)
    : InMemoryDatabase(dependencies, storeCache, options, model, adapterFactory, logger)
{
    public override Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(entries.OrderBy(e =>
            e.EntityType.ClrType == typeof(CreditAccount) && e.EntityState == EntityState.Modified ? 0 : 1).ToList(), cancellationToken);
}
#pragma warning restore EF1001
