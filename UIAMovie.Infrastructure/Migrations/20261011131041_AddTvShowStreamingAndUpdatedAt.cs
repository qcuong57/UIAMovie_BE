using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTvShowStreamingAndUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaybackUrl",
                table: "TvShowVideos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "TvShowVideos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TvShowVideos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamingType",
                table: "TvShowVideos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TvShows",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$sBshAlPF1LF4gR/lou2lUeWFswJeJJbfFigPDCst1b8JPHYtuiK1m");

            migrationBuilder.CreateIndex(
                name: "IX_TvShowViewSessions_Counted_ViewDate_Show",
                table: "TvShowViewSessions",
                columns: new[] { "ViewDate", "TvShowId" },
                filter: "\"IsCountedAsView\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_MovieViewSessions_Counted_ViewDate_Movie",
                table: "MovieViewSessions",
                columns: new[] { "ViewDate", "MovieId" },
                filter: "\"IsCountedAsView\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TvShowViewSessions_Counted_ViewDate_Show",
                table: "TvShowViewSessions");

            migrationBuilder.DropIndex(
                name: "IX_MovieViewSessions_Counted_ViewDate_Movie",
                table: "MovieViewSessions");

            migrationBuilder.DropColumn(
                name: "PlaybackUrl",
                table: "TvShowVideos");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "TvShowVideos");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TvShowVideos");

            migrationBuilder.DropColumn(
                name: "StreamingType",
                table: "TvShowVideos");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TvShows");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$FkG7PkhisehrJyEd/1CGkujSxhP6wpV8extDFInDYLOGRjHLRKs2O");
        }
    }
}
