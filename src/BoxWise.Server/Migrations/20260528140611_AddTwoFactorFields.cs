using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxWise.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailForTwoFactor",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecretKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorGracePeriodUntil",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TwoFactorMethod",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorSetupCompletedAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailForTwoFactor",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TotpSecretKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorGracePeriodUntil",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorMethod",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorSetupCompletedAt",
                table: "AspNetUsers");
        }
    }
}
