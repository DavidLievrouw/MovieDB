using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieDB.Infrastructure.Migrations;

/// <summary>
/// Creates the domain and auth tables. MariaDB DDL is not transactional, so every statement is a single
/// self-guarding CREATE TABLE IF NOT EXISTS (keys and constraints inline); a run that failed half-way can be retried.
/// </summary>
internal partial class Initial : Migration
{
    private const string TableOptions = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `genres` (
                `id` int NOT NULL AUTO_INCREMENT,
                `name` varchar(50) NOT NULL,
                `created_at` datetime(6) NOT NULL,
                CONSTRAINT `pk_genres` PRIMARY KEY (`id`),
                UNIQUE INDEX `ux_genres_name` (`name`)
            ) {TableOptions};
            """
        );

        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `media` (
                `id` int NOT NULL AUTO_INCREMENT,
                `name` varchar(50) NOT NULL,
                `created_at` datetime(6) NOT NULL,
                CONSTRAINT `pk_media` PRIMARY KEY (`id`),
                UNIQUE INDEX `ux_media_name` (`name`)
            ) {TableOptions};
            """
        );

        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `movies` (
                `id` int NOT NULL AUTO_INCREMENT,
                `title` varchar(200) NOT NULL,
                `release_year` smallint NOT NULL,
                `director` varchar(150) NOT NULL,
                `genre_id` int NOT NULL,
                `runtime_minutes` smallint NOT NULL,
                `watched_on` date NOT NULL,
                `medium_id` int NULL,
                `overall_score` tinyint NULL,
                `story_score` tinyint NULL,
                `picture_score` tinyint NULL,
                `music_score` tinyint NULL,
                `sound_score` tinyint NULL,
                `extras_score` tinyint NULL,
                `review` mediumtext NULL,
                `created_at` datetime(6) NOT NULL,
                `updated_at` datetime(6) NOT NULL,
                `version` int NOT NULL,
                CONSTRAINT `pk_movies` PRIMARY KEY (`id`),
                CONSTRAINT `ck_movies_runtime_minutes` CHECK (`runtime_minutes` BETWEEN 1 AND 999),
                CONSTRAINT `ck_movies_overall_score` CHECK (`overall_score` IS NULL OR `overall_score` BETWEEN 1 AND 10),
                CONSTRAINT `ck_movies_story_score` CHECK (`story_score` IS NULL OR `story_score` BETWEEN 1 AND 10),
                CONSTRAINT `ck_movies_picture_score` CHECK (`picture_score` IS NULL OR `picture_score` BETWEEN 1 AND 10),
                CONSTRAINT `ck_movies_music_score` CHECK (`music_score` IS NULL OR `music_score` BETWEEN 1 AND 10),
                CONSTRAINT `ck_movies_sound_score` CHECK (`sound_score` IS NULL OR `sound_score` BETWEEN 1 AND 10),
                CONSTRAINT `ck_movies_extras_score` CHECK (`extras_score` IS NULL OR `extras_score` BETWEEN 1 AND 10),
                UNIQUE INDEX `ux_movies_title_year_director` (`title`, `release_year`, `director`),
                INDEX `ix_movies_genre_id` (`genre_id`),
                INDEX `ix_movies_medium_id` (`medium_id`),
                CONSTRAINT `fk_movies_genres` FOREIGN KEY (`genre_id`) REFERENCES `genres` (`id`) ON DELETE RESTRICT,
                CONSTRAINT `fk_movies_media` FOREIGN KEY (`medium_id`) REFERENCES `media` (`id`) ON DELETE RESTRICT
            ) {TableOptions};
            """
        );

        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `users` (
                `id` varchar(36) NOT NULL,
                `user_name` varchar(256) NOT NULL,
                `normalized_user_name` varchar(256) NOT NULL,
                `password_hash` varchar(512) NOT NULL,
                `security_stamp` varchar(64) NOT NULL,
                `concurrency_stamp` varchar(64) NOT NULL,
                `created_at` datetime(6) NOT NULL,
                `updated_at` datetime(6) NOT NULL,
                CONSTRAINT `pk_users` PRIMARY KEY (`id`),
                UNIQUE INDEX `ux_users_normalized_user_name` (`normalized_user_name`)
            ) {TableOptions};
            """
        );

        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `auth_throttle_states` (
                `key` varchar(80) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                `window_start` datetime(6) NOT NULL,
                `attempts` int NOT NULL,
                `failures` int NOT NULL,
                `blocked_until` datetime(6) NOT NULL,
                `expires_at` datetime(6) NOT NULL,
                `last_failure` datetime(6) NOT NULL,
                `last_account` varchar(80) CHARACTER SET ascii COLLATE ascii_bin NULL,
                `account_changes` int NOT NULL,
                `last_success` datetime(6) NULL,
                CONSTRAINT `pk_auth_throttle_states` PRIMARY KEY (`key`),
                INDEX `ix_auth_throttle_states_expires_at` (`expires_at`)
            ) {TableOptions};
            """
        );

        migrationBuilder.Sql(
            $"""
            CREATE TABLE IF NOT EXISTS `data_protection_keys` (
                `id` int NOT NULL AUTO_INCREMENT,
                `friendly_name` varchar(200) NULL,
                `xml` longtext NULL,
                CONSTRAINT `pk_data_protection_keys` PRIMARY KEY (`id`)
            ) {TableOptions};
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS `movies`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `genres`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `media`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `users`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `auth_throttle_states`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `data_protection_keys`;");
    }
}
