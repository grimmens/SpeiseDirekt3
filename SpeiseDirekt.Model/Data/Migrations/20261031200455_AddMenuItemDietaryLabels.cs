using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemDietaryLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSpicy",
                table: "MenuItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVegan",
                table: "MenuItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVegetarian",
                table: "MenuItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSpicy",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "IsVegan",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "IsVegetarian",
                table: "MenuItems");
        }
    }
}
