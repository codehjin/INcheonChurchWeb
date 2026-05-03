# DataCollectionHub 리팩토링 지시서

이 문서는 `DataCollectionHub.razor` 파일의 비대해진 코드를 정리하고, Blazor Server의 안정성을 높이기 위한 단계별 리팩토링 가이드입니다.

**⚠️ 코파일럿(Copilot)에게 내리는 기본 원칙**
- 기존의 기능(비즈니스 로직)과 화면 디자인(UI/CSS)은 **절대 변경하지 않습니다.**
- 오직 코드의 '구조 개선'과 '가독성 향상', '안정성 확보'에만 집중합니다.
- 한 번에 하나의 단계만 수행하며, 사용자의 피드백을 기다립니다.

---

## 1단계: 매직 넘버/스트링 상수화 및 중복 로직(회계연도) 분리

**[Copilot 지시용 프롬프트]**
> 현재 프로젝트에서 기능과 UI 스타일은 절대 변경하지 말고, 코드의 유지보수성 향상을 위해 다음 구조적 개선을 진행해 줘.
> 1. `Models/GlobalConstants.cs` 파일에 다음 항목들을 상수로 추출해 줘.
>    - 파일 업로드 제한 용량 (예: MaxSingleFileSize = 25 * 1024 * 1024, MaxMultiFileSize = 10 * 1024 * 1024 등)
>    - 트랜잭션 타입 및 카테고리 등 반복되는 매직 스트링 (예: "수입", "지출", "미분류")
> 2. `DataCollectionHub.razor` 파일 내 `LoadBankFiles`와 `HandleLedgerImport`에 완전히 중복되어 있는 회계연도 계산 로직 및 `GetQuarter`, `GetFiscalYearEndDate` 메서드를 추출해 줘.
> 3. 추출한 공통 로직을 `GlobalConstants.cs`의 정적 메서드나 별도의 `DateHelper` 클래스로 분리하고, 기존 컴포넌트에서는 이 공통 메서드를 호출하도록 코드를 수정해 줘.

---

## 2단계: 비대한 컴포넌트 분리 (DataCollectionHub.razor)

**[Copilot 지시용 프롬프트]**
> `DataCollectionHub.razor` 파일의 기능과 CSS 스타일, HTML 구조는 100% 동일하게 유지한 상태에서, 단일 책임 원칙에 따라 컴포넌트만 분리해 줘.
> 1. 다음 4개의 탭에 해당하는 영역을 각각 독립된 자식 컴포넌트 파일로 분리해 줘.
>    - `ReceiptSingleUpload.razor` (영수증 단일 탭)
>    - `ReceiptMultiUpload.razor` (영수증 일괄 탭)
>    - `BankImport.razor` (엑셀 내역 탭)
>    - `LedgerMatching.razor` (장부 매칭 탭)
> 2. `DataCollectionHub.razor` (부모 컴포넌트)는 전체 껍데기 UI(탭 메뉴)와 활성화된 탭 상태 관리만 담당하게 해 줘.
> 3. 부모와 자식 간에 필요한 데이터(예: CurrentUser, OcrUsage 등)는 `[Parameter]`와 `EventCallback`을 통해 주고받도록 통신 구조를 구성해 줘.

---

## 3단계: CSS 격리(Isolation) 및 JavaScript 분리

**[Copilot 지시용 프롬프트]**
> 방금 분리한 컴포넌트들 및 기존 파일들의 UI 디자인이나 기능은 전혀 변경하지 말고, 다음 정리 작업만 수행해 줘.
> 1. `DataCollectionHub.razor` 상단에 하드코딩되어 있던 `<style>` 태그 안의 CSS 클래스(`.hub-surface`, `.hub-card` 등)들을 모두 잘라내어, `DataCollectionHub.razor.css` 파일을 생성한 뒤 그곳으로 이동시켜 줘 (CSS Isolation 적용).
> 2. 자식 컴포넌트들로 분리된 HTML 요소에 필요한 스타일도 각각의 `.razor.css` 파일로 적절히 분배해 줘.
> 3. 기존 컴포넌트 하단에 `<script>` 태그로 작성된 `window.downloadExcelFile` 함수를 잘라내어 `wwwroot/js/app.js` 파일에 넣어줘 (파일이 없다면 새로 생성).
> 4. 프로젝트의 `App.razor` 파일 하단에서 `<script src="/js/app.js"></script>`를 로드하도록 수정하여, Blazor 표준 JS 인터롭 환경을 구성해 줘.

---

## 4단계: DB 동시성 문제 해결 (Blazor Server 안정성 확보)

**[Copilot 지시용 프롬프트]**
> Blazor Server의 동시성 오류(Concurrency Exception)를 방지하고 프로그램의 안정성을 높이기 위해 DbContext 주입 방식을 수정해 줘. 기능 변화는 없어야 해.
> 1. `DataCollectionHub.razor` 및 방금 분리된 자식 컴포넌트들에서 `@inject AppDbContext DbContext`로 직접 주입받던 코드를 모두 `@inject IDbContextFactory<AppDbContext> DbContextFactory`로 변경해 줘.
> 2. 컴포넌트 내부의 데이터를 조회하거나 저장하는 모든 메서드 안에서 `using var context = DbContextFactory.CreateDbContext();`를 사용하여 단발성으로 DB 컨텍스트를 생성하고 작업이 끝나면 안전하게 해제되도록 로직을 수정해 줘.
> 3. `Program.cs`의 서비스 등록 부분도 `AddDbContext`에서 `AddDbContextFactory`로 올바르게 변경되었는지 확인하고 수정해 줘.

---

## 5단계: 파일 처리 로직 공통화 및 Imports 정리

**[Copilot 지시용 프롬프트]**
> 기존 기능의 변경 없이, 중복 코드 제거 및 구조 개선을 위한 마무리 작업을 진행해 줘.
> 1. 여러 컴포넌트에 중복되어 있는 파일 업로드 경로 생성 로직(`Path.Combine(Env.WebRootPath, "uploads", "receipts")`) 및 물리적 파일 저장/이동/삭제 로직을 전담하는 `FileService.cs` 클래스를 `Services` 폴더에 생성해 줘.
> 2. 파일 처리가 필요한 자식 컴포넌트들(`ReceiptSingleUpload`, `ReceiptMultiUpload` 등)에서 새로 만든 `FileService`를 의존성 주입(DI) 받아 사용하도록 기존 코드를 리팩토링해 줘.
> 3. `Components_Imports.razor`와 `_Imports.razor`에 중복된 using 문들이 있다면, 루트 경로의 `_Imports.razor` 하나로 통합한 뒤 불필요한 파일은 제거하도록 처리해 줘.