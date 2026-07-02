using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvertisements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advertisements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VideoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CloudinaryPublicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    SkipAfterSeconds = table.Column<int>(type: "integer", nullable: true),
                    ClickThroughUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advertisements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MidRollOffsetSeconds = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdSchedules_Advertisements_AdvertisementId",
                        column: x => x.AdvertisementId,
                        principalTable: "Advertisements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$DEswnRjH8V4Awvb8D7cVKuvdqlB96sTuJOMlnX.5Algi4Q2kRVFxy");

            migrationBuilder.CreateIndex(
                name: "IX_AdSchedules_AdvertisementId",
                table: "AdSchedules",
                column: "AdvertisementId");

            migrationBuilder.CreateIndex(
                name: "IX_AdSchedules_ContentType_ContentId",
                table: "AdSchedules",
                columns: new[] { "ContentType", "ContentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdSchedules");

            migrationBuilder.DropTable(
                name: "Advertisements");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$QQAkKJ1.cud3eTQi5wTkJOVt3KC.iA/D9QmmZjgPtOXplkGjXcUJ.");
        }
    }
}
