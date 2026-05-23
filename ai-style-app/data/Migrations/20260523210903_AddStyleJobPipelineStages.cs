using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStyleJobPipelineStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "beard_color",
                table: "style_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "beard_style",
                table: "style_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "current_stage",
                table: "style_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Queued");

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "style_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hair_color",
                table: "style_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "haircut",
                table: "style_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "intermediate_image_url",
                table: "style_jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_beard_stage_pending",
                table: "style_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pipeline_mode",
                table: "style_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "HairOnly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "beard_color",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "beard_style",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "current_stage",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "hair_color",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "haircut",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "intermediate_image_url",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "is_beard_stage_pending",
                table: "style_jobs");

            migrationBuilder.DropColumn(
                name: "pipeline_mode",
                table: "style_jobs");
        }
    }
}
