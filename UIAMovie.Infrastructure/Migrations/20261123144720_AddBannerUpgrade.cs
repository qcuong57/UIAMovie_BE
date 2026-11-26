using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BannerId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Banners",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "PlanId",
                table: "Banners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "Banners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "Banners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "All");

            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "Banners",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BadgeText",
                table: "Banners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CtaText",
                table: "Banners",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageHeight",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageWidth",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MobileImageHeight",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MobileImageWidth",
                table: "Banners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OpenInNewTab",
                table: "Banners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPlanPrice",
                table: "Banners",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Subtitle",
                table: "Banners",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BannerEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BannerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannerEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BannerEvents_Banners_BannerId",
                        column: x => x.BannerId,
                        principalTable: "Banners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$fl8D4cVg5eQUe/wdW1M54eaxL9f1HR.dMPyMU3bF7FDoPnhX.l8U2");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_BannerId",
                table: "PaymentOrders",
                column: "BannerId");

            migrationBuilder.CreateIndex(
                name: "IX_Banners_PlanId",
                table: "Banners",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BannerEvents_BannerId_Type_CreatedAt",
                table: "BannerEvents",
                columns: new[] { "BannerId", "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannerEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_BannerId",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_Banners_PlanId",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "BannerId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "BackgroundColor",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "BadgeText",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "CtaText",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "ImageHeight",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "ImageWidth",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "MobileImageHeight",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "MobileImageWidth",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "OpenInNewTab",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "ShowPlanPrice",
                table: "Banners");

            migrationBuilder.DropColumn(
                name: "Subtitle",
                table: "Banners");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Banners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "PlanId",
                table: "Banners",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$ADAB8gsqNEpmKxiP0N6yU.MCRbE5rkICXlTNjngwqI9Ly2vLuJF1e");
        }
    }
}
