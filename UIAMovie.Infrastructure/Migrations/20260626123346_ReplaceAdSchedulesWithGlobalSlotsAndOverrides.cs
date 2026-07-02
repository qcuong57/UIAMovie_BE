using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAdSchedulesWithGlobalSlotsAndOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdSchedules");

            migrationBuilder.CreateTable(
                name: "AdContentOverrides",
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
                    table.PrimaryKey("PK_AdContentOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdContentOverrides_Advertisements_AdvertisementId",
                        column: x => x.AdvertisementId,
                        principalTable: "Advertisements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlobalAdSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MidRollOffsetSeconds = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalAdSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalAdSlots_Advertisements_AdvertisementId",
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
                value: "$2a$11$HF0euuVMbVlssmrgPknfI.ANifCXdbE6dJxPvXbhZw9RrGKwHABAK");

            migrationBuilder.CreateIndex(
                name: "IX_AdContentOverrides_AdvertisementId",
                table: "AdContentOverrides",
                column: "AdvertisementId");

            migrationBuilder.CreateIndex(
                name: "IX_AdContentOverrides_ContentType_ContentId",
                table: "AdContentOverrides",
                columns: new[] { "ContentType", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdContentOverrides_ContentType_ContentId_IsActive",
                table: "AdContentOverrides",
                columns: new[] { "ContentType", "ContentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAdSlots_AdvertisementId",
                table: "GlobalAdSlots",
                column: "AdvertisementId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAdSlots_AppliesTo",
                table: "GlobalAdSlots",
                column: "AppliesTo");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalAdSlots_AppliesTo_IsActive",
                table: "GlobalAdSlots",
                columns: new[] { "AppliesTo", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdContentOverrides");

            migrationBuilder.DropTable(
                name: "GlobalAdSlots");

            migrationBuilder.CreateTable(
                name: "AdSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MidRollOffsetSeconds = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
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
    }
}
