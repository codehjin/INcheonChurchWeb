if (typeof window.downloadJsonFile !== 'function') {
    window.downloadJsonFile = (fileName, jsonContent) => {
        const blob = new Blob([jsonContent], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.download = fileName;
        link.href = url;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    };
}

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

// 🖨️ 인쇄 — 팝업 창 대신 화면 밖 iframe에서 인쇄한다.
//    팝업 방식은 인쇄를 취소했을 때 미리보기 창이 남고(크롬), 모바일에서는 아예 차단된다.
//    iframe은 눈에 보이지 않으므로 취소해도 남을 것이 없다.
window.printPopup = function (htmlContent) {
    // 이전 인쇄에서 남은 프레임이 있으면 정리
    var stale = document.getElementById('ma-print-frame');
    if (stale && stale.parentNode) stale.parentNode.removeChild(stale);

    var f = document.createElement('iframe');
    f.id = 'ma-print-frame';
    f.setAttribute('aria-hidden', 'true');
    // display:none이면 브라우저가 내용을 그리지 않아 빈 종이가 나온다 → 화면 밖으로 밀어둔다
    f.style.cssText = 'position:fixed; left:-10000px; top:0; width:820px; height:1160px; border:0;';
    document.body.appendChild(f);

    var doc = f.contentDocument || (f.contentWindow && f.contentWindow.document);
    if (!doc) {
        alert('인쇄 화면을 준비하지 못했습니다.');
        return;
    }

    doc.open();
    doc.write(htmlContent);
    doc.close();

    var win = f.contentWindow;
    var cleaned = false;

    function cleanup() {
        if (cleaned) return;
        cleaned = true;
        setTimeout(function () {
            try { if (f.parentNode) f.parentNode.removeChild(f); } catch (e) { }
        }, 300);
    }

    // 영수증 이미지까지 다 그려진 뒤에 인쇄한다 (최대 5초 대기)
    function whenImagesReady(cb) {
        var imgs = [];
        try { imgs = Array.prototype.slice.call(doc.images || []); } catch (e) { }
        var pending = imgs.filter(function (im) { return !im.complete; });
        if (pending.length === 0) { cb(); return; }

        var left = pending.length;
        var fired = false;
        function one() { if (--left <= 0) done(); }
        function done() { if (fired) return; fired = true; cb(); }

        pending.forEach(function (im) {
            im.addEventListener('load', one, { once: true });
            im.addEventListener('error', one, { once: true });
        });
        setTimeout(done, 5000);
    }

    function go() {
        whenImagesReady(function () {
            try {
                win.addEventListener('afterprint', cleanup);
                win.focus();
                win.print();
            } catch (e) {
                cleanup();
                return;
            }
            // 크롬에서 print()는 대화상자가 닫힐 때까지 멈춰 있으므로,
            // 여기까지 왔다는 건 인쇄를 마쳤거나 취소했다는 뜻이다.
            setTimeout(cleanup, 1000);
        });
    }

    if (doc.readyState === 'complete') setTimeout(go, 200);
    else win.addEventListener('load', function () { setTimeout(go, 200); });
};
