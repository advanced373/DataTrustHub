using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataTrustHub.Infrastructure.Persistance.Migrations
{
    // #128: invariant D1 becomes a check constraint on DataItems: a data item is either personal (no policy,
    // no classification priority) or classified (both set). A row with only one of the two would make
    // DataItem.IsReadableBy throw
    /// <inheritdoc />
    public partial class AddDataItemClassificationCheck : Migration
    {
        // Runs first, in the migration's transaction: when an existing row, soft-deleted or not, breaks D1, it
        // throws and nothing is changed. The error names the rule and the table, never a value or id (ADR-0019)
        private const string CheckExistingData = """
            IF EXISTS (
                SELECT 1 FROM [DataItems]
                WHERE ([PolicyId] IS NULL AND [ClassificationPriority] IS NOT NULL)
                   OR ([PolicyId] IS NOT NULL AND [ClassificationPriority] IS NULL))
                THROW 50000, N'AddDataItemClassificationCheck: rule D1 is broken: table DataItems holds one or more data items with a policy but no classification priority, or a classification priority but no policy. Make those items personal or fully classified first; nothing was changed.', 1;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CheckExistingData);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DataItems_Classification",
                table: "DataItems",
                sql: "([PolicyId] IS NULL AND [ClassificationPriority] IS NULL) OR ([PolicyId] IS NOT NULL AND [ClassificationPriority] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DataItems_Classification",
                table: "DataItems");
        }
    }
}
