using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WPAIPlugin.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginProjectsAndVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PluginProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    PluginVersionNumber = table.Column<string>(type: "text", nullable: false),
                    SpecJson = table.Column<string>(type: "text", nullable: false),
                    ArtifactKey = table.Column<string>(type: "text", nullable: false),
                    Validated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PluginVersions_PluginProjects_PluginProjectId",
                        column: x => x.PluginProjectId,
                        principalTable: "PluginProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PluginProjects_UserId",
                table: "PluginProjects",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PluginVersions_PluginProjectId_RevisionNumber",
                table: "PluginVersions",
                columns: new[] { "PluginProjectId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PluginVersions");

            migrationBuilder.DropTable(
                name: "PluginProjects");
        }
    }
}
