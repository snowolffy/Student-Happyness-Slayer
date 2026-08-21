using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations.ActivityLog
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ActorAdminUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorUsername = table.Column<string>(type: "TEXT", nullable: true),
                    TargetClientGuid = table.Column<string>(type: "TEXT", nullable: true),
                    TargetMachineName = table.Column<string>(type: "TEXT", nullable: true),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_ActorAdminUserId_Timestamp",
                table: "ActivityLogEntries",
                columns: new[] { "ActorAdminUserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_Category_Timestamp",
                table: "ActivityLogEntries",
                columns: new[] { "Category", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_TargetClientGuid_Timestamp",
                table: "ActivityLogEntries",
                columns: new[] { "TargetClientGuid", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_Timestamp",
                table: "ActivityLogEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogEntries");
        }
    }
}
