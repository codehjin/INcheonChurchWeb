using Church.Home.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace INcheonChurchWeb.Data
{
    /// <summary>
    /// home.db 생성·마이그레이션. 재정앱 기동 시에만 부른다.
    /// 학부모앱은 home.db 를 읽기 전용으로만 열고, 이 코드를 갖고 있지도 않다.
    /// </summary>
    public static class HomeDbInitializer
    {
        public static void Initialize(HomeDbContext db)
        {
            // SQLite 는 파일은 만들어도 폴더는 만들지 않는다 (기본 경로 homedata/home.db)
            var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            if (!string.IsNullOrEmpty(path) && path != ":memory:")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }

            db.Database.Migrate();

            // 🔒 home.db 는 WAL 이면 안 된다.
            //   EF Core 는 SQLite 파일을 새로 만들 때 WAL 로 바꿔 둔다. 그런데 WAL 은 읽는 쪽도
            //   -wal · -shm 파일을 함께 보고 써야 한다. 학부모 컨테이너는 이 디렉터리를 :ro 로
            //   마운트하므로 그 파일들을 만들 수 없고, 재정앱이 연결을 다 닫아 파일이 지워지는
            //   순간 학부모앱이 DB를 열지 못한다.
            //   롤백 저널(DELETE)은 읽기에 잠금만 쓰므로 읽기 전용 마운트에서도 안전하다.
            //   쓰기는 공개 버튼을 누를 때뿐이라 동시성 손해도 없다.
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;");
        }
    }
}
