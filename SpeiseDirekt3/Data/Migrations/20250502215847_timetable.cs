using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class timetable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QRCodes_Menus_MenuId",
                table: "QRCodes");

            migrationBuilder.DropIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes");

            migrationBuilder.DropColumn(
                name: "FontFamily",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "PrimaryColor",
                table: "Menus");

            migrationBuilder.DropColumn(
                name: "SecondaryColor",
                table: "Menus");

            migrationBuilder.AlterColumn<Guid>(
                name: "MenuId",
                table: "QRCodes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsTimeTableBased",
                table: "QRCodes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TimeTableEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    MenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QRCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTableEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeTableEntries_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimeTableEntries_QRCodes_QRCodeId",
                        column: x => x.QRCodeId,
                        principalTable: "QRCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableEntries_MenuId",
                table: "TimeTableEntries",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeTableEntries_QRCodeId",
                table: "TimeTableEntries",
                column: "QRCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_QRCodes_Menus_MenuId",
                table: "QRCodes",
                column: "MenuId",
                principalTable: "Menus",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QRCodes_Menus_MenuId",
                table: "QRCodes");

            migrationBuilder.DropTable(
                name: "TimeTableEntries");

            migrationBuilder.DropIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes");

            migrationBuilder.DropColumn(
                name: "IsTimeTableBased",
                table: "QRCodes");

            migrationBuilder.AlterColumn<Guid>(
                name: "MenuId",
                table: "QRCodes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes",
                column: "MenuId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QRCodes_Menus_MenuId",
                table: "QRCodes",
                column: "MenuId",
                principalTable: "Menus",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
