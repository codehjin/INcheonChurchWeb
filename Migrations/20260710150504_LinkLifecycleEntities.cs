using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace INcheonChurchWeb.Migrations
{
    /// <inheritdoc />
    public partial class LinkLifecycleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnnualPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FiscalYear = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Quarter = table.Column<byte>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedIncome = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PlannedExpense = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnualPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseResolutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResolutionNo = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AnnualPlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    BudgetPlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    DrafterId = table.Column<string>(type: "TEXT", nullable: true),
                    ApproverId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseResolutions_AnnualPlans_AnnualPlanId",
                        column: x => x.AnnualPlanId,
                        principalTable: "AnnualPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseResolutions_BudgetPlans_BudgetPlanId",
                        column: x => x.BudgetPlanId,
                        principalTable: "BudgetPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseResolutions_Users_ApproverId",
                        column: x => x.ApproverId,
                        principalTable: "Users",
                        principalColumn: "Username",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseResolutions_Users_DrafterId",
                        column: x => x.DrafterId,
                        principalTable: "Users",
                        principalColumn: "Username",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MeetingDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FreeMemo = table.Column<string>(type: "TEXT", nullable: true),
                    EventDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EventLocation = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedAttendees = table.Column<int>(type: "INTEGER", nullable: true),
                    AnnualPlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyMeetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyMeetings_AnnualPlans_AnnualPlanId",
                        column: x => x.AnnualPlanId,
                        principalTable: "AnnualPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LedgerTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BudgetPlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    AnnualPlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpenseResolutionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerTransactions_AnnualPlans_AnnualPlanId",
                        column: x => x.AnnualPlanId,
                        principalTable: "AnnualPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LedgerTransactions_BudgetPlans_BudgetPlanId",
                        column: x => x.BudgetPlanId,
                        principalTable: "BudgetPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerTransactions_ExpenseResolutions_ExpenseResolutionId",
                        column: x => x.ExpenseResolutionId,
                        principalTable: "ExpenseResolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    VendorName = table.Column<string>(type: "TEXT", nullable: true),
                    ActualAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpenseResolutionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_ExpenseResolutions_ExpenseResolutionId",
                        column: x => x.ExpenseResolutionId,
                        principalTable: "ExpenseResolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseResolutions_AnnualPlanId",
                table: "ExpenseResolutions",
                column: "AnnualPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseResolutions_ApproverId",
                table: "ExpenseResolutions",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseResolutions_BudgetPlanId",
                table: "ExpenseResolutions",
                column: "BudgetPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseResolutions_DrafterId",
                table: "ExpenseResolutions",
                column: "DrafterId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseResolutions_ResolutionNo",
                table: "ExpenseResolutions",
                column: "ResolutionNo");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_AnnualPlanId",
                table: "LedgerTransactions",
                column: "AnnualPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_BudgetPlanId",
                table: "LedgerTransactions",
                column: "BudgetPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_ExpenseResolutionId",
                table: "LedgerTransactions",
                column: "ExpenseResolutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ExpenseResolutionId",
                table: "Receipts",
                column: "ExpenseResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyMeetings_AnnualPlanId",
                table: "WeeklyMeetings",
                column: "AnnualPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerTransactions");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "WeeklyMeetings");

            migrationBuilder.DropTable(
                name: "ExpenseResolutions");

            migrationBuilder.DropTable(
                name: "AnnualPlans");
        }
    }
}
