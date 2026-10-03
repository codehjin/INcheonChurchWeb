using Church.Home.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// [미리보기] — 학부모앱이 실제로 쓰는 부품(Church.Home.Ui 의 NoticePaper)과 스타일로
    /// 통신문 한 장을 HTML 문서로 그린다. 편집 화면이 이 문서를 iframe 에 넣어 보여 준다.
    /// 학부모 화면의 스타일이 재정앱 화면과 섞이지 않게 문서째 따로 만든다.
    /// </summary>
    public class NoticePreviewRenderer
    {
        private const string ParentFonts =
            "https://fonts.googleapis.com/css2?family=Jua&family=Nanum+Myeongjo:wght@400;700;800&family=Nanum+Pen+Script&display=swap";

        private readonly IServiceProvider _services;
        private readonly ILoggerFactory _loggers;
        private readonly IWebHostEnvironment _env;

        public NoticePreviewRenderer(IServiceProvider services, ILoggerFactory loggers, IWebHostEnvironment env)
        {
            _services = services;
            _loggers = loggers;
            _env = env;
        }

        public async Task<string> RenderAsync(ParentPortalService.NoticePreview preview, string baseUri)
        {
            await using var renderer = new HtmlRenderer(_services, _loggers);
            var paper = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<NoticePaper>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(NoticePaper.Notice)] = preview.Notice,
                    [nameof(NoticePaper.Profile)] = preview.Profile,
                    [nameof(NoticePaper.DeptName)] = preview.DeptName,
                }));
                return output.ToHtmlString();
            });

            var root = baseUri.TrimEnd('/') + "/";
            return $$"""
                <!DOCTYPE html>
                <html lang="ko">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <base target="_blank">
                <link rel="stylesheet" href="https://cdn.jsdelivr.net/gh/orioncactus/pretendard@v1.3.9/dist/web/static/pretendard.min.css">
                <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css">
                <link rel="stylesheet" href="{{ParentFonts}}">
                <link rel="stylesheet" href="{{root}}{{Versioned("_content/Church.Home.Ui/home.css")}}">
                <link rel="stylesheet" href="{{root}}{{Versioned("_content/Church.Home.Ui/rich.css")}}">
                </head>
                <body>
                <div class="hp-shell">
                <header class="hp-topbar"><span class="hp-topbar-brand"><i class="bi bi-envelope-paper-heart"></i><span>인천중앙교회 교회학교</span></span></header>
                <main class="hp-main">{{paper}}</main>
                </div>
                </body>
                </html>
                """;
        }

        // 스타일을 고치면 주소도 바뀌게 (브라우저가 옛 css 를 물고 있지 않게)
        private string Versioned(string path)
        {
            try
            {
                var info = _env.WebRootFileProvider.GetFileInfo(path);
                return info.Exists ? $"{path}?v={info.LastModified.UtcTicks}" : path;
            }
            catch
            {
                return path;
            }
        }
    }
}
