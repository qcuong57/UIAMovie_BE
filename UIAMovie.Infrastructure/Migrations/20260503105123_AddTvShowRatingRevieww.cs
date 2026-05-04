using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTvShowRatingRevieww : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "MovieId",
                table: "RatingReviews",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "EpisodeId",
                table: "RatingReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TvShowId",
                table: "RatingReviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$vU/i.On9oQ5JixsMdwhXZ.kXbNfpUaYrIwWsAEOfodX3ax5sNJND6");

            migrationBuilder.CreateIndex(
                name: "IX_RatingReviews_EpisodeId",
                table: "RatingReviews",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingReviews_TvShowId",
                table: "RatingReviews",
                column: "TvShowId");

            migrationBuilder.AddForeignKey(
                name: "FK_RatingReviews_Episodes_EpisodeId",
                table: "RatingReviews",
                column: "EpisodeId",
                principalTable: "Episodes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RatingReviews_TvShows_TvShowId",
                table: "RatingReviews",
                column: "TvShowId",
                principalTable: "TvShows",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RatingReviews_Episodes_EpisodeId",
                table: "RatingReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_RatingReviews_TvShows_TvShowId",
                table: "RatingReviews");

            migrationBuilder.DropIndex(
                name: "IX_RatingReviews_EpisodeId",
                table: "RatingReviews");

            migrationBuilder.DropIndex(
                name: "IX_RatingReviews_TvShowId",
                table: "RatingReviews");

            migrationBuilder.DropColumn(
                name: "EpisodeId",
                table: "RatingReviews");

            migrationBuilder.DropColumn(
                name: "TvShowId",
                table: "RatingReviews");

            migrationBuilder.AlterColumn<Guid>(
                name: "MovieId",
                table: "RatingReviews",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$L7lHruPJ6pKAOqMiJXCfeO8JcBjBSNDbLSt2jNSY6GzVHVjsZS3hS");
        }
    }
}
