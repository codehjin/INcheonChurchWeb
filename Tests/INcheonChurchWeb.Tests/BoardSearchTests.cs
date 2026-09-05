using INcheonChurchWeb.Models;

namespace INcheonChurchWeb.Tests;

/// <summary>
/// 연간계획·주간회의록 검색창(한 칸)에 들어온 입력을 토큰으로 해석하는 규칙을 고정한다.
/// 순수 함수라 DB 없이 검증된다.
/// </summary>
public class BoardSearchTests
{
    private static SearchToken One(string input)
    {
        var tokens = BoardSearch.Parse(input);
        return Assert.Single(tokens);
    }

    [Theory]
    [InlineData("성경학교", SearchTokenKind.Text)]
    [InlineData("2026-07-28", SearchTokenKind.Date)]
    [InlineData("2026.7.2", SearchTokenKind.Date)]
    [InlineData("2026/07/28", SearchTokenKind.Date)]
    [InlineData("2026-07", SearchTokenKind.YearMonth)]
    [InlineData("2026", SearchTokenKind.Year)]
    [InlineData("07.28", SearchTokenKind.MonthDay)]
    [InlineData("7-28", SearchTokenKind.MonthDay)]
    [InlineData("3분기", SearchTokenKind.Quarter)]
    [InlineData("Q3", SearchTokenKind.Quarter)]
    [InlineData("q1", SearchTokenKind.Quarter)]
    public void 입력_형태를_토큰_종류로_해석한다(string input, SearchTokenKind expected)
    {
        Assert.Equal(expected, One(input).Kind);
    }

    [Theory]
    [InlineData("1234")]        // 연도 범위(1900~2200) 밖
    [InlineData("13-45")]       // 월·일 범위 밖
    [InlineData("2026-13")]     // 13월
    [InlineData("abc")]
    public void 해석에_실패하면_텍스트로_떨어진다(string input)
    {
        // 사용자가 무엇을 치든 검색이 깨지지 않아야 한다
        Assert.Equal(SearchTokenKind.Text, One(input).Kind);
    }

    [Fact]
    public void 공백으로_나눠_여러_조건을_만든다()
    {
        var tokens = BoardSearch.Parse("성경학교 3분기");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(SearchTokenKind.Text, tokens[0].Kind);
        Assert.Equal("성경학교", tokens[0].Text);
        Assert.Equal(SearchTokenKind.Quarter, tokens[1].Kind);
        Assert.Equal(3, tokens[1].Quarter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 빈_입력은_토큰이_없다(string? input)
    {
        Assert.Empty(BoardSearch.Parse(input));
    }

    // ── 텍스트 매칭 ──────────────────────────────────────────────────
    [Fact]
    public void 텍스트는_여러_필드_중_하나만_맞아도_된다()
    {
        var t = One("성경학교");

        Assert.True(BoardSearch.MatchesText(t, "여름성경학교", null));
        Assert.True(BoardSearch.MatchesText(t, null, "겨울성경학교 준비"));
        Assert.False(BoardSearch.MatchesText(t, "달란트행사", "교사 회식"));
    }

    [Fact]
    public void 텍스트_매칭은_대소문자를_가리지_않는다()
    {
        Assert.True(BoardSearch.MatchesText(One("mt"), "교사훈련(MT)"));
    }

    // ── 날짜 매칭 ────────────────────────────────────────────────────
    private static readonly DateTime EventStart = new(2026, 7, 28);
    private static readonly DateTime EventEnd = new(2026, 8, 1);

    [Theory]
    [InlineData("2026-07-28", true)]   // 시작일
    [InlineData("2026-07-30", true)]   // 기간 중간
    [InlineData("2026-08-01", true)]   // 종료일
    [InlineData("2026-09-01", false)]
    [InlineData("2026-07", true)]      // 그 달에 걸침
    [InlineData("2026-08", true)]      // 종료일이 8월이라 걸침
    [InlineData("2026-06", false)]
    [InlineData("07.28", true)]        // 연도 무관, 월·일
    [InlineData("08.01", true)]
    [InlineData("07.01", false)]
    [InlineData("2026", true)]
    [InlineData("2025", false)]
    public void 기간_행사는_걸치기만_해도_찾힌다(string input, bool expected)
    {
        Assert.Equal(expected, BoardSearch.MatchesDate(One(input), EventStart, EventEnd));
    }

    [Fact]
    public void 종료일이_없으면_하루짜리로_본다()
    {
        var day = One("2026-07-28");

        Assert.True(BoardSearch.MatchesDate(day, EventStart));
        Assert.False(BoardSearch.MatchesDate(One("2026-07-29"), EventStart));
    }

    [Fact]
    public void 존재하지_않는_날짜는_아무것도_찾지_않는다()
    {
        // 2026-02-30 — 파싱은 되지만 실제로 없는 날. 예외 없이 false여야 한다
        var t = One("2026-02-30");

        Assert.Equal(SearchTokenKind.Date, t.Kind);
        Assert.False(BoardSearch.MatchesDate(t, new DateTime(2026, 2, 28)));
    }

    [Fact]
    public void 종료일이_시작일보다_빨라도_예외가_나지_않는다()
    {
        // 잘못 입력된 데이터가 검색을 터뜨리면 안 된다
        var t = One("2026-07-28");

        Assert.True(BoardSearch.MatchesDate(t, EventStart, new DateTime(2026, 1, 1)));
    }
}
