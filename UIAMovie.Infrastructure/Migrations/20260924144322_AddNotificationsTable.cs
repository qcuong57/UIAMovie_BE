using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "general"),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$XCXC4xGsDJXbrGtoL87qyeE/cuIwZFZTYq0y3tZk9yHCNY0oHps5y");

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_FirstAirDate",
                table: "TvShows",
                column: "FirstAirDate");

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_ImdbRating",
                table: "TvShows",
                column: "ImdbRating");

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_IsPremium_Status",
                table: "TvShows",
                columns: new[] { "IsPremium", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_OriginCountry",
                table: "TvShows",
                column: "OriginCountry");

            migrationBuilder.CreateIndex(
                name: "IX_TvShows_Status",
                table: "TvShows",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ImdbRating",
                table: "Movies",
                column: "ImdbRating");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_IsPremium_IsPublished",
                table: "Movies",
                columns: new[] { "IsPremium", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_OriginCountry",
                table: "Movies",
                column: "OriginCountry");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ReleaseDate",
                table: "Movies",
                column: "ReleaseDate");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ReleaseDate_ImdbRating",
                table: "Movies",
                columns: new[] { "ReleaseDate", "ImdbRating" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_FirstAirDate",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_ImdbRating",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_IsPremium_Status",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_OriginCountry",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_TvShows_Status",
                table: "TvShows");

            migrationBuilder.DropIndex(
                name: "IX_Movies_ImdbRating",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_IsPremium_IsPublished",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_OriginCountry",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_ReleaseDate",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_ReleaseDate_ImdbRating",
                table: "Movies");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$fCqo8cPTPCRZ.cL3uDTmzOrAvkQmfCG/YayLZWhU.rPmmFks7RiqW");
        }
    }
}
