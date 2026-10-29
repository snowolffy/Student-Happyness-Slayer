using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceCatalogId",
                table: "WebsiteRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebsiteCatalogSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxEntries = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncEntryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteCatalogSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteRules_UrlPattern",
                table: "WebsiteRules",
                column: "UrlPattern",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteCatalogSources");

            migrationBuilder.DropIndex(
                name: "IX_WebsiteRules_UrlPattern",
                table: "WebsiteRules");

            migrationBuilder.DropColumn(
                name: "SourceCatalogId",
                table: "WebsiteRules");
        }
    }
}
