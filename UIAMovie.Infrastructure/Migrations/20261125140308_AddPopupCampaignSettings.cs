using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPopupCampaignSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbGroup",
                table: "Banners",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Device",
                table: "Banners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "all");

            migrationBuilder.AddColumn<int>(
                name: "DismissCooldownHours",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayDelaySeconds",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InteractionCooldownHours",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxShows",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShowIntervalHours",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetGenres",
                table: "Banners",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPages",
                table: "Banners",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$1FQfQA1igD0wIstPFUwKyOTc8pbtSHYJM7oNKR6ZcWkOldwOjqXX2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbGroup",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Device",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "DismissCooldownHours",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "DisplayDelaySeconds",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "InteractionCooldownHours",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "MaxShows",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "ShowIntervalHours",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "TargetGenres",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "TargetPages",
                table: "Banners");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$7pyRRkyyj4.MAfRLOoGd..cRia1.10Kqlq.mo9YOK3K9PKFX2Hg0y");
        }
    }
}
