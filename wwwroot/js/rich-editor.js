// =========================================================
// 가정통신문 서식 편집기 (Quill 2) — 인사말 · 안내 본문
//   - Quill 은 이 편집기를 처음 쓸 때만 CDN 에서 불러온다 (다른 화면은 영향 없음)
//   - 서식은 전부 class 로 남긴다 (ql-font-*, ql-size-*, ql-color-*, ql-bg-*, ql-align-*, ql-indent-*)
//     → 허용 목록(Church.Home.Data/RichText.cs)과 스타일(Church.Home.Ui/wwwroot/rich.css)이 같은 이름을 쓴다
//   - 내용이 바뀌면 잠시 뒤(그리고 편집기를 떠날 때) .NET 의 OnEditorChanged 로 올려 보낸다
//   - 저장·공개·미리보기 직전에는 화면이 richEditor.flushAll() 로 남은 것을 모두 올린다
// =========================================================
(function () {
    const QUILL = 'https://cdn.jsdelivr.net/npm/quill@2.0.3/dist/quill.js';
    const QUILL_CSS = 'https://cdn.jsdelivr.net/npm/quill@2.0.3/dist/quill.snow.css';
    const FONTS = 'https://fonts.googleapis.com/css2?family=Jua&family=Nanum+Myeongjo:wght@400;700;800&family=Nanum+Pen+Script&display=swap';
    const RICH_CSS = '/_content/Church.Home.Ui/rich.css';     // 본문 서식 — 학부모 화면과 같은 파일
    const EDITOR_CSS = '/rich-editor.css';                     // 툴바 한글 이름 · 색 견본

    // 툴바 — 글꼴 · 크기 / 굵게·기울임·밑줄 / 글자색 · 형광펜 / 말머리(점·번호)·들여쓰기 / 정렬 / 서식 지우기
    const TOOLBAR = [
        [{ font: [false, 'myeongjo', 'jua', 'pen'] }, { size: ['small', false, 'large', 'huge'] }],
        ['bold', 'italic', 'underline'],
        [{ color: [false, 'red', 'blue', 'green', 'orange', 'purple', 'gray'] },
         { background: [false, 'yellow', 'pink', 'lightblue', 'lightgreen'] }],
        [{ list: 'bullet' }, { list: 'ordered' }, { indent: '-1' }, { indent: '+1' }],
        [{ align: [] }],
        ['clean'],
    ];

    // 붙여 넣은 글에서도 이 서식만 남긴다 (제목·링크·이미지 등은 글자로만)
    const FORMATS = ['font', 'size', 'bold', 'italic', 'underline', 'color', 'background', 'list', 'indent', 'align'];

    let loading = null;

    function addCss(href) {
        if (document.querySelector(`link[data-rte="${href}"]`)) return;
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;
        link.dataset.rte = href;
        document.head.appendChild(link);
    }

    function load() {
        if (window.Quill) return Promise.resolve();
        if (loading) return loading;

        addCss(QUILL_CSS);
        addCss(FONTS);
        addCss(RICH_CSS);
        addCss(EDITOR_CSS);

        loading = new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = QUILL;
            s.onload = () => { register(); resolve(); };
            s.onerror = () => { loading = null; reject(new Error('편집기를 불러오지 못했습니다.')); };
            document.head.appendChild(s);
        });
        return loading;
    }

    function register() {
        const pick = (name, list) => {
            const a = Quill.import(name);
            if (list) a.whitelist = list;
            Quill.register(a, true);
        };
        pick('attributors/class/font', ['myeongjo', 'jua', 'pen']);
        pick('attributors/class/size', ['small', 'large', 'huge']);
        pick('attributors/class/color', ['red', 'blue', 'green', 'orange', 'purple', 'gray']);
        pick('attributors/class/background', ['yellow', 'pink', 'lightblue', 'lightgreen']);
        pick('attributors/class/align', ['center', 'right', 'justify']);
    }

    // 빈 편집기는 '' — 서버에서 빈 칸으로 본다
    function valueOf(q) {
        return q.getLength() <= 1 ? '' : q.getSemanticHTML();
    }

    function fill(q, html) {
        // 붙여 넣기 경로로 변환하되 커서·스크롤은 건드리지 않는다
        const delta = html ? q.clipboard.convert({ html: html }) : [];
        q.setContents(delta, 'silent');
        q.history.clear();
    }

    const editors = new Map();
    let seq = 0;

    window.richEditor = {
        async create(host, html, placeholder, dotnet) {
            await load();

            const box = document.createElement('div');
            host.innerHTML = '';
            host.appendChild(box);

            const q = new Quill(box, {
                theme: 'snow',
                placeholder: placeholder || '',
                formats: FORMATS,
                modules: { toolbar: TOOLBAR },
            });
            q.root.classList.add('hp-rich');
            fill(q, html);

            const id = 'rte' + (++seq);
            const state = { q, last: valueOf(q), timer: null };

            state.push = () => {
                clearTimeout(state.timer);
                state.timer = null;
                const v = valueOf(q);
                if (v === state.last) return Promise.resolve();
                state.last = v;
                return dotnet.invokeMethodAsync('OnEditorChanged', v);
            };

            q.on('text-change', (_d, _o, source) => {
                if (source === 'silent') return;
                clearTimeout(state.timer);
                state.timer = setTimeout(state.push, 400);
            });
            q.on('selection-change', (range) => { if (range === null) state.push(); });   // 편집기를 떠날 때 바로

            editors.set(id, state);
            return id;
        },

        set(id, html) {
            const s = editors.get(id);
            if (!s) return;
            fill(s.q, html);
            s.last = valueOf(s.q);
        },

        async flushAll() {
            await Promise.all([...editors.values()].map(s => s.push()));
        },

        destroy(id) {
            const s = editors.get(id);
            if (s) clearTimeout(s.timer);
            editors.delete(id);
        },
    };
})();
