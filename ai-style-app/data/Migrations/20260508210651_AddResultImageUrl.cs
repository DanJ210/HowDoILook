using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResultImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "result_image_url",
                table: "style_jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "result_image_url",
                table: "style_jobs");
        }
    }
}
