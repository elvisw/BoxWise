using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxWise.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddItemConcurrencyAndEditTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Items",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Items_UpdatedByUserId",
                table: "Items",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_AspNetUsers_UpdatedByUserId",
                table: "Items",
                column: "UpdatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_AspNetUsers_UpdatedByUserId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_UpdatedByUserId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Items");
        }
    }
}
