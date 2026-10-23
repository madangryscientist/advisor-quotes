using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "advisers",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advisers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "text", nullable: false),
                    adviser_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    client_name = table.Column<string>(type: "text", nullable: false),
                    client_date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    client_is_smoker = table.Column<bool>(type: "boolean", nullable: false),
                    product_code = table.Column<string>(type: "text", nullable: false),
                    cover_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    term_years = table.Column<int>(type: "integer", nullable: false),
                    premium = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pricing_version = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_quotes_advisers_adviser_id",
                        column: x => x.adviser_id,
                        principalTable: "advisers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quote_status_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_status_changes_quotes_quote_id",
                        column: x => x.quote_id,
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "advisers",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { "ADV-001", "Thandi M." },
                    { "ADV-002", "Pieter V." },
                    { "ADV-003", "Aisha K." }
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_status_changes_quote_id",
                table: "quote_status_changes",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_adviser_id_created_at",
                table: "quotes",
                columns: new[] { "adviser_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_created_at",
                table: "quotes",
                column: "created_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote_status_changes");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.DropTable(
                name: "advisers");
        }
    }
}
