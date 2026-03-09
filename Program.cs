using INcheonChurchWeb.Components;
using INcheonChurchWeb.Data;
using INcheonChurchWeb.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

// =========================================================
// [수정] 경로 변수 미리 준비 (CS8852 에러 해결)
// =========================================================
// 1. 변수를 먼저 선언합니다. (기본값은 null)
string? contentRoot = null;
string? webRoot = null;

// 2. Docker 환경(/app 폴더 존재)인지 확인 후 변수에 값 할당
if (Directory.Exists("/app/wwwroot"))
{
    // 작업 폴더를 /app으로 확실하게 고정
    Directory.SetCurrentDirectory("/app");

    // 변수에 경로 저장
    contentRoot = "/app";
    webRoot = "wwwroot";
}

// 3. 옵션 객체를 만들 때, 준비된 변수 값을 '한 번에' 넣어줍니다.
var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot, // 여기서 할당해야 에러가 안 납니다.
    WebRootPath = webRoot
};

var builder = WebApplication.CreateBuilder(options);
// =========================================================

// 1. 키 저장 경로 설정 (프로젝트 실행 폴더 내 'keys' 폴더)
var keyDirectory = Path.Combine(Directory.GetCurrentDirectory(), "keys");

// 2. 데이터 보호 서비스 등록 (중복 없이 딱 한 번만!)
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("INcheonChurchWeb");

// 서비스 등록
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// appsettings.json이나 시놀로지 환경변수에서 "DefaultConnection" 값을 가져옵니다.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=church.db";

// 가져온 연결 문자열(connectionString)로 DB를 연결합니다.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<AccountingService>();

var app = builder.Build();

// 파이프라인 설정
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// DB 자동 생성 및 초기 데이터 설정
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var accService = services.GetRequiredService<AccountingService>();

    db.Database.EnsureCreated();
    accService.EnsureDefaultUsersAsync().Wait();
    accService.EnsureDefaultMappingsAsync().Wait();
}

app.Run();