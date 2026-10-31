using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserHardeningSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserHardeningSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BlockExtensions = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockGuestAndIncognito = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockDevTools = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockDnsAndProxy = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockOtherBrowsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserHardeningSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BrowserHardeningSettings",
                columns: new[] { "Id", "BlockDevTools", "BlockExtensions", "BlockGuestAndIncognito", "BlockOtherBrowsers", "LockDnsAndProxy", "UpdatedAt" },
                values: new object[] { 1, true, true, true, false, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrowserHardeningSettings");
        }
    }
}
