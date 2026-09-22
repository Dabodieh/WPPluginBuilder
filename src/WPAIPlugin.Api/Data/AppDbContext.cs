using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WPAIPlugin.Api.Data;

// SaaS persistence. ASP.NET Core Identity tables (Milestone 10),
// PluginProjects/PluginVersions (Milestone 11), CreditAccounts/
// CreditTransactions (Milestone 12) - still no billing/payment tables.
public sealed class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<PluginProject> PluginProjects => Set<PluginProject>();

    public DbSet<PluginVersion> PluginVersions => Set<PluginVersion>();

    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();

    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PluginProject>(entity =>
        {
            entity.HasIndex(p => p.UserId);
            entity.HasMany(p => p.Versions)
                .WithOne(v => v.PluginProject)
                .HasForeignKey(v => v.PluginProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PluginVersion>(entity =>
        {
            entity.HasIndex(v => new { v.PluginProjectId, v.RevisionNumber }).IsUnique();
        });

        builder.Entity<CreditAccount>(entity =>
        {
            entity.HasKey(a => a.UserId);
            entity.Property(a => a.Version).IsConcurrencyToken();
            entity.HasOne<IdentityUser>().WithOne().HasForeignKey<CreditAccount>(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_CreditAccounts_Balance_NonNegative", "\"Balance\" >= 0"));
        });

        builder.Entity<CreditTransaction>(entity =>
        {
            entity.HasIndex(t => t.UserId);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
