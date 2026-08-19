using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortex.Core.Migrations
{
    /// <inheritdoc />
    public partial class CostAndFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                table: "messages",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackModel",
                table: "conversations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackProvider",
                table: "conversations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cost",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "FallbackModel",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "FallbackProvider",
                table: "conversations");
        }
    }
}
