using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandExecutionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchId",
                table: "ClientCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutedAt",
                table: "ClientCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMessage",
                table: "ClientCommands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionStatus",
                table: "ClientCommands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ClientCommands_BatchId",
                table: "ClientCommands",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCommands_ExecutionStatus_CreatedAt",
                table: "ClientCommands",
                columns: new[] { "ExecutionStatus", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientCommands_BatchId",
                table: "ClientCommands");

            migrationBuilder.DropIndex(
                name: "IX_ClientCommands_ExecutionStatus_CreatedAt",
                table: "ClientCommands");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "ClientCommands");

            migrationBuilder.DropColumn(
                name: "ExecutedAt",
                table: "ClientCommands");

            migrationBuilder.DropColumn(
                name: "ExecutionMessage",
                table: "ClientCommands");

            migrationBuilder.DropColumn(
                name: "ExecutionStatus",
                table: "ClientCommands");
        }
    }
}
