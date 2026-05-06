if (typeof window.downloadExcelFile !== 'function') {
    window.downloadExcelFile = (fileName, base64String) => {
        const link = document.createElement('a');
        link.download = fileName;
        link.href = `data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,${base64String}`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };
}

window.printPopup = function (htmlContent) {
    var printed = false;
    var w = window.open('', '_blank', 'width=1000,height=800');

    if (!w) {
        alert('팝업이 차단되었습니다. 브라우저에서 팝업을 허용해 주세요.');
        return;
    }

    w.document.open();
    w.document.write(htmlContent);
    w.document.close();
    w.focus();

    function doPrint() {
        if (printed) return;
        printed = true;
        w.print();
        // 사용자가 인쇄 대화상자에서 명시적으로 취소나 닫기를 누르도록 w.close() 제거
    }

    // onload 이벤트로 호출하거나, 이벤트 미발생 대비 1.5초 후 호출
    w.onload = function () { setTimeout(doPrint, 300); };
    setTimeout(doPrint, 1500);
};