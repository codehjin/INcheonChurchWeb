using INcheonChurchWeb.Data;
using INcheonChurchWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using ClosedXML.Excel;
using ExcelDataReader;
using System.Data;
using System.Globalization;

namespace INcheonChurchWeb.Services
{
    // 영수증 업로드·삭제 / 장부 매칭
    //   ⚠️ AccountingService는 파티셜 클래스다. 다른 조각은 AccountingService.*.cs 참조.
    public partial class AccountingService
    {
        // =========================================================
        // 2. 영수증 이미지 최적화 업로드
        // =========================================================
        /// <summary>
        /// 업로드된 영수증 한 건을 저장한다. (장부 매칭 대기 목록에 들어간다)
        ///
        /// 화면 세 곳(웹 단일/일괄, 모바일 촬영/일괄)이 각자 파일 저장과 DB 삽입을 조립하고 있었고,
        /// 그중 어디도 이미지를 줄이지 않아 원본 사진이 그대로 쌓였다. (웹_상세_구조문서.md §6.5)
        /// 이제 저장 경로는 여기 하나뿐이며, 이미지는 긴 변 1200px·JPEG 품질 75로 줄여 넣는다.
        /// </summary>
        /// <returns>저장된 상대 경로 (예: /uploads/receipts/receipt_3_20260905123000123.jpg)</returns>
        public async Task<string> SaveUploadedReceiptAsync(
            FileService files, byte[] bytes, string originalName,
            int deptId, DateTime receiptDate, decimal amount, string? description)
        {
            string ext = Path.GetExtension(originalName ?? "").ToLowerInvariant();
            bool isPdf = ext == ".pdf";
            if (!isPdf) ext = ".jpg";   // 이미지는 jpg로 통일한다

            string fileName = files.BuildReceiptFileName(deptId, ext, includeMilliseconds: true);

            if (isPdf)
            {
                await files.SaveBytesAsync(bytes, fileName);
            }
            else
            {
                // 원본 사진은 5~10MB에 이른다. 증빙 확인에는 1200px이면 충분하다.
                byte[] optimized = await OptimizeReceiptImageAsync(bytes);
                await files.SaveBytesAsync(optimized, fileName);
            }

            string relativePath = files.BuildReceiptRelativePath(fileName);

            using var db = _dbFactory.CreateDbContext();
            db.UploadedReceipts.Add(new UploadedReceipt
            {
                DepartmentId = deptId,
                ImagePath = relativePath,
                ReceiptDate = receiptDate,
                Amount = amount,
                Description = string.IsNullOrWhiteSpace(description) ? "내역 없음" : description!
            });
            await db.SaveChangesAsync();

            return relativePath;
        }

        /// <summary>영수증 이미지를 긴 변 1200px·품질 75로 줄인다. 읽지 못하면 원본을 그대로 돌려준다.</summary>
        public static async Task<byte[]> OptimizeReceiptImageAsync(byte[] bytes)
        {
            try
            {
                using var input = new MemoryStream(bytes);
                using var image = await Image.LoadAsync(input);

                if (image.Width > 1200)
                    image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1200, 0), Mode = ResizeMode.Max }));

                using var output = new MemoryStream();
                await image.SaveAsync(output, new JpegEncoder { Quality = 75 });
                return output.ToArray();
            }
            catch
            {
                // HEIC 등 ImageSharp가 못 읽는 형식 — 원본을 그대로 저장한다
                return bytes;
            }
        }

        /// <summary>이번 달 AI 판독 사용량. 화면 두 곳이 같은 쿼리를 복사해 쓰고 있었다.</summary>
        public async Task<int> GetOcrUsageAsync(DateTime? month = null)
        {
            string ym = (month ?? DateTime.Now).ToString("yyyy-MM");

            using var db = _dbFactory.CreateDbContext();
            var usage = await db.OcrUsages.AsNoTracking().FirstOrDefaultAsync(u => u.YearMonth == ym);
            return usage?.UsageCount ?? 0;
        }

        public async Task<string> UploadReceiptAsync(IBrowserFile file, int transactionId)
        {
            using var db = _dbFactory.CreateDbContext();

            // 🚀 부서 정보를 가져오기 위해 Include 추가
            var entry = await db.Transactions.Include(t => t.DepartmentInfo)
                                              .FirstOrDefaultAsync(t => t.Id == transactionId);

            if (entry == null) return "내역을 찾을 수 없습니다.";

            try
            {
                string extension = Path.GetExtension(file.Name).ToLower();
                if (extension != ".pdf") extension = ".jpg"; // 이미지는 jpg로 통일

                // 파일명 오류 방지 및 부서명 추출
                string safeDesc = InvalidFileNameChars().Replace(entry.Description ?? "내용없음", "_");
                // 적요가 길어도 전체 파일명이 과도해지지 않도록 제한
                if (safeDesc.Length > MaxDescLengthInFileName) safeDesc = safeDesc[..MaxDescLengthInFileName];
                safeDesc = safeDesc.Trim();
                if (safeDesc.Length == 0) safeDesc = "내용없음";

                string deptName = entry.DepartmentInfo?.Name ?? "부서미정";

                // 🚀 파일명 규칙: 날짜_부서명_분류_적요_금액_타임스탬프.확장자
                //    - 금액은 콤마 없는 숫자만. 나중에 OCR 판독 결과와 대조할 정답 라벨로 쓴다.
                //    - 타임스탬프로 유일성을 확보한다. 부서·날짜·분류·적요가 모두 같은 거래가
                //      실제로 존재해(백업 기준 512건 중 116건) 예전 규칙은 한 파일을 덮어썼고,
                //      재업로드 시 경로가 그대로라 브라우저가 지운 영수증을 캐시에서 다시 그렸다.
                decimal amount = entry.Expense > 0 ? entry.Expense : entry.Income;
                string amountText = amount.ToString("0", CultureInfo.InvariantCulture);
                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                string newFileName = $"{entry.Date:yyyy-MM-dd}_{deptName}_{entry.Category}_{safeDesc}_{amountText}_{stamp}{extension}";

                // 영수증은 매칭 탭 업로드와 같은 폴더에 모은다.
                // 과거 파일은 /uploads/ 에 남아 있지만 DB에 전체 경로가 있어 그대로 열린다.
                string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "receipts");
                if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);
                string filePath = Path.Combine(uploadFolder, newFileName);

                using var inputStream = file.OpenReadStream(1024 * 1024 * 20);
                {
                    if (extension == ".pdf") { using (var fs = new FileStream(filePath, FileMode.Create)) { await inputStream.CopyToAsync(fs); } }
                    else
                    {
                        using (var image = await Image.LoadAsync(inputStream))
                        {
                            if (image.Width > 1200) image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1200, 0), Mode = ResizeMode.Max }));
                            await image.SaveAsync(filePath, new JpegEncoder { Quality = 75 });
                        }
                    }
                }
                entry.ReceiptPath = $"/uploads/receipts/{newFileName}";
                await db.SaveChangesAsync();
                return "OK";
            }
            catch (Exception ex) { return $"실패: {ex.Message}"; }
        }

        // 🚀 장부에서 영수증을 분리한다.
        //    영수증 경로는 ReceiptPath 하나로 관리한다(예전 ReceiptUrl 은 이관 후 폐기).
        //
        //    파일 처리 정책(하이브리드):
        //      - 짝이 되는 UploadedReceipts 행이 있으면 → 파일을 남기고 IsMatched=false 로 되돌린다.
        //        미연결 목록에 썸네일이 정상으로 복귀하고, 잘못 눌러도 재매칭할 수 있다.
        //        (완전 삭제는 매칭 탭의 '삭제(반려)'가 담당한다.)
        //      - 짝이 없으면(장부에서 직접 올린 건) → 돌아갈 목록이 없어 고아가 되므로 파일을 지운다.
        //    단, 다른 거래나 다른 영수증이 같은 경로를 참조 중이면 절대 지우지 않는다.
        //    (예전 파일명 규칙은 유일하지 않아 과거 데이터에 경로 공유가 존재한다.)
        public async Task RemoveReceiptAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();

            var entry = await db.Transactions.FindAsync(id);
            if (entry == null) return;

            string path = entry.ReceiptPath;

            // 매칭으로 연결됐던 영수증은 전부 미연결 목록으로 되돌리고, 그 파일은 보존한다.
            // (한 거래에 여러 장이 붙어 있을 수 있다.)
            var linkedReceipts = await db.UploadedReceipts
                .Where(r => r.LedgerEntryId == id || (path != "" && r.ImagePath == path))
                .ToListAsync();

            var keepFiles = new HashSet<string>(
                linkedReceipts.Select(r => r.ImagePath), StringComparer.OrdinalIgnoreCase);

            foreach (var r in linkedReceipts)
            {
                r.LedgerEntryId = null;
                r.IsMatched = false;
                r.SortOrder = 0;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                // 붙은 파일은 없지만 연결만 남아 있던 경우 — 연결 해제만 저장하고 끝낸다.
                if (linkedReceipts.Count > 0) await db.SaveChangesAsync();
                return;
            }

            bool keepFile = keepFiles.Contains(path);

            entry.ReceiptPath = "";

            // 🚀 DB를 먼저 확정한 뒤 파일을 지운다.
            //    반대 순서면 저장이 실패했을 때 경로만 남아 깨진 이미지가 된다.
            await db.SaveChangesAsync();

            if (keepFile) return;

            // 다른 거래가 아직 같은 파일을 참조 중이면 남긴다.
            //    (예전 파일명 규칙은 유일하지 않아 과거 데이터에 경로 공유가 존재한다.)
            if (await db.Transactions.AnyAsync(t => t.Id != id && t.ReceiptPath == path)) return;

            // 다른 영수증 행이 같은 파일을 참조 중이어도 남긴다.
            if (await db.UploadedReceipts.AnyAsync(r => r.ImagePath == path)) return;

            // 파일 삭제는 실패해도 DB 정리를 되돌리지 않는다 (경로는 이미 비워졌다).
            try
            {
                var fullPath = Path.Combine(_env.WebRootPath, path.TrimStart('/'));
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"영수증 파일 삭제 실패({path}): {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 🔗 장부 매칭 — 미연결 영수증 ↔ 증빙 없는 장부
        // ══════════════════════════════════════════════════════════════
        public async Task<(List<UploadedReceipt> Receipts, List<LedgerEntry> Unmatched)> GetMatchingDataAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();

            // 연결 여부는 FK 가 기준이다. 예전 삭제 로직이 남긴 고아 영수증
            // (IsMatched=true 인데 물고 있는 장부가 없는 행)도 여기서 자연히 목록에 복귀한다.
            var receipts = await db.UploadedReceipts.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && r.LedgerEntryId == null)
                .OrderByDescending(r => r.ReceiptDate)
                .ToListAsync();

            var unmatched = await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId && (t.ReceiptPath == null || t.ReceiptPath == ""))
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            return (receipts, unmatched);
        }

        /// <summary>영수증 연결 결과. 금액이 어긋나면 화면이 경고를 띄우도록 수치를 함께 돌려준다.</summary>
        public sealed record MatchResult(bool Ok, int Linked, decimal ReceiptTotal, decimal EntryAmount, string? Error = null)
        {
            /// <summary>영수증 합계와 장부 금액이 다른가. 막지는 않고 알리기만 한다.</summary>
            public bool AmountDiffers => Ok && ReceiptTotal != EntryAmount;
        }

        // 영수증 한 건을 장부 한 건에 연결한다.
        public Task<MatchResult> MatchReceiptAsync(FileService files, int receiptId, int entryId, string deptName)
            => MatchReceiptsAsync(files, new[] { receiptId }, entryId, deptName);

        // 🚀 영수증 여러 장을 장부 한 건에 연결한다.
        //    카드 결제는 3만원 한 건인데 영수증은 1만원짜리 3장으로 나오는 경우가 있다.
        //    파일명은 장부 내용으로 바꿔 나중에 알아보기 쉽게 하고, 여러 장이면 순번을 붙인다.
        //    금액이 맞지 않아도 막지 않는다 — 부분 영수증·할인으로 딱 떨어지지 않는 경우가 있다.
        public async Task<MatchResult> MatchReceiptsAsync(FileService files, IEnumerable<int> receiptIds, int entryId, string deptName)
        {
            var idList = receiptIds?.Distinct().ToList() ?? new List<int>();
            if (idList.Count == 0) return new MatchResult(false, 0, 0, 0, "연결할 영수증을 선택해 주세요.");

            using var db = _dbFactory.CreateDbContext();

            var entry = await db.Transactions.FindAsync(entryId);
            if (entry == null) return new MatchResult(false, 0, 0, 0, "해당 장부 내역을 찾을 수 없습니다.");

            var receipts = await db.UploadedReceipts
                .Where(r => idList.Contains(r.Id))
                .OrderBy(r => r.ReceiptDate).ThenBy(r => r.Id)
                .ToListAsync();
            if (receipts.Count == 0) return new MatchResult(false, 0, 0, 0, "영수증을 찾을 수 없습니다.");

            // 이 거래에 이미 붙어 있는 장수 뒤에 이어 붙인다.
            int nextOrder = await db.UploadedReceipts
                .Where(r => r.LedgerEntryId == entryId)
                .CountAsync();

            decimal entryAmount = entry.Expense > 0 ? entry.Expense : entry.Income;
            string safeDesc = string.Concat((entry.Description ?? "내역없음").Split(Path.GetInvalidFileNameChars())).Replace(" ", "");

            foreach (var receipt in receipts)
            {
                string oldPath = receipt.ImagePath;

                if (File.Exists(files.GetPhysicalPathFromRelative(oldPath)))
                {
                    string newName = $"{entry.Date:yyyyMMdd}_{deptName}_{entry.Category}_{entryAmount}원_{safeDesc}_{receipt.Id}{Path.GetExtension(oldPath)}";
                    try
                    {
                        files.TryMoveByRelativePath(oldPath, newName, out var newPath);
                        receipt.ImagePath = newPath;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"파일명 변경 실패: {ex.Message}");
                    }
                }

                receipt.LedgerEntryId = entry.Id;
                receipt.IsMatched = true;
                receipt.SortOrder = nextOrder++;
            }

            // 장부의 ReceiptPath 는 '대표 1장'이다. 증빙 아이콘·모달이 쓰는 값이라
            // 비워 두지 않는다. 전체 목록은 LedgerEntryId 로 조회한다.
            if (string.IsNullOrWhiteSpace(entry.ReceiptPath))
                entry.ReceiptPath = receipts[0].ImagePath;

            await db.SaveChangesAsync();

            // SQLite 는 decimal 합산을 서버에서 못 하므로 메모리에서 더한다.
            var linkedAmounts = await db.UploadedReceipts
                .Where(r => r.LedgerEntryId == entry.Id)
                .Select(r => r.Amount)
                .ToListAsync();
            decimal receiptTotal = linkedAmounts.Sum();

            return new MatchResult(true, receipts.Count, receiptTotal, entryAmount);
        }

        /// <summary>장부 여러 건에 붙은 영수증을 한 번에 읽는다. 인쇄에서 쓴다.</summary>
        public async Task<Dictionary<int, List<UploadedReceipt>>> GetReceiptsByEntryAsync(IEnumerable<int> entryIds)
        {
            var idList = entryIds?.Distinct().ToList() ?? new List<int>();
            if (idList.Count == 0) return new Dictionary<int, List<UploadedReceipt>>();

            using var db = _dbFactory.CreateDbContext();

            var rows = await db.UploadedReceipts.AsNoTracking()
                .Where(r => r.LedgerEntryId != null && idList.Contains(r.LedgerEntryId.Value))
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
                .ToListAsync();

            return rows.GroupBy(r => r.LedgerEntryId!.Value)
                       .ToDictionary(g => g.Key, g => g.ToList());
        }

        // 날짜·금액이 일치하는 건을 한꺼번에 연결한다. 후보가 여럿이면 내역 문구로 좁힌다.
        public async Task<int> AutoMatchReceiptsAsync(int deptId)
        {
            var (receipts, entries) = await GetMatchingDataAsync(deptId);
            var pool = entries.ToList();
            int count = 0;

            foreach (var r in receipts)
            {
                var exact = pool.Where(e => e.Date.Date == r.ReceiptDate.Date && e.Expense == r.Amount).ToList();

                LedgerEntry? target = null;
                if (exact.Count == 1) target = exact[0];
                else if (exact.Count > 1 && !string.IsNullOrWhiteSpace(r.Description))
                {
                    string rd = r.Description.Replace(" ", "");
                    target = exact.FirstOrDefault(e => e.Description != null
                        && (e.Description.Replace(" ", "").Contains(rd) || rd.Contains(e.Description.Replace(" ", ""))));
                }

                if (target == null) continue;

                using var db = _dbFactory.CreateDbContext();
                var dbReceipt = await db.UploadedReceipts.FindAsync(r.Id);
                var dbEntry = await db.Transactions.FindAsync(target.Id);
                if (dbReceipt == null || dbEntry == null) continue;

                dbEntry.ReceiptPath = r.ImagePath;
                dbReceipt.LedgerEntryId = dbEntry.Id;
                dbReceipt.IsMatched = true;
                dbReceipt.SortOrder = 0;
                await db.SaveChangesAsync();

                pool.Remove(target);
                count++;
            }

            return count;
        }
    }
}
