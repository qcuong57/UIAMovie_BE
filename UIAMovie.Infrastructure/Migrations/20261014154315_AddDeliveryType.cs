using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryType",
                table: "MovieVideos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryType",
                table: "Episodes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$HO4U7Si8Gk3OIpyvKnOD/.ugLd7UDZBAE52iWt5CZUxc07A.pIiU.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryType",
                table: "MovieVideos");

            migrationBuilder.DropColumn(
                name: "DeliveryType",
                table: "Episodes");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$nRQXMT4cCCTS8xBQMWv07O2ykmsW7Xzhe7EbNKQp1DDDpoRBI6BdG");
        }
    }
}
