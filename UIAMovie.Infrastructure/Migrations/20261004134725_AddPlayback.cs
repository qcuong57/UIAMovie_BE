using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Duration",
                table: "WatchHistories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressSeconds",
                table: "WatchHistories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PlaybackUrl",
                table: "MovieVideos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "MovieVideos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "MovieVideos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamingType",
                table: "MovieVideos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaybackUrl",
                table: "Episodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Episodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Episodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Quality",
                table: "Episodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamingType",
                table: "Episodes",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$cj4YEeaTN.IVeV7ysuzviOBzTjyrUG5z3yCBUd06M4tnaCNPrfAj.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "WatchHistories");

            migrationBuilder.DropColumn(
                name: "ProgressSeconds",
                table: "WatchHistories");

            migrationBuilder.DropColumn(
                name: "PlaybackUrl",
                table: "MovieVideos");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "MovieVideos");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "MovieVideos");

            migrationBuilder.DropColumn(
                name: "StreamingType",
                table: "MovieVideos");

            migrationBuilder.DropColumn(
                name: "PlaybackUrl",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Quality",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "StreamingType",
                table: "Episodes");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$TVaYF0bpESxZVGevVRxMzu1QyAcWWRK1RF1Kdhz.9ozDoWgtx2cx2");
        }
    }
}
