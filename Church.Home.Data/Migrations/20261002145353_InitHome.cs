using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Church.Home.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitHome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Greeting = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleNote = table.Column<string>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParentAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeptName = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalProfiles",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeptName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Motto = table.Column<string>(type: "TEXT", nullable: true),
                    MottoVerse = table.Column<string>(type: "TEXT", nullable: true),
                    OpenChatUrl = table.Column<string>(type: "TEXT", nullable: true),
                    OpenChatCode = table.Column<string>(type: "TEXT", nullable: true),
                    YouTubeUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoAlbumUrl = table.Column<string>(type: "TEXT", nullable: true),
                    MinisterTitle = table.Column<string>(type: "TEXT", nullable: false),
                    MinisterName = table.Column<string>(type: "TEXT", nullable: true),
                    MinisterPhone = table.Column<string>(type: "TEXT", nullable: true),
                    HeadName = table.Column<string>(type: "TEXT", nullable: true),
                    HeadPhone = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalProfiles", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "NoticeLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeLinks_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeSections_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeWeeks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Scripture = table.Column<string>(type: "TEXT", nullable: true),
                    SermonTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Activity = table.Column<string>(type: "TEXT", nullable: true),
                    Offering = table.Column<string>(type: "TEXT", nullable: true),
                    Prayer = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeWeeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeWeeks_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeLinks_NoticeId",
                table: "NoticeLinks",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_DepartmentId_Year_Month",
                table: "Notices",
                columns: new[] { "DepartmentId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeSections_NoticeId",
                table: "NoticeSections",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWeeks_NoticeId",
                table: "NoticeWeeks",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentAccounts_Username",
                table: "ParentAccounts",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeLinks");

            migrationBuilder.DropTable(
                name: "NoticeSections");

            migrationBuilder.DropTable(
                name: "NoticeWeeks");

            migrationBuilder.DropTable(
                name: "ParentAccounts");

            migrationBuilder.DropTable(
                name: "PortalProfiles");

            migrationBuilder.DropTable(
                name: "Notices");
        }
    }
}
