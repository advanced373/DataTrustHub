using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataTrustHub.Infrastructure.Persistance.Migrations
{
    // #121: the uniqueness rules U1, O1, P2 and C3 become unique indexes, so two concurrent requests that both
    // pass the use case's check cannot both save. An index key cannot be nvarchar(max), so Users.Email,
    // Organizations.Name and Policies.Name become nvarchar(256), the length the validators already allow
    /// <inheritdoc />
    public partial class AddUniquenessIndexes : Migration
    {
        // The new length of the three columns, fixed here: a later change of the model must not change
        // what this migration checked
        private const int MaxLength = 256;

        // Runs first, in the migration's transaction: when existing data breaks one of the four rules, or
        // does not fit the new length, it throws and nothing is changed. Duplicates are grouped under the
        // column collation, as the index compares them. DATALENGTH counts trailing spaces and UTF-16 code
        // units, as nvarchar(256) does. The errors name the rule and the table, never a value (ADR-0019)
        private static readonly string CheckExistingData = $"""
            IF EXISTS (SELECT 1 FROM [Users] WHERE DATALENGTH([Email]) > {MaxLength * 2})
                THROW 50000, N'AddUniquenessIndexes: the index of rule U1 cannot be created: table Users holds an Email longer than {MaxLength} characters. Shorten it first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Organizations] WHERE DATALENGTH([Name]) > {MaxLength * 2})
                THROW 50000, N'AddUniquenessIndexes: the index of rule O1 cannot be created: table Organizations holds a Name longer than {MaxLength} characters. Shorten it first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Policies] WHERE DATALENGTH([Name]) > {MaxLength * 2})
                THROW 50000, N'AddUniquenessIndexes: the index of rule P2 cannot be created: table Policies holds a Name longer than {MaxLength} characters. Shorten it first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Users] GROUP BY [Email] HAVING COUNT(*) > 1)
                THROW 50000, N'AddUniquenessIndexes: rule U1 is broken: table Users holds two or more users with the same Email. Resolve those accounts first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Organizations] GROUP BY [Name] HAVING COUNT(*) > 1)
                THROW 50000, N'AddUniquenessIndexes: rule O1 is broken: table Organizations holds two or more organizations with the same Name. Resolve them first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Policies] GROUP BY [OrganizationId], [Name] HAVING COUNT(*) > 1)
                THROW 50000, N'AddUniquenessIndexes: rule P2 is broken: table Policies holds two or more policies with the same Name in one organization. Resolve them first; nothing was changed.', 1;

            IF EXISTS (SELECT 1 FROM [Clearances] GROUP BY [UserId], [PolicyId] HAVING COUNT(*) > 1)
                THROW 50000, N'AddUniquenessIndexes: rule C3 is broken: table Clearances holds two or more clearances of one user on the same policy. Resolve them first; nothing was changed.', 1;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CheckExistingData);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Policies",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Organizations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policies_OrganizationId_Name",
                table: "Policies",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Name",
                table: "Organizations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clearances_UserId_PolicyId",
                table: "Clearances",
                columns: new[] { "UserId", "PolicyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Policies_OrganizationId_Name",
                table: "Policies");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Name",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Clearances_UserId_PolicyId",
                table: "Clearances");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Policies",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Organizations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }
    }
}
