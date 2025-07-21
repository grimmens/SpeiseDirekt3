using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt3.Migrations
{
    /// <inheritdoc />
    public partial class UpdateExistingMenuItemClicksWithApplicationUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update existing MenuItemClick records to have the correct ApplicationUserId
            // based on the Menu's ApplicationUserId
            migrationBuilder.Sql(@"
                UPDATE mic
                SET mic.ApplicationUserId = m.ApplicationUserId
                FROM MenuItemClicks mic
                INNER JOIN Menus m ON mic.MenuId = m.Id
                WHERE mic.ApplicationUserId = '00000000-0000-0000-0000-000000000000'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
