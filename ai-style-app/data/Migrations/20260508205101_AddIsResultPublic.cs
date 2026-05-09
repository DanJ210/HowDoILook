using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsResultPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_result_public",
                table: "style_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_result_public",
                table: "style_items");
        }
    }
}
