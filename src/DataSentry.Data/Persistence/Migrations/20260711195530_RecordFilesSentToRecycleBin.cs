using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataSentry.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordFilesSentToRecycleBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecycledUtc",
                table: "FileScanResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileScanResults_ReportId_Recommendation",
                table: "FileScanResults",
                columns: new[] { "ReportId", "Recommendation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileScanResults_ReportId_Recommendation",
                table: "FileScanResults");

            migrationBuilder.DropColumn(
                name: "RecycledUtc",
                table: "FileScanResults");
        }
    }
}
