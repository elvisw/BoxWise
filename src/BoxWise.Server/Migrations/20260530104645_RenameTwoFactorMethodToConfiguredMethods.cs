using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxWise.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameTwoFactorMethodToConfiguredMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TwoFactorMethod",
                table: "AspNetUsers",
                newName: "ConfiguredMethods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ConfiguredMethods",
                table: "AspNetUsers",
                newName: "TwoFactorMethod");
        }
    }
}
