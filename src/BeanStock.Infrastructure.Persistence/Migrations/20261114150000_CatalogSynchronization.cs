using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The date now changes only when the product actually changes (see Product.LastUpdatedAt).
            migrationBuilder.RenameColumn(
                name: "LastSynchronizedAt",
                table: "Products",
                newName: "LastUpdatedAt");

            migrationBuilder.CreateTable(
                name: "CatalogSyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderProductCount = table.Column<int>(type: "int", nullable: false),
                    AddedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    ReactivatedCount = table.Column<int>(type: "int", nullable: false),
                    DeactivatedCount = table.Column<int>(type: "int", nullable: false),
                    UnchangedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSyncRuns_Status_CompletedAt",
                table: "CatalogSyncRuns",
                columns: new[] { "Status", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSyncRuns");

            migrationBuilder.RenameColumn(
                name: "LastUpdatedAt",
                table: "Products",
                newName: "LastSynchronizedAt");
        }
    }
}
