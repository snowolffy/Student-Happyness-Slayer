using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomIdToRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoomId",
                table: "WebsiteRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoomId",
                table: "Rules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteRules_RoomId",
                table: "WebsiteRules",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_RoomId",
                table: "Rules",
                column: "RoomId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebsiteRules_RoomId",
                table: "WebsiteRules");

            migrationBuilder.DropIndex(
                name: "IX_Rules_RoomId",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "WebsiteRules");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "Rules");
        }
    }
}
