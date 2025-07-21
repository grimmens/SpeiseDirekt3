using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class TrackingSystemFixed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MenuItemClicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemClicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemClicks_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuItemClicks_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MenuViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QRCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuViews_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MenuViews_QRCodes_QRCodeId",
                        column: x => x.QRCodeId,
                        principalTable: "QRCodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_ClickedAt",
                table: "MenuItemClicks",
                column: "ClickedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_MenuId",
                table: "MenuItemClicks",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_MenuItemId",
                table: "MenuItemClicks",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_SessionId",
                table: "MenuItemClicks",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemClicks_SessionId_MenuItemId_ClickedAt",
                table: "MenuItemClicks",
                columns: new[] { "SessionId", "MenuItemId", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuViews_MenuId",
                table: "MenuViews",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuViews_QRCodeId",
                table: "MenuViews",
                column: "QRCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuViews_SessionId",
                table: "MenuViews",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuViews_SessionId_MenuId_ViewedAt",
                table: "MenuViews",
                columns: new[] { "SessionId", "MenuId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuViews_ViewedAt",
                table: "MenuViews",
                column: "ViewedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuItemClicks");

            migrationBuilder.DropTable(
                name: "MenuViews");
        }
    }
}
