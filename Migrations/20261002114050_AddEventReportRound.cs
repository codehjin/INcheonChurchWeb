using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace INcheonChurchWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddEventReportRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "EventReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RoundTitle",
                table: "EventReports",
                type: "TEXT",
                nullable: true);

            // 기존 보고서는 모두 1차로 본다 (컬럼 추가 시 0 으로 들어간 행 보정).
            migrationBuilder.Sql("UPDATE EventReports SET Round = 1 WHERE Round IS NULL OR Round = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Round",
                table: "EventReports");

            migrationBuilder.DropColumn(
                name: "RoundTitle",
                table: "EventReports");
        }
    }
}
