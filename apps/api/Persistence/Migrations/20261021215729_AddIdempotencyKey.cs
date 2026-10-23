using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "quotes",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "quotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_adviser_id_idempotency_key",
                table: "quotes",
                columns: new[] { "adviser_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_quotes_adviser_id_idempotency_key",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "quotes");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "quotes");
        }
    }
}
