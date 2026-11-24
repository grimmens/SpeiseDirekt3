using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt.Data.Migrations
{
    /// <summary>
    /// AP15a: the guest analytics keep no IP address and no user agent any more. Drops <c>IpAddress</c> and <c>UserAgent</c>
    /// of <c>MenuViews</c> and <c>MenuItemClicks</c> (nothing read them; the analytics group by <c>SessionId</c>, which is now
    /// the daily visitor key and keeps its old cookie GUIDs in existing rows). <see cref="Down"/> re-adds the four columns,
    /// nullable and empty: the dropped values are gone (a rollback restores the backup the deploy takes before migrating).
    /// </summary>
    public partial class RemoveGuestTrackingIpAndUserAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "MenuViews");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "MenuViews");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "MenuItemClicks");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "MenuItemClicks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "MenuViews",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "MenuViews",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "MenuItemClicks",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "MenuItemClicks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
