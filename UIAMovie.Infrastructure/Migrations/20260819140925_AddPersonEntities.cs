using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UIAMovie.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovieCasts_People_PersonId",
                table: "MovieCasts");

            migrationBuilder.DropForeignKey(
                name: "FK_MovieDirectors_People_PersonId",
                table: "MovieDirectors");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonImages_People_PersonId",
                table: "PersonImages");

            migrationBuilder.DropForeignKey(
                name: "FK_TvShowCasts_People_PersonId",
                table: "TvShowCasts");

            migrationBuilder.DropForeignKey(
                name: "FK_TvShowDirectors_People_PersonId",
                table: "TvShowDirectors");

            migrationBuilder.DropPrimaryKey(
                name: "PK_People",
                table: "People");

            migrationBuilder.RenameTable(
                name: "People",
                newName: "Persons");

            migrationBuilder.RenameIndex(
                name: "IX_People_TmdbPersonId",
                table: "Persons",
                newName: "IX_Persons_TmdbPersonId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Persons",
                table: "Persons",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$fCqo8cPTPCRZ.cL3uDTmzOrAvkQmfCG/YayLZWhU.rPmmFks7RiqW");

            migrationBuilder.AddForeignKey(
                name: "FK_MovieCasts_Persons_PersonId",
                table: "MovieCasts",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MovieDirectors_Persons_PersonId",
                table: "MovieDirectors",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonImages_Persons_PersonId",
                table: "PersonImages",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TvShowCasts_Persons_PersonId",
                table: "TvShowCasts",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TvShowDirectors_Persons_PersonId",
                table: "TvShowDirectors",
                column: "PersonId",
                principalTable: "Persons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovieCasts_Persons_PersonId",
                table: "MovieCasts");

            migrationBuilder.DropForeignKey(
                name: "FK_MovieDirectors_Persons_PersonId",
                table: "MovieDirectors");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonImages_Persons_PersonId",
                table: "PersonImages");

            migrationBuilder.DropForeignKey(
                name: "FK_TvShowCasts_Persons_PersonId",
                table: "TvShowCasts");

            migrationBuilder.DropForeignKey(
                name: "FK_TvShowDirectors_Persons_PersonId",
                table: "TvShowDirectors");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Persons",
                table: "Persons");

            migrationBuilder.RenameTable(
                name: "Persons",
                newName: "People");

            migrationBuilder.RenameIndex(
                name: "IX_Persons_TmdbPersonId",
                table: "People",
                newName: "IX_People_TmdbPersonId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_People",
                table: "People",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "PasswordHash",
                value: "$2a$11$YKQxLzVT0JkAbdWPse0U/uEp1Ue7ECCt03LArVRRL289PhLgYwZva");

            migrationBuilder.AddForeignKey(
                name: "FK_MovieCasts_People_PersonId",
                table: "MovieCasts",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MovieDirectors_People_PersonId",
                table: "MovieDirectors",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonImages_People_PersonId",
                table: "PersonImages",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TvShowCasts_People_PersonId",
                table: "TvShowCasts",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TvShowDirectors_People_PersonId",
                table: "TvShowDirectors",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
