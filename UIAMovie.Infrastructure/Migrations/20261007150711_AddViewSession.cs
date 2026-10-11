using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddViewSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MovieViewSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MovieId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccumulatedSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsCountedAsView = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieViewSessions", x => x.Id);
                    table.CheckConstraint("CK_MovieViewSessions_UserOrGuest", "(\"UserId\" IS NOT NULL AND \"GuestId\" IS NULL) OR (\"UserId\" IS NULL AND \"GuestId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MovieViewSessions_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TvShowViewSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TvShowId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccumulatedSeconds = table.Column<int>(type: "integer", nullable: false),
                    IsCountedAsView = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TvShowViewSessions", x => x.Id);
                    table.CheckConstraint("CK_TvShowViewSessions_UserOrGuest", "(\"UserId\" IS NOT NULL AND \"GuestId\" IS NULL) OR (\"UserId\" IS NULL AND \"GuestId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TvShowViewSessions_TvShows_TvShowId",
                        column: x => x.TvShowId,
                        principalTable: "TvShows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$6DoNV7NNW3ts2XNhqwVG4eHTgu8v6fZW.7V2JSbQIIPZa9fMStnaK");

            migrationBuilder.CreateIndex(
                name: "IX_MovieViewSessions_Counted_Movie_Heartbeat",
                table: "MovieViewSessions",
                columns: new[] { "MovieId", "LastHeartbeatAt" },
                filter: "\"IsCountedAsView\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_MovieViewSessions_Guest_Movie_Date",
                table: "MovieViewSessions",
                columns: new[] { "GuestId", "MovieId", "ViewDate" },
                unique: true,
                filter: "\"GuestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_MovieViewSessions_User_Movie_Date",
                table: "MovieViewSessions",
                columns: new[] { "UserId", "MovieId", "ViewDate" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TvShowViewSessions_Counted_Show_Heartbeat",
                table: "TvShowViewSessions",
                columns: new[] { "TvShowId", "LastHeartbeatAt" },
                filter: "\"IsCountedAsView\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_TvShowViewSessions_Guest_Show_Date",
                table: "TvShowViewSessions",
                columns: new[] { "GuestId", "TvShowId", "ViewDate" },
                unique: true,
                filter: "\"GuestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TvShowViewSessions_User_Show_Date",
                table: "TvShowViewSessions",
                columns: new[] { "UserId", "TvShowId", "ViewDate" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieViewSessions");

            migrationBuilder.DropTable(
                name: "TvShowViewSessions");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$cj4YEeaTN.IVeV7ysuzviOBzTjyrUG5z3yCBUd06M4tnaCNPrfAj.");
        }
    }
}
