using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiStyleApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStyleItemAnalysisJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "analysis_job_id",
                table: "style_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_style_items_analysis_job_id",
                table: "style_items",
                column: "analysis_job_id");

            migrationBuilder.AddForeignKey(
                name: "FK_style_items_face_analysis_jobs_analysis_job_id",
                table: "style_items",
                column: "analysis_job_id",
                principalTable: "face_analysis_jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_style_items_face_analysis_jobs_analysis_job_id",
                table: "style_items");

            migrationBuilder.DropIndex(
                name: "ix_style_items_analysis_job_id",
                table: "style_items");

            migrationBuilder.DropColumn(
                name: "analysis_job_id",
                table: "style_items");
        }
    }
}
