using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt.Data.Migrations
{
    /// <summary>
    /// AP14, data only (no schema change): a tenant has two roles now — the owner (the account that registered, no
    /// <c>TenantUsers</c> row) and managers. Every team member becomes a manager (<c>Role</c> 1; 0, 2, 3 and 4 were
    /// general manager, cashier, employee and driver), and the per-member permission overrides are cleared
    /// (<c>Permissions</c> 0 = none; the column is no longer read — permissions follow the role).
    /// </summary>
    public partial class TeamRolesOwnerAndManager : Migration
    {
        /// <summary>The statement of <see cref="Up"/>; plain SQL that SQL Server and SQLite both run.</summary>
        public const string UpSql = "UPDATE [TenantUsers] SET [Role] = 1, [Permissions] = 0;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }

        /// <summary>
        /// Deliberately empty: the former roles and permission overrides are not kept anywhere, so they cannot be
        /// restored (a rollback restores the backup the deploy takes before migrating). The rows stay managers, which the
        /// code before AP14 reads as "Manager" with the role's default permissions.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
