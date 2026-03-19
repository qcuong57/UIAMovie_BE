using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRatingReviewUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RatingReviews_UserId_MovieId",
                table: "RatingReviews");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$CR1B8fKoaPP7NH.40fi7tezWTs/fM1wvMg2T1davM/hjCFuETpFLC");

            migrationBuilder.CreateIndex(
                name: "IX_RatingReviews_UserId_MovieId",
                table: "RatingReviews",
                columns: new[] { "UserId", "MovieId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RatingReviews_UserId_MovieId",
                table: "RatingReviews");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$DOA9qr9vwyHFN0LkBkTsqOmXLM.6Lm0W9jVEeE2FTKJ9hRtO2.4Lm");

            migrationBuilder.CreateIndex(
                name: "IX_RatingReviews_UserId_MovieId",
                table: "RatingReviews",
                columns: new[] { "UserId", "MovieId" },
                unique: true);
        }
    }
}
