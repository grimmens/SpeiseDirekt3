using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class complexnavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ComplexNavigation",
                table: "Menus",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplexNavigation",
                table: "Menus");
        }
    }
}
