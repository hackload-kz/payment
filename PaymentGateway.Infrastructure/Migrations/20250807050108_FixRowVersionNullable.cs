using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRowVersionNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Make row_version nullable to allow database to set it
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN row_version DROP NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert back to NOT NULL constraint
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN row_version SET NOT NULL;
            ");
        }
    }
}
