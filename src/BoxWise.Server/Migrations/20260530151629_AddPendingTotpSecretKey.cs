using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxWise.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingTotpSecretKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingTotpSecretKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingTotpSecretKey",
                table: "AspNetUsers");
        }
    }
}
