using Microsoft.Data.Sqlite;

namespace INcheonChurchWeb.Home.Services
{
    /// <summary>
    /// 🔒 home.db 연결 문자열을 '읽기 전용'으로 고정한다.
    /// 설정에서 Mode 를 빼먹거나 다른 값을 적어도 여기서 ReadOnly 로 덮어쓴다.
    /// (서버에서는 폴더 자체도 :ro 로 마운트된다 — 두 겹)
    /// </summary>
    public static class HomeConnection
    {
        /// <param name="baseDir">상대 경로를 풀 기준 폴더 (보통 ContentRootPath). 실행 위치에 따라 다른 파일을 열지 않게.</param>
        public static string ReadOnly(string connectionString, string? baseDir = null)
        {
            var b = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly };

            if (baseDir != null && !string.IsNullOrEmpty(b.DataSource) && b.DataSource != ":memory:"
                && !Path.IsPathRooted(b.DataSource))
            {
                b.DataSource = Path.GetFullPath(Path.Combine(baseDir, b.DataSource));
            }

            return b.ToString();
        }
    }
}
