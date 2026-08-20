using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortex.Core.Migrations
{
    /// <inheritdoc />
    public partial class MessageReasoning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reasoning",
                table: "messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reasoning",
                table: "messages");
        }
    }
}
