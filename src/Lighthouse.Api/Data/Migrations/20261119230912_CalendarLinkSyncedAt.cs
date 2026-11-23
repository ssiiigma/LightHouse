using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lighthouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CalendarLinkSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastSyncedAtUtc",
                table: "CalendarEventLinks",
                newName: "SyncedAtUtc");

            migrationBuilder.RenameColumn(
                name: "RefreshTokenProtected",
                table: "CalendarConnections",
                newName: "RefreshTokenEncrypted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SyncedAtUtc",
                table: "CalendarEventLinks",
                newName: "LastSyncedAtUtc");

            migrationBuilder.RenameColumn(
                name: "RefreshTokenEncrypted",
                table: "CalendarConnections",
                newName: "RefreshTokenProtected");
        }
    }
}
