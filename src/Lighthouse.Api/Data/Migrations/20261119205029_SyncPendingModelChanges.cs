using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lighthouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleSubject",
                table: "Users",
                newName: "GoogleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleId",
                table: "Users",
                newName: "GoogleSubject");
        }
    }
}
