using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SpeiseDirekt.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeAllergensPerMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Allergens_Code",
                table: "Allergens");

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000a"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000b"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000c"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000d"));

            migrationBuilder.DeleteData(
                table: "Allergens",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-0000-0000-00000000000e"));

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationUserId",
                table: "Allergens",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "MenuId",
                table: "Allergens",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Allergens_Code_MenuId",
                table: "Allergens",
                columns: new[] { "Code", "MenuId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allergens_MenuId",
                table: "Allergens",
                column: "MenuId");

            migrationBuilder.AddForeignKey(
                name: "FK_Allergens_Menus_MenuId",
                table: "Allergens",
                column: "MenuId",
                principalTable: "Menus",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Allergens_Menus_MenuId",
                table: "Allergens");

            migrationBuilder.DropIndex(
                name: "IX_Allergens_Code_MenuId",
                table: "Allergens");

            migrationBuilder.DropIndex(
                name: "IX_Allergens_MenuId",
                table: "Allergens");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "Allergens");

            migrationBuilder.DropColumn(
                name: "MenuId",
                table: "Allergens");

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
        }
    }
}
