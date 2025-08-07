using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixBusinessInfoColumnType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert business_info column from hstore to jsonb
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN business_info TYPE jsonb USING business_info::text::jsonb;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Convert business_info column from jsonb back to hstore
            migrationBuilder.Sql(@"
                ALTER TABLE payment.teams 
                ALTER COLUMN business_info TYPE hstore USING business_info::text::hstore;
            ");
        }
    }
}
