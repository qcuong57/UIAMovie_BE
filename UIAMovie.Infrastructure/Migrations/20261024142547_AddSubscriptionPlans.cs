using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PriceVnd = table.Column<long>(type: "bigint", nullable: false),
                    OriginalPriceVnd = table.Column<long>(type: "bigint", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    IsMostPopular = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    FeaturesJson = table.Column<string>(type: "text", nullable: false),
                    IsTrial = table.Column<bool>(type: "boolean", nullable: false),
                    MaxPerUser = table.Column<int>(type: "integer", nullable: true),
                    AllowedEmailDomains = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "AllowedEmailDomains", "CreatedAt", "Description", "DurationDays", "EndsAt", "FeaturesJson", "IsActive", "IsMostPopular", "IsTrial", "MaxPerUser", "Name", "OriginalPriceVnd", "PlanId", "PriceVnd", "SortOrder", "StartsAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-0000000000a1"), null, new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc), "Xem không giới hạn trong 30 ngày", 30, null, "[\"Full HD / 4K\",\"Không quảng cáo\",\"Tải về xem offline\"]", true, false, false, null, "Premium Tháng", null, "monthly_premium", 69000L, 1, null, new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-0000-0000-0000000000a2"), null, new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc), "Tiết kiệm hơn 28% so với gói tháng", 365, null, "[\"Full HD / 4K\",\"Không quảng cáo\",\"Tải về xem offline\",\"Ưu tiên hỗ trợ\"]", true, true, false, null, "Premium Năm", null, "yearly_premium", 599000L, 2, null, new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$0XYQ08Vhaw9sNMXfK68a..mBfLL9tSBqE.i4pYX7GgRntzGsFkePe");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_IsActive_SortOrder",
                table: "SubscriptionPlans",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_PlanId",
                table: "SubscriptionPlans",
                column: "PlanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$KmcGhecGUZg2tA5ouPcqUO0ZmTVLYfxgFWoBqv.JQnd8WQqNZJd4.");
        }
    }
}
