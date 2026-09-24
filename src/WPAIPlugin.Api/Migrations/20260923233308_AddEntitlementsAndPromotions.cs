using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WPAIPlugin.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitlementsAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseAmountMinor",
                table: "Purchases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BonusCredits",
                table: "Purchases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PromotionCodeSnapshot",
                table: "Purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "Purchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionNameSnapshot",
                table: "Purchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "CreditAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Historical purchases predate promotions entirely - their real,
            // already-charged price is BaseAmountMinor (equal to AmountMinor,
            // since none of them had a discount). Leaving the column default
            // of 0 for these rows would make every historical purchase look
            // like a 100%-discounted one in reporting.
            migrationBuilder.Sql("UPDATE \"Purchases\" SET \"BaseAmountMinor\" = \"AmountMinor\";");

            migrationBuilder.CreateTable(
                name: "BuildEntitlementAccounts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RemainingBuilds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildEntitlementAccounts", x => x.UserId);
                    table.CheckConstraint("CK_BuildEntitlementAccounts_RemainingBuilds_NonNegative", "\"RemainingBuilds\" >= 0");
                    table.ForeignKey(
                        name: "FK_BuildEntitlementAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresCode = table.Column<bool>(type: "boolean", nullable: false),
                    AppliesToPackId = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "integer", nullable: true),
                    MaxRedemptionsPerUser = table.Column<int>(type: "integer", nullable: true),
                    Eligibility = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildEntitlementTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildEntitlementTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildEntitlementTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildEntitlementTransactions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    BenefitType = table.Column<string>(type: "text", nullable: false),
                    BenefitAmount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PromotionId",
                table: "Purchases",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildEntitlementTransactions_PromotionId",
                table: "BuildEntitlementTransactions",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildEntitlementTransactions_UserId",
                table: "BuildEntitlementTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_PromotionId_UserId",
                table: "PromotionRedemptions",
                columns: new[] { "PromotionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_PurchaseId",
                table: "PromotionRedemptions",
                column: "PurchaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_UserId",
                table: "PromotionRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Code",
                table: "Promotions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_IsEnabled",
                table: "Promotions",
                column: "IsEnabled");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_Promotions_PromotionId",
                table: "Purchases",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_Promotions_PromotionId",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "BuildEntitlementAccounts");

            migrationBuilder.DropTable(
                name: "BuildEntitlementTransactions");

            migrationBuilder.DropTable(
                name: "PromotionRedemptions");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_PromotionId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "BaseAmountMinor",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "BonusCredits",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PromotionCodeSnapshot",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PromotionNameSnapshot",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "CreditAccounts");
        }
    }
}
