using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddExamModeAndAllowListPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllowListPresetDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PresetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowListPresetDomains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllowListPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowListPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoomId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClientGuidsCsv = table.Column<string>(type: "TEXT", nullable: true),
                    PresetIdsCsv = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraDomainsCsv = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedReason = table.Column<string>(type: "TEXT", nullable: true),
                    StartedByUsername = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AllowListPresetDomains",
                columns: new[] { "Id", "Domain", "PresetId" },
                values: new object[,]
                {
                    { 1, "docs.google.com", 1 },
                    { 2, "forms.gle", 1 },
                    { 3, "accounts.google.com", 1 },
                    { 4, "ssl.gstatic.com", 1 },
                    { 5, "www.gstatic.com", 1 },
                    { 6, "fonts.googleapis.com", 1 },
                    { 7, "lh3.googleusercontent.com", 1 },
                    { 8, "classroom.google.com", 2 },
                    { 9, "drive.google.com", 2 },
                    { 10, "docs.google.com", 2 },
                    { 11, "accounts.google.com", 2 },
                    { 12, "ssl.gstatic.com", 2 },
                    { 13, "www.gstatic.com", 2 },
                    { 14, "fonts.googleapis.com", 2 },
                    { 15, "lh3.googleusercontent.com", 2 },
                    { 16, "ogs.google.com", 2 },
                    { 17, "quizizz.com", 3 },
                    { 18, "cdn.quizizz.com", 3 },
                    { 19, "wayground.com", 3 },
                    { 20, "cdn.wayground.com", 3 },
                    { 21, "kahoot.it", 4 },
                    { 22, "kahoot.com", 4 },
                    { 23, "akamaized.net", 4 },
                    { 24, "scratch.mit.edu", 5 },
                    { 25, "assets.scratch.mit.edu", 5 },
                    { 26, "cdn.scratch.mit.edu", 5 },
                    { 27, "canva.com", 6 },
                    { 28, "www.canva.com", 6 },
                    { 29, "static.canva.com", 6 },
                    { 30, "media-public.canva.com", 6 },
                    { 31, "app-api.canva.com", 6 }
                });

            migrationBuilder.InsertData(
                table: "AllowListPresets",
                columns: new[] { "Id", "CreatedAt", "IsBuiltIn", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Google Forms", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Google Classroom", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Quizizz / Wayground", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Kahoot", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Scratch", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Canva", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowListPresetDomains_PresetId",
                table: "AllowListPresetDomains",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_EndedAt",
                table: "ExamSessions",
                column: "EndedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowListPresetDomains");

            migrationBuilder.DropTable(
                name: "AllowListPresets");

            migrationBuilder.DropTable(
                name: "ExamSessions");
        }
    }
}
