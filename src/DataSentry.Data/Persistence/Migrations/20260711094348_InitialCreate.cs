using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataSentry.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FilesScanned = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FilesRecommendedForDeletion = table.Column<int>(type: "INTEGER", nullable: false),
                    ReclaimableBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FilesNeedingReview = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileScanResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAccessedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Recommendation = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RiskLevel = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileScanResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileScanResults_ScanReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "ScanReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanErrors_ScanReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "ScanReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PiiFindings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileScanResultId = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DetectorName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PiiFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PiiFindings_FileScanResults_FileScanResultId",
                        column: x => x.FileScanResultId,
                        principalTable: "FileScanResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileScanResults_ReportId",
                table: "FileScanResults",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PiiFindings_FileScanResultId",
                table: "PiiFindings",
                column: "FileScanResultId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanErrors_ReportId",
                table: "ScanErrors",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanReports_CompletedUtc",
                table: "ScanReports",
                column: "CompletedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PiiFindings");

            migrationBuilder.DropTable(
                name: "ScanErrors");

            migrationBuilder.DropTable(
                name: "FileScanResults");

            migrationBuilder.DropTable(
                name: "ScanReports");
        }
    }
}
