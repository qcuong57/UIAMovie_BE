using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNestedReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentReplyId",
                table: "ReviewReplies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToUserId",
                table: "ReviewReplies",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$TVaYF0bpESxZVGevVRxMzu1QyAcWWRK1RF1Kdhz.9ozDoWgtx2cx2");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReplies_ParentReplyId",
                table: "ReviewReplies",
                column: "ParentReplyId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewReplies_ReviewReplies_ParentReplyId",
                table: "ReviewReplies",
                column: "ParentReplyId",
                principalTable: "ReviewReplies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReviewReplies_ReviewReplies_ParentReplyId",
                table: "ReviewReplies");

            migrationBuilder.DropIndex(
                name: "IX_ReviewReplies_ParentReplyId",
                table: "ReviewReplies");

            migrationBuilder.DropColumn(
                name: "ParentReplyId",
                table: "ReviewReplies");

            migrationBuilder.DropColumn(
                name: "ReplyToUserId",
                table: "ReviewReplies");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$BqnuEr3Letxefhm9ox9uSu/6ESaLZns5phQVyJOYDrE5eH989WzGC");
        }
    }
}
