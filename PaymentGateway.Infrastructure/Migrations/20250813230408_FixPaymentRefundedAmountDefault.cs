using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixPaymentRefundedAmountDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add default values for Payment table columns that have NOT NULL constraints
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN refunded_amount SET DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN refund_count SET DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN authorization_attempts SET DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN max_allowed_attempts SET DEFAULT 3;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN payment_method SET DEFAULT 0;");
            
            // Update existing NULL values
            migrationBuilder.Sql("UPDATE payment.payments SET refunded_amount = 0 WHERE refunded_amount IS NULL;");
            migrationBuilder.Sql("UPDATE payment.payments SET refund_count = 0 WHERE refund_count IS NULL;");
            migrationBuilder.Sql("UPDATE payment.payments SET authorization_attempts = 0 WHERE authorization_attempts IS NULL;");
            migrationBuilder.Sql("UPDATE payment.payments SET max_allowed_attempts = 3 WHERE max_allowed_attempts IS NULL;");
            migrationBuilder.Sql("UPDATE payment.payments SET payment_method = 0 WHERE payment_method IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove default values
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN refunded_amount DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN refund_count DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN authorization_attempts DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN max_allowed_attempts DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE payment.payments ALTER COLUMN payment_method DROP DEFAULT;");
        }
    }
}
