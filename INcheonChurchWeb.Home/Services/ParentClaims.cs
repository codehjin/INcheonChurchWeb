using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace INcheonChurchWeb.Home.Services
{
    /// <summary>
    /// 로그인 쿠키에 담는 것: 계정 Id 와 비밀번호 지문(Stamp) 두 가지뿐.
    /// 부서·이름은 담지 않는다 — 요청마다 DB 의 계정에서 다시 읽는다.
    /// 쿠키는 Data Protection 으로 암호화·서명되어 브라우저에서 고칠 수 없다.
    /// </summary>
    public static class ParentClaims
    {
        public const string AccountId = "parent_account";
        public const string Stamp = "parent_stamp";

        public static ClaimsPrincipal ToPrincipal(ParentSession session)
            => new(new ClaimsIdentity(new[]
            {
                new Claim(AccountId, session.AccountId.ToString(CultureInfo.InvariantCulture)),
                new Claim(Stamp, session.Stamp)
            }, CookieAuthenticationDefaults.AuthenticationScheme));

        public static ParentSession? FromPrincipal(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true) return null;

            var id = principal.FindFirst(AccountId)?.Value;
            var stamp = principal.FindFirst(Stamp)?.Value;
            return int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId) && !string.IsNullOrEmpty(stamp)
                ? new ParentSession(accountId, stamp)
                : null;
        }
    }
}
