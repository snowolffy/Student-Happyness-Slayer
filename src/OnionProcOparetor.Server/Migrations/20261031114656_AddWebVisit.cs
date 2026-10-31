using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebVisit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebVisits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", nullable: false),
                    VisitedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false),
                    PageTitle = table.Column<string>(type: "TEXT", nullable: true),
                    WasBlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: true),
                    StudentFullName = table.Column<string>(type: "TEXT", nullable: true),
                    StudentClassName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebVisits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebVisits_ClientGuid_VisitedAt",
                table: "WebVisits",
                columns: new[] { "ClientGuid", "VisitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebVisits_Domain",
                table: "WebVisits",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_WebVisits_VisitedAt",
                table: "WebVisits",
                column: "VisitedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebVisits");
        }
    }
}
