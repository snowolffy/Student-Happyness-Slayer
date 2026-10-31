using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInAndRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StudentCode",
                table: "LogEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    RetentionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: true),
                    LastBackupAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastBackupSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    LastBackupFileName = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignInSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequireLoginMode = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireLoginRoomIdsCsv = table.Column<string>(type: "TEXT", nullable: true),
                    GuestModeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmergencyCodeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordMinLength = table.Column<int>(type: "INTEGER", nullable: false),
                    ForceChangePasswordOnFirstLogin = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutFailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MultiDeviceMode = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoLogoutIdleMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoutOnShutdown = table.Column<bool>(type: "INTEGER", nullable: false),
                    TeacherCanResetPassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowStudentNameInConsole = table.Column<bool>(type: "INTEGER", nullable: false),
                    LoginScreenMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LockScreenDefaultMessage = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StudentCode = table.Column<string>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    SchoolEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ClassName = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StudentId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MachineName = table.Column<string>(type: "TEXT", nullable: false),
                    RoomId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSessions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BackupSettings",
                columns: new[] { "Id", "Frequency", "LastBackupAt", "LastBackupFileName", "LastBackupSizeBytes", "RetentionCount", "StoragePath", "UpdatedAt" },
                values: new object[] { 1, 0, null, null, null, 14, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "SignInSettings",
                columns: new[] { "Id", "AutoLogoutIdleMinutes", "EmergencyCodeEnabled", "ForceChangePasswordOnFirstLogin", "GuestModeEnabled", "LockScreenDefaultMessage", "LockoutFailedAttempts", "LoginScreenMessage", "LogoutOnShutdown", "MultiDeviceMode", "PasswordMinLength", "RequireLoginMode", "RequireLoginRoomIdsCsv", "ShowStudentNameInConsole", "TeacherCanResetPassword", "UpdatedAt" },
                values: new object[] { 1, 15, true, true, false, "กรุณาละสายตาจากหน้าจอและฟังครูผู้สอน", 5, null, true, 0, 6, 0, null, true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_Students_ClassName",
                table: "Students",
                column: "ClassName");

            migrationBuilder.CreateIndex(
                name: "IX_Students_SchoolEmail",
                table: "Students",
                column: "SchoolEmail",
                unique: true,
                filter: "[SchoolEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Students_StudentCode",
                table: "Students",
                column: "StudentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSessions_ClientGuid_EndedAt",
                table: "StudentSessions",
                columns: new[] { "ClientGuid", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentSessions_StartedAt",
                table: "StudentSessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSessions_StudentId_EndedAt",
                table: "StudentSessions",
                columns: new[] { "StudentId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropTable(
                name: "SignInSettings");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropTable(
                name: "StudentSessions");

            migrationBuilder.DropColumn(
                name: "StudentCode",
                table: "LogEntries");
        }
    }
}
