using System.Text.Encodings.Web;
using System.Text.Unicode;
using Church.Home.Data;
using INcheonChurchWeb.Home.Components;
using INcheonChurchWeb.Home.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// =========================================================
// 학부모 포털 (ijch-school.kro.kr)
//   home.db 하나만 읽기 전용으로 연다. church.db 는 모른다 — 서버에서는 마운트조차 되지 않는다.
//   Migrate() 를 부르지 않는다. home.db 는 재정앱이 만들고 채운다.
//
//   화면은 서버가 HTML 로 그려 보낸다 (static SSR, 실시간 연결 없음).
//   폰이 잠들었다 깨어나도 끊길 연결이 없고, 1GB 서버에서 접속자마다 회로를 붙잡지 않는다.
// =========================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// 한글을 &#xC544; 같은 문자 코드로 바꾸지 않고 그대로 내보낸다 (화면은 같고 HTML 이 2~3배 작다).
// < > & " 같은 HTML 특수문자는 여전히 바뀌어 나간다 — 본문에 태그를 적어도 글자로 보인다.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// 🔒 설정에서 Mode 를 빼먹어도 ReadOnly 로 고정된다 (HomeConnection)
var homeConnectionString = HomeConnection.ReadOnly(
    builder.Configuration.GetConnectionString("HomeConnection") ?? "Data Source=homedata/home.db",
    builder.Environment.ContentRootPath);
builder.Services.AddDbContextFactory<HomeDbContext>(options =>
    options.UseSqlite(homeConnectionString));

builder.Services.AddScoped<HomeReader>();

// 로그인 쿠키 암호화 키. 재정앱과 키를 나눠 쓰지 않는다 (ApplicationName 이 다르다).
var keysPath = builder.Configuration["DataProtection:KeysPath"]
               ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("INcheonChurchWeb.Home");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "ijch_parent";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // nginx 뒤에서는 https 로 보인다 (ForwardedHeaders)
        options.ExpireTimeSpan = TimeSpan.FromDays(60);                     // 학부모가 매번 비번을 찾지 않게
        options.SlidingExpiration = true;

        // 🔒 요청마다 계정을 다시 확인한다 — 정지·비번 재발급이 이미 로그인한 폰에도 바로 듣는다.
        options.Events.OnValidatePrincipal = async context =>
        {
            var session = ParentClaims.FromPrincipal(context.Principal);
            var reader = context.HttpContext.RequestServices.GetRequiredService<HomeReader>();
            if (session == null || await reader.RestoreAsync(session) == null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // 🔒 따로 열어 둔 곳(로그인·오류·healthz)을 빼면 전부 로그인해야 한다. 새 화면을 더해도 기본이 잠김.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// nginx 가 TLS 를 끝내고 넘겨준다. 컨테이너 포트는 127.0.0.1 에만 열려 있어 nginx 말고는 붙을 수 없다.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// 다른 사이트가 이 화면을 틀 안에 넣지 못하게, 링크를 눌러 나갈 때 주소가 따라가지 않게.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseStaticFiles();      // 로그인 화면도 css 를 써야 하므로 인증보다 앞
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// 배포 확인용 — home.db 를 읽을 수 있는지만 알려 준다 (내용·건수는 내보내지 않는다)
app.MapGet("/healthz", async (IDbContextFactory<HomeDbContext> dbFactory) =>
{
    try
    {
        using var db = dbFactory.CreateDbContext();
        await db.Notices.AnyAsync();
        return Results.Text("ok");
    }
    catch
    {
        return Results.Text("home.db unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    // 다른 사이트가 몰래 로그아웃시키지 못하게 (폼에 심은 토큰 확인)
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

app.MapRazorComponents<App>();

app.Run();

/// <summary>통합 테스트(WebApplicationFactory)가 이 앱을 띄울 수 있게.</summary>
public partial class Program { }
