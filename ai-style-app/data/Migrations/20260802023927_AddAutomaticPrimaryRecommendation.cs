using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticPrimaryRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "primary_generation_job_id",
                table: "face_analysis_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_style_id",
                table: "face_analysis_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "primary_style_item_id",
                table: "face_analysis_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_face_analysis_jobs_primary_generation_job_id",
                table: "face_analysis_jobs",
                column: "primary_generation_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_face_analysis_jobs_primary_style_item_id",
                table: "face_analysis_jobs",
                column: "primary_style_item_id");

            migrationBuilder.AddForeignKey(
                name: "FK_face_analysis_jobs_style_items_primary_style_item_id",
                table: "face_analysis_jobs",
                column: "primary_style_item_id",
                principalTable: "style_items",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_face_analysis_jobs_style_jobs_primary_generation_job_id",
                table: "face_analysis_jobs",
                column: "primary_generation_job_id",
                principalTable: "style_jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_face_analysis_jobs_style_items_primary_style_item_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_face_analysis_jobs_style_jobs_primary_generation_job_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropIndex(
                name: "ix_face_analysis_jobs_primary_generation_job_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropIndex(
                name: "ix_face_analysis_jobs_primary_style_item_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropColumn(
                name: "primary_generation_job_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropColumn(
                name: "primary_style_id",
                table: "face_analysis_jobs");

            migrationBuilder.DropColumn(
                name: "primary_style_item_id",
                table: "face_analysis_jobs");
        }
    }
}
