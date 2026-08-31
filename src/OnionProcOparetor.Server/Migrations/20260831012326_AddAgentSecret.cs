using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AgentSecretEnrolledAt",
                table: "ClientMachines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentSecretHash",
                table: "ClientMachines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentSecretSalt",
                table: "ClientMachines",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentSecretEnrolledAt",
                table: "ClientMachines");

            migrationBuilder.DropColumn(
                name: "AgentSecretHash",
                table: "ClientMachines");

            migrationBuilder.DropColumn(
                name: "AgentSecretSalt",
                table: "ClientMachines");
        }
    }
}
