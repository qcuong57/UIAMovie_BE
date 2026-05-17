using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBanFieldsToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BanReason",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BannedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "BanReason", "BannedAt", "PasswordHash" },
                values: new object[] { null, null, "$2a$11$rqN2dlHMVV/70PSGyxEBmeqIzWpGFhYvs0A9Dk0hOypV1.6psxIiq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BanReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BannedAt",
                table: "Users");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$vU/i.On9oQ5JixsMdwhXZ.kXbNfpUaYrIwWsAEOfodX3ax5sNJND6");
        }
    }
}
