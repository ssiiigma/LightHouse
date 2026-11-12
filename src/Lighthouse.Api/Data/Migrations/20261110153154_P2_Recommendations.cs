using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lighthouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class P2_Recommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAtUtc",
                table: "RecommendationFeedbacks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Reward",
                table: "RecommendationFeedbacks",
                type: "float",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModelTrainingAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModelVersion = table.Column<int>(type: "int", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    AverageReward = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelTrainingAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationFeedbacks_UserId_RecommendationKey",
                table: "RecommendationFeedbacks",
                columns: new[] { "UserId", "RecommendationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationFeedbacks_UserId_TaskId",
                table: "RecommendationFeedbacks",
                columns: new[] { "UserId", "TaskId" },
                unique: true,
                filter: "[RespondedAtUtc] IS NULL AND [TaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelTrainingAudits_RunAtUtc",
                table: "ModelTrainingAudits",
                column: "RunAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelTrainingAudits");

            migrationBuilder.DropIndex(
                name: "IX_RecommendationFeedbacks_UserId_RecommendationKey",
                table: "RecommendationFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_RecommendationFeedbacks_UserId_TaskId",
                table: "RecommendationFeedbacks");

            migrationBuilder.DropColumn(
                name: "RespondedAtUtc",
                table: "RecommendationFeedbacks");

            migrationBuilder.DropColumn(
                name: "Reward",
                table: "RecommendationFeedbacks");
        }
    }
}
