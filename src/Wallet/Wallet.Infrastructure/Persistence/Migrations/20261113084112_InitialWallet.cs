using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency", x => new { x.player_id, x.scope, x.key });
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "player_identities",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_identities", x => new { x.issuer, x.subject });
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallets", x => new { x.player_id, x.currency });
                    table.CheckConstraint("ck_wallet_currency", "currency IN ('USD','EUR','BTC')");
                    table.CheckConstraint("ck_wallet_nonnegative", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    bet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                    table.CheckConstraint("ck_transaction_balance", "balance_after >= 0");
                    table.CheckConstraint("ck_transaction_bet", "(kind = 'WIN' AND bet_id IS NOT NULL) OR (kind <> 'WIN' AND bet_id IS NULL)");
                    table.CheckConstraint("ck_transaction_kind", "kind IN ('BET','WIN','BONUS')");
                    table.CheckConstraint("ck_transaction_positive", "amount > 0");
                    table.ForeignKey(
                        name: "FK_transactions_transactions_bet_id",
                        column: x => x.bet_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_wallets_player_id_currency",
                        columns: x => new { x.player_id, x.currency },
                        principalTable: "wallets",
                        principalColumns: new[] { "player_id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_identities_player_id",
                table: "player_identities",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_bet_id",
                table: "transactions",
                column: "bet_id",
                unique: true,
                filter: "kind = 'WIN'");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_player_id_created_at_id",
                table: "transactions",
                columns: new[] { "player_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_player_id_currency",
                table: "transactions",
                columns: new[] { "player_id", "currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency");

            migrationBuilder.DropTable(
                name: "inbox");

            migrationBuilder.DropTable(
                name: "player_identities");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "wallets");
        }
    }
}
