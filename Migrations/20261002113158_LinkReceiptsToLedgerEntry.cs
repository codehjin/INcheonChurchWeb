using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace INcheonChurchWeb.Migrations
{
    /// <inheritdoc />
    public partial class LinkReceiptsToLedgerEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LedgerEntryId",
                table: "UploadedReceipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "UploadedReceipts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UploadedReceipts_LedgerEntryId",
                table: "UploadedReceipts",
                column: "LedgerEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_UploadedReceipts_Transactions_LedgerEntryId",
                table: "UploadedReceipts",
                column: "LedgerEntryId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 🚀 기존 연결 백필.
            //    지금까지는 장부의 ReceiptPath 와 영수증의 ImagePath 가 같은 문자열인 것이
            //    곧 '연결'이었다. 그 관계를 FK 로 옮긴다.
            migrationBuilder.Sql(@"
                UPDATE UploadedReceipts
                   SET LedgerEntryId = (
                           SELECT t.Id FROM Transactions t
                            WHERE t.ReceiptPath = UploadedReceipts.ImagePath
                            ORDER BY t.Id
                            LIMIT 1)
                 WHERE IFNULL(ImagePath,'') <> ''
                   AND EXISTS (
                           SELECT 1 FROM Transactions t
                            WHERE t.ReceiptPath = UploadedReceipts.ImagePath);
            ");

            // 연결된 행은 IsMatched 를 맞춰 둔다 (두 값은 짝을 이룬다).
            migrationBuilder.Sql(
                "UPDATE UploadedReceipts SET IsMatched = 1 WHERE LedgerEntryId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadedReceipts_Transactions_LedgerEntryId",
                table: "UploadedReceipts");

            migrationBuilder.DropIndex(
                name: "IX_UploadedReceipts_LedgerEntryId",
                table: "UploadedReceipts");

            migrationBuilder.DropColumn(
                name: "LedgerEntryId",
                table: "UploadedReceipts");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "UploadedReceipts");
        }
    }
}
