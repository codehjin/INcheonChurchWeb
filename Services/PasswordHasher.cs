using System;
using System.Security.Cryptography;

namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// 🔐 비밀번호 해시 유틸리티 (PBKDF2 / HMAC-SHA256)
    ///
    /// 저장 형식:  pbkdf2$반복횟수$salt(base64)$hash(base64)
    /// 예)        pbkdf2$100000$8Kk2...$Yq9v...
    ///
    /// 외부 패키지 없이 .NET 내장 암호화를 쓴다(ASP.NET Core Identity와 같은 방식).
    ///
    /// ⚠️ 기존 데이터는 평문으로 저장되어 있다. Verify()가 평문도 함께 처리하며,
    ///    로그인 성공 시 호출부에서 해시로 다시 저장(자동 이행)한다.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2";
        private const int Iterations = 100_000;   // 반복 횟수 (높을수록 무차별 대입에 강함)
        private const int SaltSize = 16;          // 128비트
        private const int KeySize = 32;           // 256비트

        /// <summary>평문 비밀번호를 해시 문자열로 만든다.</summary>
        public static string Hash(string password)
        {
            if (password == null) password = "";

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        /// <summary>저장된 값이 이미 해시 형식인지 판별한다.</summary>
        public static bool IsHashed(string? stored)
            => !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix + "$", StringComparison.Ordinal);

        /// <summary>
        /// 입력한 비밀번호가 맞는지 확인한다.
        /// 저장값이 해시면 해시 검증, 평문이면 평문 비교(레거시 호환).
        /// </summary>
        public static bool Verify(string? password, string? stored)
        {
            if (stored == null) return false;
            password ??= "";

            // ── 레거시: 평문 저장분 ──
            if (!IsHashed(stored))
                return FixedTimeEquals(password, stored);

            // ── 해시 검증 ──
            var parts = stored.Split('$');
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[1], out int iterations)) return false;

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException) { return false; }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>문자열 비교도 타이밍 공격에 노출되지 않게 처리.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            var ba = System.Text.Encoding.UTF8.GetBytes(a);
            var bb = System.Text.Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length) return false;
            return CryptographicOperations.FixedTimeEquals(ba, bb);
        }
    }
}
