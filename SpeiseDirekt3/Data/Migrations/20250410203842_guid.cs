using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class guid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationUserId",
                table: "MenuItems",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationUserId",
                table: "Categories",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_Id_ApplicationUserId",
                table: "MenuItems",
                columns: new[] { "Id", "ApplicationUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Id_ApplicationUserId",
                table: "Categories",
                columns: new[] { "Id", "ApplicationUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MenuItems_Id_ApplicationUserId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Id_ApplicationUserId",
                table: "Categories");

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationUserId",
                table: "MenuItems",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationUserId",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
        }
    }
}
