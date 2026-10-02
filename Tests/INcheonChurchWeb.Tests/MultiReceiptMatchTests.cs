using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 영수증 여러 장을 장부 한 건에 거는 동작을 고정한다.
///
/// 카드 결제는 3만원 한 건인데 영수증은 1만원짜리 3장으로 나오는 경우가 있다.
/// 연결은 UploadedReceipt.LedgerEntryId 로 하고, 장부의 ReceiptPath 는 증빙
/// 아이콘·모달이 쓰는 '대표 1장'으로 남는다.
///
/// 금액이 어긋나도 막지 않는다 — 부분 영수증·할인으로 딱 떨어지지 않는 경우가 있어
/// 경고만 돌려주고 연결은 진행한다.
/// </summary>
public class MultiReceiptMatchTests : IDisposable
{
    private const int DeptId = 3;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;
    private readonly FileService _files;
    private readonly string _root;

    public MultiReceiptMatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "multi-receipt-" + Guid.NewGuid().ToString("N"));
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

    private int 거래(decimal expense, string desc = "카드결제")
    {
        using var db = _db.CreateDbContext();
        var e = new LedgerEntry
        {
            DepartmentId = DeptId,
            Date = DateTime.Parse("2026-03-10"),
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

    /// <summary>영수증 1장과 그 실제 파일을 만든다.</summary>
    private int 영수증(decimal amount, string desc = "영수증")
    {
        using var db = _db.CreateDbContext();
        var r = new UploadedReceipt
        {
            DepartmentId = DeptId,
            ReceiptDate = DateTime.Parse("2026-03-10"),
            Amount = amount,
            Description = desc,
            ImagePath = "/uploads/receipts/r_" + Guid.NewGuid().ToString("N")[..8] + ".jpg",
            IsMatched = false
        };
        db.UploadedReceipts.Add(r);
        db.SaveChanges();

        var physical = _files.GetPhysicalPathFromRelative(r.ImagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllBytes(physical, new byte[] { 1, 2, 3 });

        return r.Id;
    }

    private List<UploadedReceipt> 붙은영수증(int entryId)
    {
        using var db = _db.CreateDbContext();
        return db.UploadedReceipts
            .Where(r => r.LedgerEntryId == entryId)
            .OrderBy(r => r.SortOrder)
            .ToList();
    }

    [Fact]
    public async Task 만원짜리_3장을_3만원_거래_한_건에_건다()
    {
        int entryId = 거래(30_000);
        int a = 영수증(10_000, "1번");
        int b = 영수증(10_000, "2번");
        int c = 영수증(10_000, "3번");

        var result = await _svc.MatchReceiptsAsync(_files, new[] { a, b, c }, entryId, "유년부");

        Assert.True(result.Ok);
        Assert.Equal(3, result.Linked);
        Assert.Equal(30_000, result.ReceiptTotal);
        Assert.Equal(30_000, result.EntryAmount);
        Assert.False(result.AmountDiffers);

        Assert.Equal(3, 붙은영수증(entryId).Count);
    }

    [Fact]
    public async Task 여러_장을_걸어도_장부에는_대표_한_장이_남는다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000), 영수증(10_000) };

        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        using var db = _db.CreateDbContext();
        var entry = db.Transactions.First(t => t.Id == entryId);

        Assert.False(string.IsNullOrEmpty(entry.ReceiptPath));
        Assert.Contains(붙은영수증(entryId), r => r.ImagePath == entry.ReceiptPath);
    }

    [Fact]
    public async Task 순서가_0부터_차례로_매겨진다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000), 영수증(10_000) };

        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        Assert.Equal(new[] { 0, 1, 2 }, 붙은영수증(entryId).Select(r => r.SortOrder).ToArray());
    }

    [Fact]
    public async Task 나중에_한_장을_더_걸면_뒤에_이어_붙는다()
    {
        int entryId = 거래(30_000);
        await _svc.MatchReceiptsAsync(_files, new[] { 영수증(10_000), 영수증(10_000) }, entryId, "유년부");
        await _svc.MatchReceiptsAsync(_files, new[] { 영수증(10_000) }, entryId, "유년부");

        var linked = 붙은영수증(entryId);
        Assert.Equal(3, linked.Count);
        Assert.Equal(new[] { 0, 1, 2 }, linked.Select(r => r.SortOrder).ToArray());
    }

    [Fact]
    public async Task 금액이_맞지_않으면_경고하되_연결은_한다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000) };   // 2만원뿐

        var result = await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        Assert.True(result.Ok);
        Assert.True(result.AmountDiffers);
        Assert.Equal(20_000, result.ReceiptTotal);
        Assert.Equal(30_000, result.EntryAmount);
        Assert.Equal(2, 붙은영수증(entryId).Count);   // 막지 않는다
    }

    [Fact]
    public async Task 연결된_영수증은_미연결_목록에서_빠진다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000), 영수증(10_000) };

        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        var (receipts, unmatched) = await _svc.GetMatchingDataAsync(DeptId);
        Assert.Empty(receipts);
        Assert.Empty(unmatched);
    }

    [Fact]
    public async Task 분리하면_붙은_영수증이_전부_미연결로_돌아온다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000), 영수증(10_000) };
        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        await _svc.RemoveReceiptAsync(entryId);

        Assert.Empty(붙은영수증(entryId));

        var (receipts, _) = await _svc.GetMatchingDataAsync(DeptId);
        Assert.Equal(3, receipts.Count);
        Assert.All(receipts, r => Assert.False(r.IsMatched));

        using var db = _db.CreateDbContext();
        Assert.Equal("", db.Transactions.First(t => t.Id == entryId).ReceiptPath);
    }

    [Fact]
    public async Task 분리해도_영수증_파일은_남는다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000) };
        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        var paths = 붙은영수증(entryId).Select(r => r.ImagePath).ToList();

        await _svc.RemoveReceiptAsync(entryId);

        foreach (var p in paths)
            Assert.True(File.Exists(_files.GetPhysicalPathFromRelative(p)), $"보존되어야 한다: {p}");
    }

    [Fact]
    public async Task 인쇄용_조회는_거래별로_순서대로_묶어_돌려준다()
    {
        int entryId = 거래(30_000);
        var ids = new[] { 영수증(10_000), 영수증(10_000), 영수증(10_000) };
        await _svc.MatchReceiptsAsync(_files, ids, entryId, "유년부");

        int other = 거래(5_000, "다른 거래");
        await _svc.MatchReceiptsAsync(_files, new[] { 영수증(5_000) }, other, "유년부");

        var map = await _svc.GetReceiptsByEntryAsync(new[] { entryId, other });

        Assert.Equal(3, map[entryId].Count);
        Assert.Single(map[other]);
        Assert.Equal(new[] { 0, 1, 2 }, map[entryId].Select(r => r.SortOrder).ToArray());
    }

    [Fact]
    public async Task 영수증을_고르지_않으면_연결하지_않는다()
    {
        int entryId = 거래(30_000);

        var result = await _svc.MatchReceiptsAsync(_files, Array.Empty<int>(), entryId, "유년부");

        Assert.False(result.Ok);
        Assert.Empty(붙은영수증(entryId));
    }

    [Fact]
    public async Task 없는_장부에는_연결하지_않는다()
    {
        var result = await _svc.MatchReceiptsAsync(_files, new[] { 영수증(10_000) }, 99_999, "유년부");

        Assert.False(result.Ok);

        var (receipts, _) = await _svc.GetMatchingDataAsync(DeptId);
        Assert.Single(receipts);   // 영수증은 그대로 미연결로 남는다
    }
}
