using INcheonChurchWeb.Models;
using INcheonChurchWeb.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 영수증 저장 경로를 고정한다. (웹_상세_구조문서.md §6.5)
///
/// 예전에는 화면 세 곳(웹 단일/일괄, 모바일 촬영/일괄)이 각자 파일 저장과 DB 삽입을 조립했고,
/// 그중 어디도 이미지를 줄이지 않아 원본 사진이 그대로 쌓였다.
/// 지금은 <see cref="AccountingService.SaveUploadedReceiptAsync"/> 하나뿐이다.
/// </summary>
public class ReceiptSaveTests : IDisposable
{
    private const int DeptId = 3;

    private readonly TestDb _db = new();
    private readonly AccountingService _svc;
    private readonly FileService _files;
    private readonly string _root;

    public ReceiptSaveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "receipt-tests-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>지정한 크기의 JPEG 바이트를 만든다.</summary>
    private static byte[] MakeJpeg(int width, int height)
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder { Quality = 95 });
        return ms.ToArray();
    }

    private string PhysicalPath(string relative) =>
        Path.Combine(_root, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task 저장하면_파일과_DB_기록이_함께_생긴다()
    {
        var bytes = MakeJpeg(800, 600);

        var path = await _svc.SaveUploadedReceiptAsync(
            _files, bytes, "IMG_2841.jpg", DeptId,
            DateTime.Parse("2026-03-15"), 248_000, "○○마트");

        Assert.StartsWith("/uploads/receipts/", path);
        Assert.True(File.Exists(PhysicalPath(path)));

        using var db = _db.CreateDbContext();
        var saved = Assert.Single(db.UploadedReceipts.ToList());

        Assert.Equal(DeptId, saved.DepartmentId);
        Assert.Equal(new DateTime(2026, 3, 15), saved.ReceiptDate);
        Assert.Equal(248_000, saved.Amount);
        Assert.Equal("○○마트", saved.Description);
        Assert.Equal(path, saved.ImagePath);
        Assert.False(saved.IsMatched);
    }

    [Fact]
    public async Task 긴_변이_1200px를_넘으면_줄여서_저장한다()
    {
        var bytes = MakeJpeg(3000, 2000);

        var path = await _svc.SaveUploadedReceiptAsync(
            _files, bytes, "big.jpg", DeptId, DateTime.Today, 1000, null);

        using var saved = await Image.LoadAsync(PhysicalPath(path));

        Assert.Equal(1200, saved.Width);
        Assert.Equal(800, saved.Height);          // 비율 유지
    }

    [Fact]
    public async Task 작은_사진은_확대하지_않는다()
    {
        var bytes = MakeJpeg(640, 480);

        var path = await _svc.SaveUploadedReceiptAsync(
            _files, bytes, "small.jpg", DeptId, DateTime.Today, 1000, null);

        using var saved = await Image.LoadAsync(PhysicalPath(path));

        Assert.Equal(640, saved.Width);
        Assert.Equal(480, saved.Height);
    }

    [Fact]
    public async Task 내역이_비면_내역_없음으로_넣는다()
    {
        await _svc.SaveUploadedReceiptAsync(
            _files, MakeJpeg(100, 100), "a.jpg", DeptId, DateTime.Today, 1000, "   ");

        using var db = _db.CreateDbContext();
        Assert.Equal("내역 없음", db.UploadedReceipts.Single().Description);
    }

    [Fact]
    public async Task 이미지가_아닌_바이트도_예외_없이_저장된다()
    {
        // HEIC 등 ImageSharp가 못 읽는 형식 — 원본 그대로 저장되어야 한다
        var junk = new byte[] { 1, 2, 3, 4, 5 };

        var path = await _svc.SaveUploadedReceiptAsync(
            _files, junk, "photo.heic", DeptId, DateTime.Today, 1000, null);

        Assert.Equal(junk, await File.ReadAllBytesAsync(PhysicalPath(path)));
    }

    [Fact]
    public async Task PDF는_이미지_변환_없이_그대로_저장된다()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 };   // "%PDF"

        var path = await _svc.SaveUploadedReceiptAsync(
            _files, pdf, "receipt.pdf", DeptId, DateTime.Today, 1000, null);

        Assert.EndsWith(".pdf", path);
        Assert.Equal(pdf, await File.ReadAllBytesAsync(PhysicalPath(path)));
    }

    [Fact]
    public async Task 같은_초에_여러_장을_올려도_파일명이_겹치지_않는다()
    {
        // 예전 규칙은 초 단위라 한 파일을 덮어썼다
        var paths = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            paths.Add(await _svc.SaveUploadedReceiptAsync(
                _files, MakeJpeg(100, 100), $"IMG_{i}.jpg", DeptId, DateTime.Today, 1000, null));
        }

        Assert.Equal(5, paths.Distinct().Count());
        Assert.All(paths, p => Assert.True(File.Exists(PhysicalPath(p))));
    }

    [Fact]
    public async Task OCR_사용량은_없으면_0이다()
    {
        Assert.Equal(0, await _svc.GetOcrUsageAsync());

        using (var db = _db.CreateDbContext())
        {
            db.OcrUsages.Add(new OcrUsage { YearMonth = DateTime.Now.ToString("yyyy-MM"), UsageCount = 42 });
            db.SaveChanges();
        }

        Assert.Equal(42, await _svc.GetOcrUsageAsync());
        Assert.Equal(0, await _svc.GetOcrUsageAsync(DateTime.Now.AddMonths(-1)));
    }
}
