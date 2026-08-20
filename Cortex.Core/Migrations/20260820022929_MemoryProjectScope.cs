using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cortex.Core.Migrations
{
    /// <inheritdoc />
    public partial class MemoryProjectScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "memories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_memories_ProjectId",
                table: "memories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_memories_UserId_ProjectId",
                table: "memories",
                columns: new[] { "UserId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_memories_projects_ProjectId",
                table: "memories",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_memories_projects_ProjectId",
                table: "memories");

            migrationBuilder.DropIndex(
                name: "IX_memories_ProjectId",
                table: "memories");

            migrationBuilder.DropIndex(
                name: "IX_memories_UserId_ProjectId",
                table: "memories");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "memories");
        }
    }
}
