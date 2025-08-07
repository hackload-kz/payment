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
            // Add default value for failed_authentication_attempts column
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN failed_authentication_attempts SET DEFAULT 0;
                
                UPDATE payment.teams 
                SET failed_authentication_attempts = 0 
                WHERE failed_authentication_attempts IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove default value for failed_authentication_attempts column
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN failed_authentication_attempts DROP DEFAULT;
            ");
        }
    }
}
