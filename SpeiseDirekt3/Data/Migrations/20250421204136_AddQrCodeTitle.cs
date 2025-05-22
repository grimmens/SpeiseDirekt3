using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class AddQrCodeTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "QRCodes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "QRCodes");
        }
    }
}
