using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataTrustHub.Infrastructure.Persistance.Migrations
{
    // Data only (#123): stored emails take the normalized form of the EmailAddress value object (U7)
    /// <inheritdoc />
    public partial class NormalizeUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NormalizeStoredEmails);
        }

        // The SQL Server form of the rule: surrounding whitespace trimmed, then the whole address lower-cased.
        // @whitespace holds the 25 characters string.Trim() removes (char.IsWhiteSpace): U+0009-U+000D,
        // U+0020, U+0085, U+00A0, U+1680, U+2000-U+200A, U+2028, U+2029, U+202F, U+205F, U+3000.
        // The trim runs under a binary collation, so it matches code points as string.Trim() does (the
        // SQL_Latin1_General_CP1 collations do not match U+1680, U+202F and U+205F). Lower-casing and
        // grouping use the database default collation, which is the column's, as the application's lookups do.
        // The check runs first, in the same batch and in the migration's transaction, so a normalization
        // that would give two users the same address fails and changes nothing. The error names the problem,
        // never the addresses.
        // Known gap: LOWER follows the collation, which can lower-case some non-ASCII letters differently
        // from ToLowerInvariant; ASCII addresses get exactly the value object's form
        private const string NormalizeStoredEmails = """
            DECLARE @whitespace nvarchar(32) =
                NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(32)
                + NCHAR(133) + NCHAR(160) + NCHAR(5760)
                + NCHAR(8192) + NCHAR(8193) + NCHAR(8194) + NCHAR(8195) + NCHAR(8196) + NCHAR(8197)
                + NCHAR(8198) + NCHAR(8199) + NCHAR(8200) + NCHAR(8201) + NCHAR(8202)
                + NCHAR(8232) + NCHAR(8233) + NCHAR(8239) + NCHAR(8287) + NCHAR(12288);

            IF EXISTS (
                SELECT 1 FROM [Users]
                GROUP BY LOWER(TRIM(@whitespace FROM [Email] COLLATE Latin1_General_100_BIN2) COLLATE DATABASE_DEFAULT)
                HAVING COUNT(*) > 1)
                THROW 50000, N'NormalizeUserEmails: normalizing the stored emails (trim and lower-case) would give two or more users the same email address. Resolve those accounts first; no email was changed.', 1;

            UPDATE [Users]
            SET [Email] = LOWER(TRIM(@whitespace FROM [Email] COLLATE Latin1_General_100_BIN2) COLLATE DATABASE_DEFAULT);
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the original case and spacing are not kept, and the normalized emails stay valid
        }
    }
}
