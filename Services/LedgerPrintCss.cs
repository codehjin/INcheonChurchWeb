namespace INcheonChurchWeb.Services
{
    /// <summary>
    /// 장부·영수증 인쇄 지면의 CSS. <b>단일 정의</b>다.
    ///
    /// ⚠️ 기준은 웹이다. 여기 값을 바꾸면 웹 인쇄물이 바뀐다.
    ///
    /// 두 곳이 이 문자열을 쓴다.
    ///   · 웹    : 팝업 문서를 만들 때 &lt;style&gt; 안에 그대로 넣는다 (MonthlyLedger)
    ///   · 모바일: 전용 인쇄 페이지가 &lt;style&gt;로 출력한다 (MobileLedgerPrint)
    /// </summary>
    public static class LedgerPrintCss
    {
        /// <summary>지면 규칙. @page 포함 — 새 문서(팝업)에서 쓸 때만 유효하다.</summary>
        public const string PageRules = @"
* { box-sizing: border-box !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
html, body { height: 100%; margin: 0 !important; padding: 0 !important; overflow: visible !important; }
@page { size: A4 portrait; margin: 25mm 10mm 5mm 10mm; }
body { font-family: 'Malgun Gothic', sans-serif; background-color: white !important; }
";

        /// <summary>표·영수증 그리드 규칙. 두 경로가 똑같이 쓴다.</summary>
        public const string SheetRules = @"
.page-break-before { page-break-before: always !important; break-before: page !important; }
.print-only-title { text-align:center; font-size:18pt; font-weight:bold; margin-bottom:15px; color: black; }

.ledger-table-print { width: 100%; border-collapse: collapse; font-size: 10pt; color: black; table-layout: auto; }
.ledger-table-print th, .ledger-table-print td { border: 1px solid #444; padding: 4px; text-align: center; word-break: keep-all; vertical-align: middle; }
.ledger-table-print th { background-color: #e9ecef !important; font-weight: bold; }
.money-cell-print { text-align: right !important; white-space: nowrap !important; }

.receipt-grid-1, .receipt-grid-2, .receipt-grid-4 { display: flex !important; flex-wrap: wrap !important; justify-content: space-between !important; align-content: flex-start; gap: 0; }
.receipt-box { page-break-inside: avoid; display: flex; flex-direction: column; color: black; margin: 0; padding: 0; }
.receipt-grid-1 .receipt-box { height: 266mm; width: 100%; }
.receipt-grid-2 .receipt-box { height: 266mm; width: 48.5%; }
.receipt-grid-4 { row-gap: 4mm !important; column-gap: 3% !important; }
.receipt-grid-4 .receipt-box { height: 131mm; width: 48.5%; }

.receipt-img-wrapper { flex: 1; display: flex; align-items: center; justify-content: center; border: 1px dashed #eee; overflow: hidden; background-color: white; }
.receipt-img-wrapper img { max-width: 100%; max-height: 100%; object-fit: contain; }
.receipt-empty { flex: 1; border: 1px dashed #eee; display: flex; align-items: center; justify-content: center; }
";

        /// <summary>팝업 문서용 전체 규칙.</summary>
        public static string All => PageRules + SheetRules;
    }
}
