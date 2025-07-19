using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationUserIdToMenuItemClick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationUserId",
                table: "MenuItemClicks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_Id_ApplicationUserId",
                table: "MenuItemClicks",
                columns: new[] { "Id", "ApplicationUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MenuItemClicks_Id_ApplicationUserId",
                table: "MenuItemClicks");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "MenuItemClicks");
        }
    }
}
