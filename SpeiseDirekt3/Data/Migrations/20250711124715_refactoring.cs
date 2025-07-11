using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class refactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplexNavigation",
                table: "Menus");

            migrationBuilder.CreateTable(
                name: "TranslationCaches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    TranslatedText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SourceLanguage = table.Column<int>(type: "int", nullable: false),
                    TargetLanguage = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationCaches_CreatedAt",
                table: "TranslationCaches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationCaches_LastUsedAt",
                table: "TranslationCaches",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationCaches_SourceLanguage_TargetLanguage",
                table: "TranslationCaches",
                columns: new[] { "SourceLanguage", "TargetLanguage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranslationCaches");

            migrationBuilder.AddColumn<bool>(
                name: "ComplexNavigation",
                table: "Menus",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
