using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAllJsonColumnsFromHstore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert only the teams.metadata column that's causing the issue
            migrationBuilder.Sql(@"
                -- Teams table metadata column (business_info already converted)
                ALTER TABLE payment.teams 
                ALTER COLUMN metadata TYPE jsonb USING metadata::text::jsonb;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Convert teams.metadata column back to hstore (rollback)
            migrationBuilder.Sql(@"
                -- Teams table metadata column
                ALTER TABLE payment.teams 
                ALTER COLUMN metadata TYPE hstore USING metadata::text::hstore;
            ");
        }
    }
}
