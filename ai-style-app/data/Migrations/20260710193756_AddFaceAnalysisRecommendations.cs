using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceAnalysisRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "face_analysis_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    image_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    gender = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    preferences_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quality_passed = table.Column<bool>(type: "boolean", nullable: true),
                    quality_failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quality_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    feature_vector_json = table.Column<string>(type: "jsonb", nullable: true),
                    analysis_confidence = table.Column<double>(type: "double precision", nullable: true),
                    recommendations_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_analysis_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    selected_style_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    feedback_tags_json = table.Column<string>(type: "jsonb", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_feedback_face_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "face_analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_face_analysis_jobs_status",
                table: "face_analysis_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_face_analysis_jobs_user_id",
                table: "face_analysis_jobs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_feedback_analysis_job_id",
                table: "recommendation_feedback",
                column: "analysis_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_feedback_user_id",
                table: "recommendation_feedback",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_feedback");

            migrationBuilder.DropTable(
                name: "face_analysis_jobs");
        }
    }
}
