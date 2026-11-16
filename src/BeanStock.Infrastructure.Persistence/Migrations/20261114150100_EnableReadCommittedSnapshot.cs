using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Alone in its migration on purpose: ALTER DATABASE cannot run in a transaction, so EF Core cannot roll it back
    /// together with other operations. Isolated, a failure leaves nothing half applied.
    /// </remarks>
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // READ_COMMITTED_SNAPSHOT: readers see the last committed version instead of waiting for writers,
            // so a long synchronization transaction never blocks the catalog listing. Already on by default on
            // Azure SQL. ALTER DATABASE cannot run in a transaction, and needs to be the only session in the
            // database: other sessions are disconnected (only once, when the option is off).
            migrationBuilder.Sql(
                """
                IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0
                    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 1
                    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;
                """,
                suppressTransaction: true);
        }
    }
}
