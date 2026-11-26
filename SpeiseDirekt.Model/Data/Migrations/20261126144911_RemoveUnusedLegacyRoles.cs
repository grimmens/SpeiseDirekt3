using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeiseDirekt.Data.Migrations
{
    /// <summary>
    /// Data only (no schema change; the model snapshot is unchanged): deletes the six Identity roles of the former
    /// delivery-platform plan — <c>Manager</c>, <c>Cashier</c>, <c>Driver</c>, <c>Employee</c>, <c>Customer</c>,
    /// <c>GeneralManager</c> — which no code references and production assigns to nobody. Only a role without any
    /// <c>AspNetUserRoles</c> row and without any <c>AspNetRoleClaims</c> row is deleted; every other role stays, the new
    /// <c>GlobalAdmin</c> and <c>PaidTenant</c> (created by <c>RoleSetup</c> at startup, not here) included.
    /// </summary>
    public partial class RemoveUnusedLegacyRoles : Migration
    {
        /// <summary>
        /// The statement of <see cref="Up"/>. Written for SQL Server; plain enough that SQLite runs it too (the API tests
        /// check it there; E2E and the API tests create their schema with <c>EnsureCreated</c>, not with migrations). The
        /// names are matched case-insensitively on <c>Name</c> and on <c>NormalizedName</c> (either may be what a seed
        /// wrote).
        /// </summary>
        public const string UpSql =
            "DELETE FROM [AspNetRoles] " +
            "WHERE (UPPER([Name]) IN ('MANAGER', 'CASHIER', 'DRIVER', 'EMPLOYEE', 'CUSTOMER', 'GENERALMANAGER') " +
            "OR UPPER([NormalizedName]) IN ('MANAGER', 'CASHIER', 'DRIVER', 'EMPLOYEE', 'CUSTOMER', 'GENERALMANAGER')) " +
            "AND NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] WHERE [AspNetUserRoles].[RoleId] = [AspNetRoles].[Id]) " +
            "AND NOT EXISTS (SELECT 1 FROM [AspNetRoleClaims] WHERE [AspNetRoleClaims].[RoleId] = [AspNetRoles].[Id]);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }

        /// <summary>
        /// Deliberately empty: the deleted roles were unused (no user, no claim), so there is nothing to restore. A
        /// rollback restores the backup the deploy takes before migrating.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
