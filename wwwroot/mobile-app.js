// 📱 화면 폭에 따라 웹 화면 ↔ 앱 화면을 자동으로 갈아끼우기 위한 뷰포트 감지
window.maViewport = {
    // 좁은 화면이거나, 터치 기기이면서 1024px 이하이면 모바일로 본다.
    // 두 번째 조건은 Galaxy Z Fold처럼 펼치면 700~900px대가 되는 기기를 위한 것이다.
    breakpoint: 640,
    touchBreakpoint: 1024,

    isMobile: function () {
        if (window.innerWidth <= this.breakpoint) return true;
        return window.innerWidth <= this.touchBreakpoint
            && window.matchMedia('(pointer: coarse)').matches;
    },

    // 리사이즈 시 .NET으로 알려준다. 같은 상태면 알리지 않는다.
    register: function (dotNetRef) {
        if (this._handler) {
            window.removeEventListener('resize', this._handler);
            window.removeEventListener('orientationchange', this._handler);
        }

        this._ref = dotNetRef;
        this._last = this.isMobile();

        var self = this;
        this._handler = function () {
            var now = self.isMobile();
            if (now === self._last) return;
            self._last = now;
            self._ref.invokeMethodAsync('OnViewportChanged', now);
        };
        window.addEventListener('resize', this._handler);
        window.addEventListener('orientationchange', this._handler);
        return this._last;
    },

    // 앱 화면일 때 레이아웃의 모바일 상단 헤더를 숨기기 위한 표식
    setAppRoute: function (isAppRoute) {
        document.body.classList.toggle('ma-app-route', !!isAppRoute);
    }
};


// 📄 인쇄 화면 — 인쇄를 끝내든 취소하든 끝났다는 사실을 .NET에 알린다.
// 브라우저마다 주는 신호가 달라서 afterprint / matchMedia / 포커스 복귀를 함께 본다.
window.maPrint = {
    run: function (dotNetRef) {
        var done = false;
        var mql = window.matchMedia ? window.matchMedia('print') : null;

        function onChange(e) { if (!e.matches) finish(); }
        function onFocus() { finish(); }

        function cleanup() {
            window.removeEventListener('afterprint', finish);
            window.removeEventListener('focus', onFocus);
            try {
                if (mql && mql.removeEventListener) mql.removeEventListener('change', onChange);
                else if (mql && mql.removeListener) mql.removeListener(onChange);
            } catch (e) { }
        }

        function finish() {
            if (done) return;
            done = true;
            cleanup();
            try { dotNetRef.invokeMethodAsync('OnPrintFinished'); } catch (e) { }
        }

        window.addEventListener('afterprint', finish);
        try {
            if (mql && mql.addEventListener) mql.addEventListener('change', onChange);
            else if (mql && mql.addListener) mql.addListener(onChange);
        } catch (e) { }

        window.print();

        // print()는 인쇄 대화상자가 닫힐 때까지 멈춰 있다.
        // 여기까지 왔다는 건 인쇄를 마쳤거나 취소했다는 뜻이므로 afterprint가 없어도 정리한다.
        setTimeout(finish, 1000);

        // 그래도 신호가 없는 브라우저 대비 (모바일 사파리 등)
        setTimeout(function () {
            if (!done) window.addEventListener('focus', onFocus, { once: true });
        }, 1500);
    }
};
