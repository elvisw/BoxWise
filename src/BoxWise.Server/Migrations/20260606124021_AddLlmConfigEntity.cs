using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoxWise.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmConfigEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmConfigs");
        }
    }
}
