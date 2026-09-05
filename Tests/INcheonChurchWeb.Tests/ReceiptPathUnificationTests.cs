using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 영수증 경로가 ReceiptPath 하나로 관리된다는 것을 고정한다.
///
/// 예전에는 장부 직접 업로드는 ReceiptPath, 매칭 탭은 ReceiptUrl 로 나뉘어 있었다.
/// 그래서 미연결 조회가 ReceiptUrl 만 보는 바람에, PC 업로드로 영수증이 이미 붙은
/// 거래가 매칭 탭에 계속 '증빙 없음'으로 떴고, 거기서 매칭하면 한 거래에 두 경로가
/// 동시에 생겨 한쪽이 보이지 않는 유령 파일이 됐다.
/// </summary>
public class ReceiptPathUnificationTests : IDisposable
{
    private const int DeptId = 3;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;
    private readonly FileService _files;
    private readonly string _root;

    public ReceiptPathUnificationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "receipt-unify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var env = new TestEnv { WebRootPath = _root, ContentRootPath = _root };
        _svc = new AccountingService(_db, env);
        _files = new FileService(env);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private int 거래(decimal expense, string desc = "테스트", string date = "2026-03-10")
    {
        using var db = _db.CreateDbContext();
        var e = new LedgerEntry
        {
            DepartmentId = DeptId,
            Date = DateTime.Parse(date),
            FiscalYear = 2026,
            Type = "지출",
            Category = "행사비",
            Expense = expense,
            Description = desc
        };
        db.Transactions.Add(e);
        db.SaveChanges();
        return e.Id;
    }

    private int 미연결영수증(decimal amount, string desc = "테스트", string date = "2026-03-10")
    {
        using var db = _db.CreateDbContext();
        var r = new UploadedReceipt
        {
            DepartmentId = DeptId,
            ReceiptDate = DateTime.Parse(date),
            Amount = amount,
            Description = desc,
            ImagePath = "/uploads/receipts/r_" + Guid.NewGuid().ToString("N")[..6] + ".jpg",
            IsMatched = false
        };
        db.UploadedReceipts.Add(r);
        db.SaveChanges();
        return r.Id;
    }

    private string 경로of(int entryId)
    {
        using var db = _db.CreateDbContext();
        return db.Transactions.First(t => t.Id == entryId).ReceiptPath;
    }

    // ── 이번 일원화의 핵심 효과 ──────────────────────────────────
    [Fact]
    public async Task 증빙이_붙은_거래는_미연결_목록에서_빠진다()
    {
        int 붙은거래 = 거래(10_000, "이미 증빙 있음");
        int 빈거래 = 거래(20_000, "증빙 없음");

        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == 붙은거래).ReceiptPath = "/uploads/receipts/already.jpg";
            db.SaveChanges();
        }

        var (_, unmatched) = await _svc.GetMatchingDataAsync(DeptId);

        Assert.Single(unmatched);
        Assert.Equal(빈거래, unmatched[0].Id);
    }

    // ── 매칭 ────────────────────────────────────────────────────
    [Fact]
    public async Task 매칭하면_ReceiptPath에_경로가_들어간다()
    {
        int entryId = 거래(14_700, "네이버파이낸셜");
        int receiptId = 미연결영수증(14_700, "네이버파이낸셜");

        bool ok = await _svc.MatchReceiptAsync(_files, receiptId, entryId, "유년부");

        Assert.True(ok);
        Assert.False(string.IsNullOrEmpty(경로of(entryId)));

        using var db = _db.CreateDbContext();
        Assert.True(db.UploadedReceipts.First(r => r.Id == receiptId).IsMatched);
    }

    [Fact]
    public async Task 매칭된_거래는_다시_미연결로_잡히지_않는다()
    {
        int entryId = 거래(14_700, "네이버파이낸셜");
        int receiptId = 미연결영수증(14_700, "네이버파이낸셜");

        await _svc.MatchReceiptAsync(_files, receiptId, entryId, "유년부");

        var (receipts, unmatched) = await _svc.GetMatchingDataAsync(DeptId);

        Assert.Empty(receipts);
        Assert.Empty(unmatched);
    }

    [Fact]
    public async Task 자동매치도_ReceiptPath에_넣는다()
    {
        int entryId = 거래(2_300, "우리동네할인마트");
        미연결영수증(2_300, "우리동네할인마트");

        int count = await _svc.AutoMatchReceiptsAsync(DeptId);

        Assert.Equal(1, count);
        Assert.StartsWith("/uploads/receipts/", 경로of(entryId));
    }

    // ── 분리(삭제) ──────────────────────────────────────────────
    [Fact]
    public async Task 매칭_영수증을_분리하면_미연결로_돌아오고_파일은_남는다()
    {
        int entryId = 거래(2_300, "우리동네할인마트");
        int receiptId = 미연결영수증(2_300, "우리동네할인마트");

        await _svc.MatchReceiptAsync(_files, receiptId, entryId, "유년부");

        string path = 경로of(entryId);
        Directory.CreateDirectory(Path.GetDirectoryName(_files.GetPhysicalPathFromRelative(path))!);
        File.WriteAllBytes(_files.GetPhysicalPathFromRelative(path), new byte[] { 1, 2, 3 });

        await _svc.RemoveReceiptAsync(entryId);

        Assert.Equal("", 경로of(entryId));
        Assert.True(File.Exists(_files.GetPhysicalPathFromRelative(path)), "파일이 보존되어야 한다");

        using var db = _db.CreateDbContext();
        Assert.False(db.UploadedReceipts.First(r => r.Id == receiptId).IsMatched);

        var (receipts, unmatched) = await _svc.GetMatchingDataAsync(DeptId);
        Assert.Single(receipts);
        Assert.Single(unmatched);
    }

    [Fact]
    public async Task 짝이_없는_영수증을_분리하면_파일이_지워진다()
    {
        int entryId = 거래(10_000);
        string path = "/uploads/receipts/직접올린것.jpg";

        Directory.CreateDirectory(Path.GetDirectoryName(_files.GetPhysicalPathFromRelative(path))!);
        File.WriteAllBytes(_files.GetPhysicalPathFromRelative(path), new byte[] { 1, 2, 3 });

        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == entryId).ReceiptPath = path;
            db.SaveChanges();
        }

        await _svc.RemoveReceiptAsync(entryId);

        Assert.Equal("", 경로of(entryId));
        Assert.False(File.Exists(_files.GetPhysicalPathFromRelative(path)));
    }

    [Fact]
    public async Task 다른_거래가_같은_파일을_쓰면_지우지_않는다()
    {
        string 공유경로 = "/uploads/receipts/공유파일.jpg";
        int a = 거래(10_000, "A");
        int b = 거래(10_000, "B");

        Directory.CreateDirectory(Path.GetDirectoryName(_files.GetPhysicalPathFromRelative(공유경로))!);
        File.WriteAllBytes(_files.GetPhysicalPathFromRelative(공유경로), new byte[] { 1, 2, 3 });

        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == a).ReceiptPath = 공유경로;
            db.Transactions.First(t => t.Id == b).ReceiptPath = 공유경로;
            db.SaveChanges();
        }

        await _svc.RemoveReceiptAsync(a);

        Assert.Equal("", 경로of(a));
        Assert.Equal(공유경로, 경로of(b));
        Assert.True(File.Exists(_files.GetPhysicalPathFromRelative(공유경로)), "다른 거래가 참조 중이므로 남아야 한다");
    }

    [Fact]
    public async Task 파일이_이미_없어도_예외없이_경로만_비운다()
    {
        int entryId = 거래(10_000);

        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == entryId).ReceiptPath = "/uploads/receipts/없는파일.jpg";
            db.SaveChanges();
        }

        await _svc.RemoveReceiptAsync(entryId);

        Assert.Equal("", 경로of(entryId));
    }

    // ── 과거 데이터 호환 ────────────────────────────────────────
    [Fact]
    public async Task 옛_uploads_경로도_그대로_유효하다()
    {
        int entryId = 거래(10_000);
        string 옛경로 = "/uploads/유년부_2025-08-23_여름성경학교_미래체육연구.jpg";

        using (var db = _db.CreateDbContext())
        {
            db.Transactions.First(t => t.Id == entryId).ReceiptPath = 옛경로;
            db.SaveChanges();
        }

        // 증빙이 있는 것으로 인식되어 미연결 목록에서 빠진다
        var (_, unmatched) = await _svc.GetMatchingDataAsync(DeptId);
        Assert.Empty(unmatched);

        Assert.Equal(옛경로, 경로of(entryId));
    }
}
