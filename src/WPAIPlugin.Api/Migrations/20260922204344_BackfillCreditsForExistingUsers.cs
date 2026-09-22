using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WPAIPlugin.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCreditsForExistingUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only newly inserted accounts receive a ledger grant. The CTE
            // keeps both writes in one statement, inside the EF migration transaction.
            migrationBuilder.Sql("""
                WITH new_accounts AS (
                    INSERT INTO "CreditAccounts" ("UserId", "Balance", "UpdatedAtUtc", "Version")
                    SELECT "Id", 100, CURRENT_TIMESTAMP, 0 FROM "AspNetUsers" u
                    WHERE NOT EXISTS (SELECT 1 FROM "CreditAccounts" a WHERE a."UserId" = u."Id")
                    RETURNING "UserId"
                )
                INSERT INTO "CreditTransactions" ("Id", "UserId", "Amount", "Type", "Reference", "CreatedAtUtc")
                SELECT gen_random_uuid(), "UserId", 100, 'SignupGrant',
                    'signup:' || gen_random_uuid()::text, CURRENT_TIMESTAMP FROM new_accounts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Grants may already have been spent; never rewrite ledger history.
            throw new NotSupportedException("Credit backfill cannot be reversed. Use a forward migration.");
        }
    }
}
