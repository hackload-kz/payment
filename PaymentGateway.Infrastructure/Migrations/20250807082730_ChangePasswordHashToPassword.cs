using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangePasswordHashToPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only drop password_hash column if it exists
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns 
                              WHERE table_schema = 'payment' 
                              AND table_name = 'teams' 
                              AND column_name = 'password_hash') THEN
                        ALTER TABLE payment.teams DROP COLUMN password_hash;
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "transactions",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "teams",
                type: "bytea",
                rowVersion: true,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            // Only add password column if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns 
                                  WHERE table_schema = 'payment' 
                                  AND table_name = 'teams' 
                                  AND column_name = 'password') THEN
                        ALTER TABLE payment.teams ADD password character varying(128) NOT NULL DEFAULT '';
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "payments",
                type: "bytea",
                rowVersion: true,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "payment_methods",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "customers",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password",
                schema: "payment",
                table: "teams");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "transactions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "teams",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "payment",
                table: "teams",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "payments",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "payment_methods",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "row_version",
                schema: "payment",
                table: "customers",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
