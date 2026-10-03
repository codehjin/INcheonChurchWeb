using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace INcheonChurchWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddParentPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParentNotices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Greeting = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleNote = table.Column<string>(type: "TEXT", nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HomeSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentNotices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParentPortalAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    HomeSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentPortalAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParentPortalProfiles",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    HeadPhone = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    HomeSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentPortalProfiles", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "ParentNoticeLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentNoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentNoticeLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentNoticeLinks_ParentNotices_ParentNoticeId",
                        column: x => x.ParentNoticeId,
                        principalTable: "ParentNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentNoticeSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentNoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentNoticeSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentNoticeSections_ParentNotices_ParentNoticeId",
                        column: x => x.ParentNoticeId,
                        principalTable: "ParentNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentNoticeWeeks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentNoticeId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_ParentNoticeWeeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentNoticeWeeks_ParentNotices_ParentNoticeId",
                        column: x => x.ParentNoticeId,
                        principalTable: "ParentNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParentNoticeLinks_ParentNoticeId",
                table: "ParentNoticeLinks",
                column: "ParentNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentNotices_DepartmentId_Year_Month",
                table: "ParentNotices",
                columns: new[] { "DepartmentId", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ParentNoticeSections_ParentNoticeId",
                table: "ParentNoticeSections",
                column: "ParentNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentNoticeWeeks_ParentNoticeId",
                table: "ParentNoticeWeeks",
                column: "ParentNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentPortalAccounts_Username",
                table: "ParentPortalAccounts",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParentNoticeLinks");

            migrationBuilder.DropTable(
                name: "ParentNoticeSections");

            migrationBuilder.DropTable(
                name: "ParentNoticeWeeks");

            migrationBuilder.DropTable(
                name: "ParentPortalAccounts");

            migrationBuilder.DropTable(
                name: "ParentPortalProfiles");

            migrationBuilder.DropTable(
                name: "ParentNotices");
        }
    }
}
