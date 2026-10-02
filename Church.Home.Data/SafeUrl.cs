namespace Church.Home.Data
{
    /// <summary>
    /// 학부모 화면에 링크로 나가는 주소는 http(s) 절대주소만 허용한다.
    /// javascript: · data: 같은 주소가 href 에 들어가면 누르는 순간 스크립트가 돈다.
    /// 재정앱은 저장할 때, 학부모앱은 그릴 때 — 양쪽에서 같은 규칙으로 거른다.
    /// </summary>
    public static class SafeUrl
    {
        public static bool IsHttp(string? url)
            => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        /// <summary>허용되는 주소면 다듬어서, 아니면 null 을 돌려준다.</summary>
        public static string? OrNull(string? url)
            => IsHttp(url) ? url!.Trim() : null;
    }
}
