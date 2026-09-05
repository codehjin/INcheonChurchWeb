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

                string uploadFolder = Path.Combine(_env.WebRootPath, "uploads");
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
                entry.ReceiptPath = $"/uploads/{newFileName}";
                await db.SaveChangesAsync();
                return "OK";
            }
            catch (Exception ex) { return $"실패: {ex.Message}"; }
        }

        // 🚀 장부에서 영수증을 분리한다.
        //    경로가 ReceiptPath(장부 직접 업로드) / ReceiptUrl(매칭 탭 연결) 두 곳에 나뉘어 있어
        //    양쪽을 모두 정리한다. 예전에는 ReceiptPath 만 봐서, 매칭으로 붙인 파일이
        //    디스크에 그대로 남고 UploadedReceipts 행도 IsMatched=true 인 채 고아가 됐다.
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

            // 이 거래가 물고 있던 경로들 (중복 제거)
            var paths = new[] { entry.ReceiptPath, entry.ReceiptUrl }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .Distinct()
                .ToList();

            // 매칭으로 연결됐던 영수증은 미연결 목록으로 되돌리고, 그 파일은 보존한다.
            var keepFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths.Count > 0)
            {
                var linkedReceipts = await db.UploadedReceipts
                    .Where(r => paths.Contains(r.ImagePath))
                    .ToListAsync();

                foreach (var r in linkedReceipts)
                {
                    r.IsMatched = false;
                    keepFiles.Add(r.ImagePath);
                }
            }

            entry.ReceiptPath = "";
            entry.ReceiptUrl = "";

            // 🚀 DB를 먼저 확정한 뒤 파일을 지운다.
            //    반대 순서면 저장이 실패했을 때 경로만 남아 깨진 이미지가 된다.
            await db.SaveChangesAsync();

            foreach (var path in paths)
            {
                if (keepFiles.Contains(path)) continue;

                // 다른 거래가 아직 같은 파일을 참조 중이면 남긴다.
                bool stillReferenced = await db.Transactions
                    .AnyAsync(t => t.Id != id && (t.ReceiptPath == path || t.ReceiptUrl == path));
                if (stillReferenced) continue;

                // 다른 영수증 행이 같은 파일을 참조 중이어도 남긴다.
                if (await db.UploadedReceipts.AnyAsync(r => r.ImagePath == path)) continue;

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
        }

        // ══════════════════════════════════════════════════════════════
        // 🔗 장부 매칭 — 미연결 영수증 ↔ 증빙 없는 장부
        // ══════════════════════════════════════════════════════════════
        public async Task<(List<UploadedReceipt> Receipts, List<LedgerEntry> Unmatched)> GetMatchingDataAsync(int deptId)
        {
            using var db = _dbFactory.CreateDbContext();

            var receipts = await db.UploadedReceipts.AsNoTracking()
                .Where(r => r.DepartmentId == deptId && !r.IsMatched)
                .OrderByDescending(r => r.ReceiptDate)
                .ToListAsync();

            var unmatched = await db.Transactions.AsNoTracking()
                .Where(t => t.DepartmentId == deptId && (t.ReceiptUrl == null || t.ReceiptUrl == ""))
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            return (receipts, unmatched);
        }

        // 영수증 한 건을 장부 한 건에 연결한다. 파일명을 장부 내용으로 바꿔 나중에 알아보기 쉽게 한다.
        public async Task<bool> MatchReceiptAsync(FileService files, int receiptId, int entryId, string deptName)
        {
            using var db = _dbFactory.CreateDbContext();

            var receipt = await db.UploadedReceipts.FindAsync(receiptId);
            var entry = await db.Transactions.FindAsync(entryId);
            if (receipt == null || entry == null) return false;

            string oldPath = receipt.ImagePath;

            if (File.Exists(files.GetPhysicalPathFromRelative(oldPath)))
            {
                string safeDesc = string.Concat((entry.Description ?? "내역없음").Split(Path.GetInvalidFileNameChars())).Replace(" ", "");
                decimal amount = entry.Expense > 0 ? entry.Expense : entry.Income;
                string newName = $"{entry.Date:yyyyMMdd}_{deptName}_{entry.Category}_{amount}원_{safeDesc}_{receipt.Id}{Path.GetExtension(oldPath)}";

                try
                {
                    files.TryMoveByRelativePath(oldPath, newName, out var newPath);
                    entry.ReceiptUrl = newPath;
                    receipt.ImagePath = newPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"파일명 변경 실패: {ex.Message}");
                    entry.ReceiptUrl = oldPath;
                }
            }
            else
            {
                entry.ReceiptUrl = oldPath;
            }

            receipt.IsMatched = true;
            await db.SaveChangesAsync();
            return true;
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

                dbEntry.ReceiptUrl = r.ImagePath;
                dbReceipt.IsMatched = true;
                await db.SaveChangesAsync();

                pool.Remove(target);
                count++;
            }

            return count;
        }
    }
}
