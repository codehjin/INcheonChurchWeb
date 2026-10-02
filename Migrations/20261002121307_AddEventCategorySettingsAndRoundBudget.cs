using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace INcheonChurchWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddEventCategorySettingsAndRoundBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Budget",
                table: "EventReports",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventCategorySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", nullable: true),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCategorySettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventCategorySettings_DepartmentId_Name",
                table: "EventCategorySettings",
                columns: new[] { "DepartmentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventCategorySettings");

            migrationBuilder.DropColumn(
                name: "Budget",
                table: "EventReports");
        }
    }
}
