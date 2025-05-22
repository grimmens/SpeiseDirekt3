using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class fonts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Designcode",
                table: "Menus",
                newName: "Theme");

            migrationBuilder.AddColumn<string>(
                name: "FontFamily",
                table: "Menus",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColor",
                table: "Menus",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColor",
                table: "Menus",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FontFamily",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "SecondaryColor",
                table: "Menus");

            migrationBuilder.RenameColumn(
                name: "Theme",
                table: "Menus",
                newName: "Designcode");
        }
    }
}
