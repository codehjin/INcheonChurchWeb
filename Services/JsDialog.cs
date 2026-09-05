using Microsoft.JSInterop;

namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// 브라우저 대화상자 헬퍼.
    ///
    /// <c>window.alert</c>·<c>window.confirm</c>은 <c>Components/App.razor</c>에서 SweetAlert2로
    /// 전역 오버라이드되어 있어, 여기를 거치면 앱 전체가 같은 모양의 대화상자를 쓴다.
    ///
    /// 예전에는 페이지마다 <c>ShowAlertAsync</c>를 각자 선언해 두었다(5곳 복제 —
    /// 웹_상세_구조문서.md §6.7). 이제 확장 메서드 하나로 모은다.
    /// </summary>
    public static class JsDialog
    {
        public static ValueTask AlertAsync(this IJSRuntime js, string message)
            => js.InvokeVoidAsync("alert", message);

        public static ValueTask<bool> ConfirmAsync(this IJSRuntime js, string message)
            => js.InvokeAsync<bool>("confirm", message);
    }
}
