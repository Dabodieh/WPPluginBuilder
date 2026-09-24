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

    public DbSet<AiUsageEvent> AiUsageEvents => Set<AiUsageEvent>();

    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<ProcessedPaymentEvent> ProcessedPaymentEvents => Set<ProcessedPaymentEvent>();

    public DbSet<BuildEntitlementAccount> BuildEntitlementAccounts => Set<BuildEntitlementAccount>();

    public DbSet<BuildEntitlementTransaction> BuildEntitlementTransactions => Set<BuildEntitlementTransaction>();

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();

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

        builder.Entity<AiUsageEvent>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => new { e.Provider, e.Model });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AdminAuditLog>(entity =>
        {
            entity.HasIndex(e => e.AdminUserId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => new { e.TargetType, e.TargetId });
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(e => e.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Purchase>(entity =>
        {
            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.CreatedAtUtc);
            entity.HasIndex(p => p.ProviderCheckoutSessionId).IsUnique();
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Promotion>().WithMany().HasForeignKey(p => p.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProcessedPaymentEvent>(entity =>
        {
            entity.HasKey(e => e.ProviderEventId);
        });

        builder.Entity<BuildEntitlementAccount>(entity =>
        {
            entity.HasKey(a => a.UserId);
            entity.Property(a => a.Version).IsConcurrencyToken();
            entity.HasOne<IdentityUser>().WithOne().HasForeignKey<BuildEntitlementAccount>(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_BuildEntitlementAccounts_RemainingBuilds_NonNegative", "\"RemainingBuilds\" >= 0"));
        });

        builder.Entity<BuildEntitlementTransaction>(entity =>
        {
            entity.HasIndex(t => t.UserId);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Promotion>().WithMany().HasForeignKey(t => t.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Promotion>(entity =>
        {
            // Postgres unique indexes treat NULL as distinct, so multiple
            // no-code (automatic) promotions coexist fine alongside this.
            entity.HasIndex(p => p.Code).IsUnique();
            entity.HasIndex(p => p.IsEnabled);
        });

        builder.Entity<PromotionRedemption>(entity =>
        {
            entity.HasIndex(r => new { r.PromotionId, r.UserId });
            // One redemption per Purchase, at most - guards the webhook path
            // at the database level, not just in application code.
            entity.HasIndex(r => r.PurchaseId).IsUnique();
            entity.HasOne<Promotion>().WithMany().HasForeignKey(r => r.PromotionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Purchase>().WithMany().HasForeignKey(r => r.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
