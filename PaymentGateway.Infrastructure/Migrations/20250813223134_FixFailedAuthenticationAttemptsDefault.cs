using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFailedAuthenticationAttemptsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add default value to failed_authentication_attempts column
            migrationBuilder.Sql("ALTER TABLE payment.teams ALTER COLUMN failed_authentication_attempts SET DEFAULT 0;");
            
            // Update existing records that might have NULL values
            migrationBuilder.Sql("UPDATE payment.teams SET failed_authentication_attempts = 0 WHERE failed_authentication_attempts IS NULL;");
            
            // Add default value to is_sensitive column in audit_entries
            migrationBuilder.Sql("ALTER TABLE payment.audit_entries ALTER COLUMN is_sensitive SET DEFAULT FALSE;");
            
            // Update existing audit records that might have NULL values
            migrationBuilder.Sql("UPDATE payment.audit_entries SET is_sensitive = FALSE WHERE is_sensitive IS NULL;");
            
            // Add default value to is_archived column in audit_entries  
            migrationBuilder.Sql("ALTER TABLE payment.audit_entries ALTER COLUMN is_archived SET DEFAULT FALSE;");
            
            // Update existing audit records that might have NULL values
            migrationBuilder.Sql("UPDATE payment.audit_entries SET is_archived = FALSE WHERE is_archived IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove default value from failed_authentication_attempts column
            migrationBuilder.Sql("ALTER TABLE payment.teams ALTER COLUMN failed_authentication_attempts DROP DEFAULT;");
            
            // Remove default values from audit_entries columns
            migrationBuilder.Sql("ALTER TABLE payment.audit_entries ALTER COLUMN is_sensitive DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE payment.audit_entries ALTER COLUMN is_archived DROP DEFAULT;");
        }
    }
}
