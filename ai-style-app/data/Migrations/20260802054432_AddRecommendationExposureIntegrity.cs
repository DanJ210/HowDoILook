using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationExposureIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendation_exposures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    experiment_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    experiment_applied = table.Column<bool>(type: "boolean", nullable: false),
                    telemetry_schema_version = table.Column<int>(type: "integer", nullable: true),
                    telemetry_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    primary_style_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    primary_generation_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_exposures", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_exposures_face_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "face_analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recommendation_exposures_style_jobs_primary_generation_job_~",
                        column: x => x.primary_generation_job_id,
                        principalTable: "style_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_exposure_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exposure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    style_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    style_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recommendation_rank = table.Column<int>(type: "integer", nullable: false),
                    ranking_score = table.Column<double>(type: "double precision", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    was_shown = table.Column<bool>(type: "boolean", nullable: false),
                    shown_order = table.Column<int>(type: "integer", nullable: true),
                    selection_probability = table.Column<double>(type: "double precision", nullable: false),
                    style_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generation_job_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_exposure_candidates", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_exposure_candidates_recommendation_exposures~",
                        column: x => x.exposure_id,
                        principalTable: "recommendation_exposures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recommendation_exposure_candidates_style_items_style_item_id",
                        column: x => x.style_item_id,
                        principalTable: "style_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendation_exposure_candidates_style_jobs_generation_jo~",
                        column: x => x.generation_job_id,
                        principalTable: "style_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_exposure_candidates_generation_job_id",
                table: "recommendation_exposure_candidates",
                column: "generation_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_exposure_candidates_style_item_id",
                table: "recommendation_exposure_candidates",
                column: "style_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_recommendation_exposure_candidates_exposure_style",
                table: "recommendation_exposure_candidates",
                columns: new[] { "exposure_id", "style_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_exposures_created_at",
                table: "recommendation_exposures",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_exposures_primary_generation_job_id",
                table: "recommendation_exposures",
                column: "primary_generation_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_exposures_user_id",
                table: "recommendation_exposures",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_recommendation_exposures_analysis_job_id",
                table: "recommendation_exposures",
                column: "analysis_job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_exposure_candidates");

            migrationBuilder.DropTable(
                name: "recommendation_exposures");
        }
    }
}
