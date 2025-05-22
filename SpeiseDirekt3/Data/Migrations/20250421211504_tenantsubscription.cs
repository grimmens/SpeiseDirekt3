using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class tenantsubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes");

            migrationBuilder.CreateTable(
                name: "TenantSubscriptions",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    SubscriptionStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubscriptionEnd = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptions", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_AspNetUsers_TenantId",
                        column: x => x.TenantId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes",
                column: "MenuId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes");

            migrationBuilder.CreateIndex(
                name: "IX_QRCodes_MenuId",
                table: "QRCodes",
                column: "MenuId");
        }
    }
}
