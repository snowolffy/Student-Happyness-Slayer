using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations.ActivityLog
{
    /// <inheritdoc />
    public partial class AddActorDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorDisplayName",
                table: "ActivityLogEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorDisplayName",
                table: "ActivityLogEntries");
        }
    }
}
