using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuTranslationGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TranslationGroupId",
                table: "Menus",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menus_TranslationGroupId_Language",
                table: "Menus",
                columns: new[] { "TranslationGroupId", "Language" },
                unique: true,
                filter: "[TranslationGroupId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Menus_TranslationGroupId_Language",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "TranslationGroupId",
                table: "Menus");
        }
    }
}
