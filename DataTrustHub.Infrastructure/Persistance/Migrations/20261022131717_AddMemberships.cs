using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataTrustHub.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberships : Migration
    {
        // Values as of this migration; later renames need a new migration
        private const string AdminRole = "Admin";
        private const string OrgAdminRole = "OrgAdmin";
        private const string MemberRole = "Member";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId_OrganizationId",
                table: "Memberships",
                columns: new[] { "UserId", "OrganizationId" },
                unique: true);

            // Existing data (ADR-0014): no organization starts without an admin (M2), and admins
            // are migrated first so that they keep OrgAdmin where they also hold clearances
            migrationBuilder.Sql(AppointPlatformAdminsAsOrgAdmins);
            migrationBuilder.Sql(AddClearanceHoldersAsMembers);
        }

        // Every platform admin becomes an organization admin of every existing organization
        private const string AppointPlatformAdminsAsOrgAdmins = $"""
            INSERT INTO [Memberships] ([Id], [UserId], [OrganizationId], [Role])
            SELECT NEWID(), [u].[Id], [o].[Id], N'{OrgAdminRole}'
            FROM [Users] AS [u]
            CROSS JOIN [Organizations] AS [o]
            WHERE [u].[Role] = N'{AdminRole}';
            """;

        // Every holder of a clearance on a policy of organization X becomes a member of X, once,
        // unless they already have a membership there; orphan clearances and policies are ignored
        private const string AddClearanceHoldersAsMembers = $"""
            INSERT INTO [Memberships] ([Id], [UserId], [OrganizationId], [Role])
            SELECT NEWID(), [held].[UserId], [held].[OrganizationId], N'{MemberRole}'
            FROM (
                SELECT DISTINCT [u].[Id] AS [UserId], [o].[Id] AS [OrganizationId]
                FROM [Clearances] AS [c]
                INNER JOIN [Users] AS [u] ON [u].[Id] = [c].[UserId]
                INNER JOIN [Policies] AS [p] ON [p].[Id] = [c].[PolicyId]
                INNER JOIN [Organizations] AS [o] ON [o].[Id] = [p].[OrganizationId]
            ) AS [held]
            WHERE NOT EXISTS (
                SELECT 1 FROM [Memberships] AS [m]
                WHERE [m].[UserId] = [held].[UserId] AND [m].[OrganizationId] = [held].[OrganizationId]);
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Memberships");
        }
    }
}
