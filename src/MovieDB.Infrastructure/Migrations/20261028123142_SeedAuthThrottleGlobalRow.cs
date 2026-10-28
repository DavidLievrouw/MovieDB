using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieDB.Infrastructure.Migrations
{
    /// <inheritdoc />
    internal partial class SeedAuthThrottleGlobalRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The singleton row that serialises login throttling; INSERT IGNORE keeps a retried migration safe.
            migrationBuilder.Sql(
                "INSERT IGNORE INTO `auth_throttle_states` (`key`, `window_start`, `attempts`, `failures`, `blocked_until`, `expires_at`, `last_failure`, `account_changes`) VALUES ('global', '2000-01-01 00:00:00.000000', 0, 0, '2000-01-01 00:00:00.000000', '2000-01-01 00:00:00.000000', '2000-01-01 00:00:00.000000', 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `auth_throttle_states` WHERE `key` = 'global';");
        }
    }
}
