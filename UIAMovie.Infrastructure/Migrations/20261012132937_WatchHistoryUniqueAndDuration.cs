using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WatchHistoryUniqueAndDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "TvShowWatchHistory",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$nRQXMT4cCCTS8xBQMWv07O2ykmsW7Xzhe7EbNKQp1DDDpoRBI6BdG");

            migrationBuilder.CreateIndex(
                name: "IX_TvShowWatchHistories_UserId_TvShowId_ShowLevel",
                table: "TvShowWatchHistory",
                columns: new[] { "UserId", "TvShowId" },
                unique: true,
                filter: "\"EpisodeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TvShowWatchHistory_UserId_TvShowId_EpisodeId",
                table: "TvShowWatchHistory",
                columns: new[] { "UserId", "TvShowId", "EpisodeId" },
                unique: true,
                filter: "\"EpisodeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TvShowWatchHistories_UserId_TvShowId_ShowLevel",
                table: "TvShowWatchHistory");

            migrationBuilder.DropIndex(
                name: "IX_TvShowWatchHistory_UserId_TvShowId_EpisodeId",
                table: "TvShowWatchHistory");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "TvShowWatchHistory");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$sBshAlPF1LF4gR/lou2lUeWFswJeJJbfFigPDCst1b8JPHYtuiK1m");
        }
    }
}
