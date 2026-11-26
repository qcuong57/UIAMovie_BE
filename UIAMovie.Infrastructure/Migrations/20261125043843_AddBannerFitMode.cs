using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerFitMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FitMode",
                table: "Banners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "contain");

            migrationBuilder.AddColumn<int>(
                name: "MaxHeight",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$PNGXAuOJ6qjE96Vp6MBbe.ywIyuykIFdf4H5uwkt23fj/Z2X36KD.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FitMode",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "MaxHeight",
                table: "Banners");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$fl8D4cVg5eQUe/wdW1M54eaxL9f1HR.dMPyMU3bF7FDoPnhX.l8U2");
        }
    }
}
