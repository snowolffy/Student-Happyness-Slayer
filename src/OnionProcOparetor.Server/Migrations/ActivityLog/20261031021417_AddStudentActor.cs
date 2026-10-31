using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations.ActivityLog
{
    /// <inheritdoc />
    public partial class AddStudentActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorStudentCode",
                table: "ActivityLogEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActorStudentId",
                table: "ActivityLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_ActorStudentId_Timestamp",
                table: "ActivityLogEntries",
                columns: new[] { "ActorStudentId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_ActorStudentId_Timestamp",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "ActorStudentCode",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "ActorStudentId",
                table: "ActivityLogEntries");
        }
    }
}
