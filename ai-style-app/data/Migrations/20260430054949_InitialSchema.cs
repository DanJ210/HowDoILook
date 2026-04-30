using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "style_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_style_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "style_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    style_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    job_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    external_prediction_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_style_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_style_jobs_style_items_style_item_id",
                        column: x => x.style_item_id,
                        principalTable: "style_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_style_items_created_at",
                table: "style_items",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_style_items_user_id",
                table: "style_items",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_style_jobs_external_prediction_id",
                table: "style_jobs",
                column: "external_prediction_id");

            migrationBuilder.CreateIndex(
                name: "ix_style_jobs_status",
                table: "style_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_style_jobs_style_item_id",
                table: "style_jobs",
                column: "style_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_style_jobs_user_id",
                table: "style_jobs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "style_jobs");

            migrationBuilder.DropTable(
                name: "style_items");
        }
    }
}
