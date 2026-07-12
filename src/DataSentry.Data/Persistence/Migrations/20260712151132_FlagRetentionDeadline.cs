using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataSentry.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlagRetentionDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetentionDeadline",
                table: "FileScanResults",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                // Rows written before this column existed predate the retention check; "None" is both
                // the honest answer and the only value the enum converter can read back.
                defaultValue: nameof(Core.Models.RetentionDeadline.None));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionDeadline",
                table: "FileScanResults");
        }
    }
}
