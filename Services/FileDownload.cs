using ClosedXML.Excel;
using Microsoft.JSInterop;

namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// 브라우저 파일 다운로드 헬퍼.
    ///
    /// 엑셀을 내려보내는 화면이 5곳인데, 모두 <c>MemoryStream → SaveAs → Base64 → JS 호출</c>을
    /// 각자 조립하고 있었다. (웹_상세_구조문서.md §6.7)
    /// 여기 모아 두면 다운로드 방식을 바꿀 때 한 곳만 고치면 된다.
    ///
    /// JS 쪽 <c>window.downloadExcelFile</c>·<c>downloadJsonFile</c>은 <c>wwwroot/js/app.js</c>에만 둔다.
    /// 예전에는 Settings.razor에도 같은 함수가 인라인으로 선언되어 있었다.
    /// </summary>
    public static class FileDownload
    {
        /// <summary>ClosedXML 워크북을 xlsx로 내려보낸다.</summary>
        public static async ValueTask DownloadExcelAsync(this IJSRuntime js, XLWorkbook workbook, string fileName)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            await js.InvokeVoidAsync("downloadExcelFile", fileName, Convert.ToBase64String(stream.ToArray()));
        }

        /// <summary>이미 만들어진 xlsx 바이트를 내려보낸다.</summary>
        public static ValueTask DownloadExcelAsync(this IJSRuntime js, byte[] bytes, string fileName)
            => js.InvokeVoidAsync("downloadExcelFile", fileName, Convert.ToBase64String(bytes));

        /// <summary>JSON 문자열을 파일로 내려보낸다.</summary>
        public static ValueTask DownloadJsonAsync(this IJSRuntime js, string json, string fileName)
            => js.InvokeVoidAsync("downloadJsonFile", fileName, json);
    }
}
