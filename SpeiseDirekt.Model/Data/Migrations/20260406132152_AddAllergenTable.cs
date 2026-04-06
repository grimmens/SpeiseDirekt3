using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SpeiseDirekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllergenTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Allergens",
                table: "MenuItems");

            migrationBuilder.CreateTable(
                name: "Allergens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allergens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuItemAllergen",
                columns: table => new
                {
                    AllergensId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuItemsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemAllergen", x => new { x.AllergensId, x.MenuItemsId });
                    table.ForeignKey(
                        name: "FK_MenuItemAllergen_Allergens_AllergensId",
                        column: x => x.AllergensId,
                        principalTable: "Allergens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemAllergen_MenuItems_MenuItemsId",
                        column: x => x.MenuItemsId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Allergens",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), "A", "Gluten" },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), "B", "Crustaceans" },
                    { new Guid("a0000000-0000-0000-0000-000000000003"), "C", "Eggs" },
                    { new Guid("a0000000-0000-0000-0000-000000000004"), "D", "Fish" },
                    { new Guid("a0000000-0000-0000-0000-000000000005"), "E", "Peanuts" },
                    { new Guid("a0000000-0000-0000-0000-000000000006"), "F", "Soybeans" },
                    { new Guid("a0000000-0000-0000-0000-000000000007"), "G", "Milk" },
                    { new Guid("a0000000-0000-0000-0000-000000000008"), "H", "Nuts" },
                    { new Guid("a0000000-0000-0000-0000-000000000009"), "I", "Celery" },
                    { new Guid("a0000000-0000-0000-0000-00000000000a"), "J", "Mustard" },
                    { new Guid("a0000000-0000-0000-0000-00000000000b"), "K", "Sesame" },
                    { new Guid("a0000000-0000-0000-0000-00000000000c"), "L", "Sulphites" },
                    { new Guid("a0000000-0000-0000-0000-00000000000d"), "M", "Lupin" },
                    { new Guid("a0000000-0000-0000-0000-00000000000e"), "N", "Molluscs" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allergens_Code",
                table: "Allergens",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemAllergen_MenuItemsId",
                table: "MenuItemAllergen",
                column: "MenuItemsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItemAllergen");

            migrationBuilder.DropTable(
                name: "Allergens");

            migrationBuilder.AddColumn<string>(
                name: "Allergens",
                table: "MenuItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
