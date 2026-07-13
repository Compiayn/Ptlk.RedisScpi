using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptlk.RedisScpi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialScpiCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "command_executions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    redis_key = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    requested_payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    result_payload_json = table.Column<string>(type: "TEXT", nullable: true),
                    actual_value_json = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<long>(type: "INTEGER", nullable: true),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "redis_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_path = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false, collation: "NOCASE"),
                    redis_key = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    concurrency_stamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redis_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scpi_endpoint_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    endpoint_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false, collation: "NOCASE"),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    transport = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    tcp_host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    tcp_port = table.Column<int>(type: "INTEGER", nullable: true),
                    timeout_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    polling_interval_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    converter_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    error_check_mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    error_queue_query = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    command_terminator = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    response_terminator = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    concurrency_stamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scpi_endpoint_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scpi_log_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    endpoint_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    point_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    operation = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    level = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    command_text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    response_text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    quality = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    error_code = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    duration_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scpi_log_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_log_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    level = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    command_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_log_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scpi_point_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    endpoint_config_id = table.Column<int>(type: "INTEGER", nullable: false),
                    point_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false, collation: "NOCASE"),
                    source_path = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false, collation: "NOCASE"),
                    name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    access = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    data_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    number_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    string_format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    enum_format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    read_template = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    write_template = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    unit = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    polling_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    polling_interval_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    initial_read = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    concurrency_stamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scpi_point_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scpi_point_configs_scpi_endpoint_configs_endpoint_config_id",
                        column: x => x.endpoint_config_id,
                        principalTable: "scpi_endpoint_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scpi_enum_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scpi_point_config_id = table.Column<int>(type: "INTEGER", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false, collation: "NOCASE"),
                    code = table.Column<int>(type: "INTEGER", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scpi_enum_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_scpi_enum_options_scpi_point_configs_scpi_point_config_id",
                        column: x => x.scpi_point_config_id,
                        principalTable: "scpi_point_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_command_executions_command_id",
                table: "command_executions",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_command_executions_redis_key",
                table: "command_executions",
                column: "redis_key");

            migrationBuilder.CreateIndex(
                name: "ix_command_executions_status_completed_at",
                table: "command_executions",
                columns: new[] { "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_redis_mappings_redis_key",
                table: "redis_mappings",
                column: "redis_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redis_mappings_source_path",
                table: "redis_mappings",
                column: "source_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scpi_endpoint_configs_endpoint_id",
                table: "scpi_endpoint_configs",
                column: "endpoint_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scpi_enum_options_scpi_point_config_id_code",
                table: "scpi_enum_options",
                columns: new[] { "scpi_point_config_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scpi_enum_options_scpi_point_config_id_value",
                table: "scpi_enum_options",
                columns: new[] { "scpi_point_config_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scpi_log_entries_command_id",
                table: "scpi_log_entries",
                column: "command_id");

            migrationBuilder.CreateIndex(
                name: "ix_scpi_log_entries_created_at",
                table: "scpi_log_entries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_scpi_log_entries_endpoint_id_point_id",
                table: "scpi_log_entries",
                columns: new[] { "endpoint_id", "point_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scpi_point_configs_endpoint_config_id_point_id",
                table: "scpi_point_configs",
                columns: new[] { "endpoint_config_id", "point_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scpi_point_configs_source_path",
                table: "scpi_point_configs",
                column: "source_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_log_entries_category_level",
                table: "system_log_entries",
                columns: new[] { "category", "level" });

            migrationBuilder.CreateIndex(
                name: "ix_system_log_entries_command_id",
                table: "system_log_entries",
                column: "command_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_log_entries_created_at",
                table: "system_log_entries",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_executions");

            migrationBuilder.DropTable(
                name: "redis_mappings");

            migrationBuilder.DropTable(
                name: "scpi_enum_options");

            migrationBuilder.DropTable(
                name: "scpi_log_entries");

            migrationBuilder.DropTable(
                name: "system_log_entries");

            migrationBuilder.DropTable(
                name: "scpi_point_configs");

            migrationBuilder.DropTable(
                name: "scpi_endpoint_configs");
        }
    }
}
