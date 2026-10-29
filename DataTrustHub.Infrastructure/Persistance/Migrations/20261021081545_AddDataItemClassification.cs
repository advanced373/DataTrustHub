using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataTrustHub.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddDataItemClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClassificationPriority",
                table: "DataItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PolicyId",
                table: "DataItems",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassificationPriority",
                table: "DataItems");

            migrationBuilder.DropColumn(
                name: "PolicyId",
                table: "DataItems");
        }
    }
}
