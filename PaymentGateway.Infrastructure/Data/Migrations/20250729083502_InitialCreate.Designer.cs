﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using PaymentGateway.Infrastructure.Data;

#nullable disable

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PaymentGatewayDbContext))]
    [Migration("20250729083502_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("payment")
                .HasAnnotation("ProductVersion", "9.0.7")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("PaymentGateway.Core.Entities.AuditLog", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Changes")
                        .HasColumnType("jsonb")
                        .HasColumnName("changes");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("CreatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("created_by");

                    b.Property<string>("EntityId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("entity_id");

                    b.Property<string>("EntityName")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("entity_name");

                    b.Property<string>("IPAddress")
                        .HasMaxLength(45)
                        .HasColumnType("character varying(45)")
                        .HasColumnName("ip_address");

                    b.Property<string>("NewValues")
                        .HasColumnType("jsonb")
                        .HasColumnName("new_values");

                    b.Property<string>("OldValues")
                        .HasColumnType("jsonb")
                        .HasColumnName("old_values");

                    b.Property<string>("Operation")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("operation");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<string>("UpdatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("updated_by");

                    b.Property<string>("UserAgent")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("user_agent");

                    b.Property<string>("UserId")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("user_id");

                    b.Property<string>("UserName")
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("user_name");

                    b.HasKey("Id")
                        .HasName("p_k_audit_logs");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ix_audit_logs_created_at");

                    b.HasIndex("EntityId")
                        .HasDatabaseName("ix_audit_logs_entity_id");

                    b.HasIndex("EntityName")
                        .HasDatabaseName("ix_audit_logs_entity_name");

                    b.HasIndex("Operation")
                        .HasDatabaseName("ix_audit_logs_operation");

                    b.HasIndex("UserId")
                        .HasDatabaseName("ix_audit_logs_user_id");

                    b.HasIndex("EntityName", "EntityId")
                        .HasDatabaseName("ix_audit_logs_entity_name_id");

                    b.HasIndex("EntityName", "Operation", "CreatedAt")
                        .HasDatabaseName("ix_audit_logs_entity_operation_created");

                    b.ToTable("audit_logs", "payment");
                });

            modelBuilder.Entity("PaymentGateway.Core.Entities.Payment", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<decimal>("Amount")
                        .HasPrecision(18, 2)
                        .HasColumnType("numeric(18,2)")
                        .HasColumnName("amount");

                    b.Property<DateTime?>("AuthorizedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("authorized_at");

                    b.Property<string>("BankOrderId")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("bank_order_id");

                    b.Property<DateTime?>("CancelledAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("cancelled_at");

                    b.Property<string>("CardMask")
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("card_mask");

                    b.Property<DateTime?>("ConfirmedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("confirmed_at");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("CreatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("created_by");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasMaxLength(3)
                        .HasColumnType("character varying(3)")
                        .HasDefaultValue("RUB")
                        .HasColumnName("currency");

                    b.Property<string>("CustomerEmail")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("customer_email");

                    b.Property<string>("Description")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("description");

                    b.Property<string>("FailureReason")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("failure_reason");

                    b.Property<string>("OrderId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("order_id");

                    b.Property<string>("PaymentId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("payment_id");

                    b.Property<int>("PaymentMethod")
                        .HasColumnType("integer")
                        .HasColumnName("payment_method");

                    b.Property<string>("PaymentURL")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("payment_url");

                    b.Property<byte[]>("RowVersion")
                        .IsConcurrencyToken()
                        .IsRequired()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("row_version");

                    b.Property<int>("Status")
                        .HasColumnType("integer")
                        .HasColumnName("status");

                    b.Property<string>("TeamSlug")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("team_slug");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<string>("UpdatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("updated_by");

                    b.HasKey("Id")
                        .HasName("p_k_payments");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ix_payments_created_at");

                    b.HasIndex("OrderId")
                        .HasDatabaseName("ix_payments_order_id");

                    b.HasIndex("PaymentId")
                        .IsUnique()
                        .HasDatabaseName("ix_payments_payment_id");

                    b.HasIndex("Status")
                        .HasDatabaseName("ix_payments_status");

                    b.HasIndex("TeamSlug")
                        .HasDatabaseName("ix_payments_team_slug");

                    b.HasIndex("TeamSlug", "OrderId")
                        .IsUnique()
                        .HasDatabaseName("ix_payments_team_slug_order_id");

                    b.ToTable("payments", "payment");
                });

            modelBuilder.Entity("PaymentGateway.Core.Entities.Team", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("CreatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("created_by");

                    b.Property<string>("FailUrl")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("fail_url");

                    b.Property<bool>("IsActive")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("boolean")
                        .HasDefaultValue(true)
                        .HasColumnName("is_active");

                    b.Property<string>("NotificationUrl")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("notification_url");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("password_hash");

                    b.Property<byte[]>("RowVersion")
                        .IsConcurrencyToken()
                        .IsRequired()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("row_version");

                    b.Property<string>("SuccessUrl")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("success_url");

                    b.Property<string>("TeamName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("team_name");

                    b.Property<string>("TeamSlug")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("team_slug");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<string>("UpdatedBy")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("updated_by");

                    b.HasKey("Id")
                        .HasName("p_k_teams");

                    b.HasAlternateKey("TeamSlug")
                        .HasName("a_k_teams_team_slug");

                    b.HasIndex("IsActive")
                        .HasDatabaseName("ix_teams_is_active");

                    b.HasIndex("TeamName")
                        .HasDatabaseName("ix_teams_team_name");

                    b.HasIndex("TeamSlug")
                        .IsUnique()
                        .HasDatabaseName("ix_teams_team_slug");

                    b.ToTable("teams", "payment");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            CreatedAt = new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5360),
                            CreatedBy = "SYSTEM",
                            FailUrl = "https://demo.example.com/fail",
                            IsActive = true,
                            NotificationUrl = "https://webhook.site/demo-notifications",
                            PasswordHash = "d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791",
                            RowVersion = new byte[0],
                            SuccessUrl = "https://demo.example.com/success",
                            TeamName = "Demo Team",
                            TeamSlug = "demo-team",
                            UpdatedAt = new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5450),
                            UpdatedBy = "SYSTEM"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            CreatedAt = new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800),
                            CreatedBy = "SYSTEM",
                            IsActive = true,
                            PasswordHash = "ecd71870d1963316a97e3ac3408c9835ad8cf0f3c1bc703527c30265534f75ae",
                            RowVersion = new byte[0],
                            TeamName = "Test Team",
                            TeamSlug = "test-team",
                            UpdatedAt = new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800),
                            UpdatedBy = "SYSTEM"
                        });
                });

            modelBuilder.Entity("PaymentGateway.Core.Entities.Payment", b =>
                {
                    b.HasOne("PaymentGateway.Core.Entities.Team", null)
                        .WithMany("Payments")
                        .HasForeignKey("TeamSlug")
                        .HasPrincipalKey("TeamSlug")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_payments_team_slug");
                });

            modelBuilder.Entity("PaymentGateway.Core.Entities.Team", b =>
                {
                    b.Navigation("Payments");
                });
#pragma warning restore 612, 618
        }
    }
}
