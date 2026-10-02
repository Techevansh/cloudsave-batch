# CloudSave Batch / UI Agent 개발 인수인계 문서

> 목적: 이 문서 하나만 읽고도 Claude Code 또는 다른 개발 에이전트가 현재 상황을 이해하고, 기존 시행착오를 반복하지 않고 개발을 이어갈 수 있도록 정리한 핸드오프 문서.
>
> 기준 시점: 2026-10-02
>
> 중요: **기존 `Techevansh/cloudsave`, `Techevansh/cloudsave-excel` 저장소는 절대 수정하지 않는다.** 신규 개발은 `Techevansh/cloudsave-batch`에서만 진행한다.

---

## 1. 사용자가 원하는 최종 결과

사용자가 원하는 것은 단순한 로컬 폴더 업로더가 아니다.

기업 보안/가상 드라이브 환경의 `U:` 내부에서 Windows 탐색기로는 정상적으로 보이고 열리지만, 일반 파일시스템 API(Node.js, PowerShell `Get-ChildItem`, `stat`, `readdir`)로는 접근이 차단되는 Office 문서들을 대상으로 다음 작업을 자동화하려는 것이다.

최종 목표 흐름:

1. 사용자가 Windows 탐색기에서 원하는 시작 폴더를 직접 연다.
2. CloudSave Batch Agent가 **파일시스템 API가 아니라 Windows UI Automation**으로 탐색기에 표시된 항목을 읽는다.
3. 하위 폴더를 재귀적으로 순회한다.
4. `.pptx`, `.xlsx`, `.xls`만 대상으로 한다.
5. 각 Office 파일을 실제 PowerPoint/Excel로 연다.
6. PowerPoint에서는 리본의 **`PPTX analyzer`**, Excel에서는 **`Excel analyzer`**를 연다.
7. Task Pane의 **`구조 분석 시작`** 버튼을 실행한다.
8. 기존 Add-in 내부 로직이 OneDrive + Google Drive 업로드를 수행한다.
9. 로그인 토큰 캐시가 있으면 그대로 진행하고, 없으면 로그인/동의 창을 사용자가 직접 처리할 수 있도록 기다린다.
10. 분석/업로드 완료를 감지한다.
11. Office 파일을 닫는다.
12. 다음 파일 및 하위 폴더로 계속 진행한다.
13. 실패한 파일은 로그에 남기고 전체 작업은 가능하면 계속한다.
14. 사용자는 언제든 긴급 중지할 수 있어야 한다.

제품 개념은 다음 문구에 가깝다.

> 사용자가 지정한 로컬/가상 폴더의 구조와 Office 문서를 판독하여, 폴더 구조를 따라가며 각 Office 문서의 기존 CloudSave Add-in 동작을 자동 실행하는 UI 기반 배치 에이전트.

---

## 2. 절대 변경하면 안 되는 저장소

### `Techevansh/cloudsave`
PowerPoint용 기존 Add-in.

- 현재 PPTX 문서를 Office.js로 읽음
- Task Pane 이름: **PPTX 구조 분석기**
- 리본 버튼 표시명: **PPTX analyzer**
- Task Pane 실행 버튼: **구조 분석 시작**
- 내부적으로 OneDrive + Google Drive 업로드
- 인증 토큰을 localStorage에 캐시
- 로그인 캐시가 없으면 Office Dialog로 Microsoft/Google 로그인
- 사용자가 이미 로그인한 상태일 수도 있고 아닐 수도 있음
- **수정 금지**

### `Techevansh/cloudsave-excel`
Excel용 기존 Add-in.

- Task Pane 이름: **엑셀 구조 분석기**
- 리본 버튼 표시명: **Excel analyzer**
- Task Pane 실행 버튼: **구조 분석 시작**
- 기존 OneDrive + Google Drive 업로드 로직
- **수정 금지**

신규 에이전트는 위 두 Add-in을 외부에서 조작만 해야 한다.

---

## 3. 신규 저장소

### `Techevansh/cloudsave-batch`

로컬 경로:

```text
C:\cloudsave-batch
```

환경:

- Windows
- PowerShell 5.1 계열
- Node.js v24.18.0
- Git 2.55.0.windows.2
- Electron 설치됨
- 사용자는 개발 경험이 거의 없으므로 실행 명령은 항상 정확히 제공해야 함

기본 실행 시 PowerShell이 다른 경로에서 시작할 수 있으므로 항상:

```powershell
cd C:\cloudsave-batch
```

부터 안내하는 것이 안전하다.

사용자가 실수로 `PS C:\...>` 또는 `CD C:\...>` 프롬프트 문자까지 입력한 적이 있으므로, 입력해야 하는 실제 명령만 명확히 보여줘야 한다.

---

## 4. 실제 대상 환경

문제의 드라이브:

```text
VTW 서버 (U:)
```

대표 대상 경로:

```text
U:\부서 폴더\501. 공공부문 클라우드 네이티브 전문기술지원\10. 기술지원 2팀\099. 2팀_[개인폴더]\1. 백승훈 책임(H)
```

U:는 Cloudium 계열로 보이는 보안/가상 디스크다.

관찰된 특성:

- Windows 탐색기에서는 정상적으로 보임
- 폴더 더블클릭 및 파일 열기 가능
- PowerShell `Get-ChildItem`은 PermissionDenied / UnauthorizedAccess
- Node.js `fs.stat`, `fs.readdir`은 EPERM
- Explorer 주소창에 U: 경로를 직접 입력하는 방식도 제대로 동작하지 않는 경우가 있음
- Electron 표준 폴더 선택창에 U:가 안 보이는 경우가 있음
- U:에서 `.bat` 실행은 Cloudium이 차단함
- 드래그앤드롭으로 실제 경로/권한 전달을 기대했으나 이것도 차단됨
- **보안 우회, Cloudium 비활성화, 권한 회피 같은 방식은 사용하면 안 됨**

따라서 정답 방향은 **탐색기에 보이는 UI를 자동화하는 것**이다.

---

## 5. 실패한 접근들 — 다시 반복하지 말 것

### 5.1 직접 파일시스템 접근

Node.js:

- `fs.stat(U:\...)`
- `fs.readdir(U:\...)`

결과:

- `EPERM: operation not permitted`

PowerShell:

```powershell
Get-ChildItem -LiteralPath 'U:\...'
```

결과:

- PermissionDenied
- UnauthorizedAccessException
- PathNotFound가 섞여 나온 적도 있음

결론:

> U:를 일반 파일시스템으로 읽는 구조는 폐기.

---

### 5.2 U: 내부 BAT 실행

`CloudSave-Here.bat`를 U:에 두고 실행하려고 했음.

Cloudium 메시지:

> 디스크 드라이브에서 [.bat]와 같은 실행 파일의 동작을 지원하지 않습니다.

결론:

> U:에서 BAT 실행 불가. Agent는 반드시 로컬 `C:\cloudsave-batch`에서 실행.

---

### 5.3 탐색기 주소창 직접 이동

Clipboard + 주소창 입력, Shell/SendKeys 등으로 U: 전체 경로를 이동하려 했음.

문제:

- U:까지만 보이거나 엉뚱한 위치로 감
- 주소 입력 방식이 Cloudium 가상 경로 구조와 맞지 않음

결론:

> 사용자가 시작 폴더까지 직접 열어두고 Agent는 그 창부터 시작.

---

### 5.4 화면 좌표 추정

듀얼 모니터에서 탐색기 좌표를 추정해 `VTW 서버 (U:)`를 클릭하는 Visual Agent를 여러 버전 개발.

문제:

- 모니터 위치 영향
- 창 크기 영향
- 홈/G: 등 엉뚱한 항목 클릭
- 좌표 비율 추정 불안정
- 템플릿 매칭 score가 있어도 실제 클릭 좌표가 틀림

결론:

> 고정 좌표 기반 자동화는 주력 방식으로 사용하지 않는다.

단, UI Automation이 제공한 **실제 BoundingRectangle의 중심점 클릭**은 fallback으로 허용 가능하다. 이는 추정 좌표와 다름.

---

### 5.5 SendKeys / Ctrl+A / F6 / 방향키

Explorer Walker 초기 버전에서 사용.

문제:

- `Ctrl+A`가 탐색기 전체 항목을 선택
- 파일 경로들이 한 번에 Clipboard로 복사됨
- 전역 SendKeys 때문에 사용자의 키보드 입력이 이상해짐
- F6 포커스 순환이 Windows Explorer 버전에 따라 불안정

사용자가 매우 불편해했음.

결론:

> **SendKeys 기반 자동화 금지.**
>
> Ctrl+A, F6, 반복 방향키, 전역 키보드 입력을 자동화의 핵심으로 사용하지 않는다.

---

## 6. 검증에 성공한 핵심 기술

### 6.1 Windows UI Automation으로 U: 탐색기 항목 읽기 — 성공

`Explorer Reader v0.3 / v0.4.1` 계열에서 확인.

실제 성공 결과 예:

```text
Selected Explorer: 1. 백승훈 책임(H) - 파일 탐색기
Items: 33
Office: 14
Folder candidates: 4
Other: 15
```

실제로 다음과 같은 항목들을 읽음:

- ★ 심평원
- 000. old
- 여러 하위 폴더
- 여러 `.pptx`
- 여러 `.xlsx`
- HWP/MP4 등 OTHER

중요:

> U: 파일시스템 접근이 막혀 있어도 탐색기의 UI Automation Tree에서는 파일/폴더 이름을 읽을 수 있음.

이 방식이 현재 프로젝트의 핵심 기반이다.

---

### 6.2 UI Automation으로 Explorer의 Office 파일 열기 — 성공

`Run-Single-Office-Open.bat`

테스트 대상:

```text
Summary.pptx
```

결과:

- Explorer에서 Summary.pptx를 UI 기반으로 활성화
- 실제 PowerPoint가 열림
- 파일 하나만 열기 성공

즉:

> Explorer UI -> PPTX 실제 실행 단계는 검증 완료.

---

### 6.3 PowerPoint의 PPTX analyzer 열기 — 성공

`Run-PPTX-Analyzer-Open.bat`

PowerPoint UI에서 실제 `PPTX analyzer` 버튼을 탐색.

관찰된 후보 중:

```text
ControlType.Button | "PPTX analyzer"
```

실제 InvokePattern으로 실행 성공.

결과:

- 오른쪽 Task Pane이 열림
- Task Pane 제목: `PPTX 분석도구`
- 내부 제목: `PPTX 구조 분석기`
- 파란 버튼: `구조 분석 시작`

중요:

> PowerPoint Ribbon의 PPTX analyzer를 UI Automation으로 여는 것까지 검증 완료.

---

### 6.4 Task Pane의 구조 분석 시작 버튼 클릭 — 한 번 성공

`Run-Single-PPTX-Structure-Analysis.bat` v1.0.1 실행에서:

```text
Visible target candidates: 1
[1] ControlType.Text | name="구조 분석 시작" | rect=...
Activated using: Exact UIA rectangle click @ ...
```

즉 Task Pane WebView 내부 텍스트가 때때로 UI Automation Tree에서 보이고, BoundingRectangle 기반 클릭이 가능함.

다만 다음 실행에서는 후보가 0개가 되는 경우가 있었음.

가능 원인:

- Task Pane이 닫혀 있었음
- Task Pane 로딩 상태 차이
- WebView UIA 노출 타이밍
- PowerPoint 전면/최대화 여부
- Add-in 상태가 이미 분석 중/완료/로그인 대기 등 다른 state였음

이 부분은 상태 머신으로 처리해야 한다.

---

## 7. 인증 / 로그인 상태에 대한 중요한 요구

기존 Add-in은 localStorage에 Microsoft/Google token을 캐시한다.

따라서 에이전트는 다음 두 경우를 모두 지원해야 한다.

### 경우 A — 이미 로그인됨

- `구조 분석 시작` 클릭
- 로그인 창 없이 바로 파일 데이터 추출 및 업로드
- 완료 상태로 진행

### 경우 B — 로그인 안 됨 / 토큰 만료

- `구조 분석 시작` 클릭
- Office Dialog 또는 인증 창이 뜸
- Agent는 이것을 실패로 보지 말고 기다려야 함
- 사용자가 직접 로그인/동의
- Agent는 로그인 창이 닫히거나 분석이 재개되는 것을 감지
- 계속 진행

중요:

> Agent가 비밀번호를 저장하거나 입력하지 않는다.

---

## 8. PowerPoint Add-in 내부 실제 동작

`Techevansh/cloudsave/taskpane.html`의 핵심 UI:

```html
<h2>PPTX 구조 분석기</h2>
<button id="sync">🔍 구조 분석 시작</button>
```

옵션 기본값:

- 레이아웃 정합성 검사 = checked
- 슬라이드 관계 분석 = checked

`taskpane.js`:

- Office.onReady
- localStorage token restore
- `syncToCloud()`
- 로그인 캐시가 없으면 Microsoft / Google authentication
- PPTX 데이터 추출
- OneDrive + Google Drive 동시 업로드
- finally에서 버튼을 다시 활성화
- 버튼 텍스트가 다시 `구조 분석 시작`으로 돌아옴

따라서 **버튼이 다시 돌아오는 것**은 분석 cycle 종료 신호로 사용할 수 있다.

다만 WebView의 상태 문자열이 항상 UIA에 노출되는 것은 아니다.

---

## 9. Excel Add-in 내부 실제 동작

`Techevansh/cloudsave-excel/manifest-excel.xml`:

```text
CommandsGroup.Label = 문서도구
TaskpaneButton.Label = Excel analyzer
```

`excel.html`:

```html
<h2>엑셀 구조 분석기</h2>
<button id="sync">🔍 구조 분석 시작</button>
```

따라서 PPTX와 동일한 구조로 처리하되 Ribbon button label만:

```text
Excel analyzer
```

를 사용하면 된다.

---

## 10. 현재 cloudsave-batch 주요 파일

### 기존 Scanner / GUI

다음은 버리지 말 것.

- `src/scanner.js`
- `src/index.js`
- `src/main.js`
- `src/preload.cjs`
- `src/ui/*`

원래 로컬/일반 드라이브용 Scanner 및 Electron GUI가 있음.

하지만 U:에서는 직접 파일시스템 접근이 안 되므로 현재 UI Agent와는 별도 경로다.

---

### 개발 과정에서 생긴 진단 파일

- `agent/explorer-walker.ps1`
- `agent/single-office-open.ps1`
- `agent/office-ui-inspector.ps1`
- `agent/powerpoint-addin-probe.ps1`
- `agent/powerpoint-addin-menu-probe.ps1`
- `agent/powerpoint-pptx-analyzer-open.ps1`
- `agent/single-pptx-structure-analysis.ps1`

이 파일들은 각각 성공/실패했던 실험을 재현하는 데 도움이 된다.

특히 성공 로직을 `cloudsave-full-agent.ps1`로 옮길 때 참고해야 한다.

---

### 현재 통합 Agent

```text
agent/cloudsave-full-agent.ps1
Run-CloudSave-Full-Agent.bat
agent/full-agent.config.json
```

목표:

> Explorer -> Office -> Analyzer -> login if needed -> analysis -> close -> recurse

---

## 11. Full Agent v2.0 결과

작은 테스트 폴더에서 Office 파일 1개를 두고 실행.

성공:

```text
START_EXPLORER ... items=1 office=1 folders=0
SCAN items=1
OPEN ...pptx
```

실패:

```text
Analyzer ribbon button not uniquely found: PPTX analyzer count=0
```

즉 PPTX 파일 열기까지 성공.

---

## 12. Full Agent v2.1 결과

PowerPoint Add-in 로딩을 최대 30초 대기하도록 수정.

결과:

```text
CloudSave Full UI Agent v2.1
START_EXPLORER ...
SCAN items=1
OPEN ...pptx
ANALYZER_CANDIDATES PPTX analyzer count=0
...
Analyzer ribbon button not found after waiting: PPTX analyzer
RUN_COMPLETE processed=0
```

문제:

> 단일 테스트에서는 PPTX analyzer를 찾았는데 Full Agent에서는 PowerPoint를 연 직후 UIA Tree에서 보이지 않음.

추정 가능한 원인:

- PowerPoint window focus / activation
- PowerPoint window size
- Ribbon lazy loading
- Add-in ribbon command lazy loading
- 다른 PowerPoint window가 이미 열려 있어 잘못된 window handle 선택
- 파일이 Protected View / 로딩 중
- Ribbon tab/UI tree가 아직 생성되지 않음

---

## 13. Full Agent v2.2에서 발생한 최신 오류

v2.2에서 Office window maximize + 전면 활성화 + 추가 diagnostics를 넣으려 했음.

그러나 **PowerShell parser error로 실행 자체가 시작되지 않음.**

화면의 주요 오류:

```text
At C:\cloudsave-batch\agent\cloudsave-full-agent.ps1:603 char:19
+ Write-Host ('Log: '+$Script:LogFile)
The string is missing the terminator: '.

At ...cloudsave-full-agent.ps1:192 char:63
+ function Find-AnalyzerCandidates([IntPtr]$office,[string]$ext){
Missing closing '}' in statement block or type definition.
```

이 오류는 함수 구조가 진짜 깨졌을 가능성도 있지만, **이전에도 PowerShell 5.1이 UTF-8 without BOM + 한글 literal을 잘못 파싱하면서 파일 뒤쪽까지 연쇄 parser error를 낸 전례가 있음.**

v2.2에 직접 들어간 다음 문자열이 특히 의심됨:

```powershell
'(?i)PPTX|Excel|analy|추가 기능|Add-in'
```

이 프로젝트에서 이미 같은 종류의 문제를 겪었기 때문에:

> PowerShell 소스 안의 한글 literal을 없애거나, UTF-8 BOM을 확실히 사용하거나, C# helper/Unicode code points/영문 AutomationId 기반으로 바꿔야 한다.

### 중요 교훈

PowerShell 5.1 + Git checkout UTF-8 file 조합에서 한글 source literal을 넣는 것은 위험하다.

이미 `single-pptx-structure-analysis.ps1`에서 이 문제 때문에:

```powershell
[char]0xAD6C
...
```

형태로 한글 문자열을 Unicode code point로 조립하도록 수정한 적이 있다.

---

## 14. 현재 사용자의 불만 / 개발 방식 요구

사용자는 다음 점에 매우 불만을 느끼고 있다.

- 한 단계 테스트할 때마다 빨간 오류
- assistant가 매번 한 조각씩 코드 추가
- 같은 명령을 계속 반복
- 전체적인 예외/상태를 먼저 생각하지 않고 다음 단계에서 또 오류
- 백그라운드에서 계속 개발한다고 오해한 적이 있음

사용자가 원하는 개발 방식:

> **전체 프로그램을 먼저 끝까지 구현하고, 그 다음 실제 환경에서 실패하는 지점만 수정하는 방식.**

따라서 이후 Claude Code는:

1. 전체 architecture/state machine을 먼저 정리
2. 정적 검증
3. PowerShell parser 검증
4. 가능한 경우 Pester/Parser API로 script syntax 검증
5. 그 다음 사용자에게 실행 요청

순서로 진행하는 것이 좋다.

**사용자에게 실행시키기 전에 최소한 parser error는 자동으로 검출해야 한다.**

예:

```powershell
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
  'C:\cloudsave-batch\agent\cloudsave-full-agent.ps1',
  [ref]$tokens,
  [ref]$errors
)
$errors
```

PowerShell 5.1에서 실제 parse test를 통과한 코드만 사용자에게 전달하는 것이 바람직하다.

---

## 15. 권장 최종 Architecture

### A. Explorer UI Adapter

책임:

- 현재 사용자가 열어둔 Explorer window 선택
- ListItem/DataItem 읽기
- 항목 이름 / type metadata 읽기
- Office 파일 분류
- 폴더 분류
- item activation
- folder enter
- back
- 현재 folder identity 추적

절대 하지 말 것:

- U: `Get-ChildItem`
- `fs.readdir`
- Explorer address bar path 입력

---

### B. Office Window Adapter

PowerPoint:

- class: `PPTFrameClass`

Excel:

- class: `XLMAIN`

책임:

- 새로 열린 Office window를 정확히 특정
- file title match
- foreground/maximize
- Protected View 여부 검사
- ribbon ready 여부 대기
- Add-in button 검색

---

### C. Analyzer Adapter

PowerPoint:

```text
PPTX analyzer
```

Excel:

```text
Excel analyzer
```

Task Pane:

```text
구조 분석 시작
```

검색 우선순위:

1. 정확한 AutomationId
2. 정확한 Name
3. 부분 Name
4. UIA Tree descendant
5. 실제 BoundingRectangle fallback

무작위 좌표 금지.

---

### D. Authentication State Machine

상태 예:

```text
READY
ANALYSIS_CLICKED
AUTH_REQUIRED
AUTH_WAIT
ANALYZING
ANALYSIS_DONE
ANALYSIS_FAILED
CLOSE
NEXT
```

다음 상태 모두 고려:

- 로그인 캐시 존재
- Microsoft만 캐시 존재
- Google만 캐시 존재
- 둘 다 없음
- 토큰 만료
- 로그인 취소
- OAuth dialog error
- 네트워크 오류

---

### E. Traversal Engine

추천:

Depth First Search.

각 폴더에서:

1. current rows snapshot
2. Office files 먼저 처리
3. folder names만 별도 snapshot
4. 각 folder를 fresh UI lookup
5. enter
6. recurse
7. back
8. parent identity 검증

UI AutomationElement 객체는 navigation 후 stale 될 수 있으므로 오래 캐시하지 않는다.

---

### F. State Database / Resume

최종적으로 SQLite 또는 JSON state 필요.

파일시스템 path를 알 수 없으므로 key는:

```text
logicalExplorerPath + fileName
```

예:

```text
1. 백승훈 책임(H)\003...\file.pptx
```

저장 항목:

- logicalPath
- fileName
- extension
- status
- attemptedAt
- completedAt
- failureReason

재실행 시 완료 파일 skip 가능.

---

## 16. 안전 요구

### 긴급 정지

현재 Full Agent는 F12 계획.

반드시:

- 모든 while loop 안에서 polling
- 파일 열기 전
- analyzer 클릭 전
- folder enter 전
- back 전

에 stop check.

### Office 닫기

무조건 process kill 하지 말 것.

우선:

- WindowPattern.Close

만 사용.

저장 여부 prompt가 나오면 파일을 변경하지 않았기 때문에 정상적으로 닫혀야 하지만, prompt 발생 상황도 감지할 것.

### Corporate security

- Cloudium 우회 금지
- 권한 상승 트릭 금지
- 보안 프로그램 disable 금지
- 파일 복호화/보안 해제 금지

현재 방식은 사용자가 Explorer에서 허용된 방식으로 보는/여는 UI를 자동 조작하는 것이다.

---

## 17. Claude Code가 가장 먼저 해야 할 일

### 1순위 — 현재 Full Agent parser부터 고치기

파일:

```text
agent/cloudsave-full-agent.ps1
```

현재 v2.2는 parser error가 있음.

먼저:

- PowerShell parser 정적검사
- unmatched quote / brace 검사
- PowerShell 5.1 UTF-8/한글 literal 제거
- source에서 한글 literal을 Unicode codepoint 또는 English AutomationId 기반으로 바꾸기

### 2순위 — 성공했던 단일 PPTX analyzer 검색 로직을 그대로 통합

성공 파일:

```text
agent/powerpoint-pptx-analyzer-open.ps1
```

이 로직은 실제 PPTX analyzer button을 찾고 열었다.

Full Agent의 `Ensure-AnalyzerPane`을 새로 추측하지 말고, 이 성공한 로직을 재사용/함수화하는 것이 우선이다.

### 3순위 — 성공했던 Explorer reader 로직 유지

성공 파일:

```text
agent/explorer-walker.ps1
```

33개 항목을 성공적으로 읽었던 방법을 임의로 다시 바꾸지 말 것.

### 4순위 — single PPTX 전체 cycle 먼저 통합 테스트

작은 테스트 폴더에서 Office 1개.

목표:

```text
Explorer read
-> file open
-> PowerPoint ready
-> PPTX analyzer open
-> Task Pane open
-> structure analysis click
-> cached auth or interactive auth
-> wait
-> done
-> close
```

이 전체 cycle이 성공한 뒤에만 recursion 활성화.

### 5순위 — Excel cycle

PowerPoint가 안정화된 뒤:

```text
Excel analyzer
```

동일 pattern.

### 6순위 — recursion

마지막에 하위 폴더 순회 활성화.

---

## 18. 테스트 전략 권장

### Phase 1
폴더 내 PPTX 1개.

### Phase 2
PPTX 1개 + XLSX 1개.

### Phase 3
Office 3개 + OTHER 2개.

### Phase 4
하위 폴더 1개.

### Phase 5
2 depth folder.

### Phase 6
실제 `1. 백승훈 책임(H)` 전체.

실제 업무 폴더 전체를 디버깅 테스트로 사용하지 말 것.

---

## 19. 성공 기준 로그 예

최종적으로 아래와 비슷한 로그가 나오면 된다.

```text
RUN_START

SCAN ROOT
FOUND folder=2 office=3 skip=5

OPEN sample.pptx
OFFICE_READY PowerPoint
ANALYZER_READY PPTX analyzer
TASKPANE_READY
ANALYSIS_CLICK
AUTH cached
ANALYZING
ANALYSIS_DONE
OFFICE_CLOSE
SUCCESS sample.pptx

OPEN sample.xlsx
OFFICE_READY Excel
ANALYZER_READY Excel analyzer
TASKPANE_READY
ANALYSIS_CLICK
AUTH interactive
AUTH_DONE
ANALYZING
ANALYSIS_DONE
OFFICE_CLOSE
SUCCESS sample.xlsx

ENTER subfolder
...
BACK

RUN_COMPLETE
success=...
failed=...
skipped=...
```

---

## 20. 사용자 UX 최종 방향

사용자는 CLI를 계속 보고 싶어하지 않는다.

최종적으로 기존 Electron GUI와 Full Agent를 연결하는 것이 이상적이다.

GUI에서:

- 현재 Explorer 폴더 사용
- 시작
- 일시정지
- 중지
- 현재 파일
- 성공 수
- 실패 수
- 진행 로그

를 보여주는 구조가 적합하다.

하지만 **UI보다 먼저 Agent 안정성 확보가 우선**이다.

---

## 21. 현재 GitHub에서 확인해야 할 핵심 파일

```text
Techevansh/cloudsave-batch

agent/explorer-walker.ps1
agent/single-office-open.ps1
agent/powerpoint-pptx-analyzer-open.ps1
agent/single-pptx-structure-analysis.ps1
agent/cloudsave-full-agent.ps1
agent/full-agent.config.json

Run-CloudSave-Full-Agent.bat
README.md
```

참조 전용 저장소:

```text
Techevansh/cloudsave
Techevansh/cloudsave-excel
```

**참조만 하고 수정하지 않는다.**

---

## 22. 개발 원칙 요약

1. U: 파일시스템 API 접근을 다시 시도하지 않는다.
2. Explorer UI Automation을 기반으로 한다.
3. SendKeys를 사용하지 않는다.
4. 고정 화면 좌표를 사용하지 않는다.
5. UIA BoundingRectangle 기반 fallback만 허용한다.
6. 기존 Add-in 저장소를 수정하지 않는다.
7. 로그인 있음/없음을 둘 다 state로 처리한다.
8. 사용자에게 실행시키기 전에 PowerShell parser 검증을 한다.
9. 한글 literal과 PowerShell 5.1 encoding 문제를 피한다.
10. 이미 성공한 단일 테스트 코드를 Full Agent에 재사용한다.
11. 전체 cycle을 완성한 뒤 recursion을 켠다.
12. 실패 시 로그를 남기고 가능한 경우 다음 파일로 진행한다.
13. 긴급 중지 기능은 항상 유지한다.
14. 실제 전체 업무 폴더는 마지막 단계에서만 테스트한다.

---

## 23. 가장 중요한 현재 결론

이 프로젝트가 불가능한 것은 아니다.

이미 실제 환경에서 다음이 검증되었다.

- Cloudium U:의 Explorer UI 항목 읽기 성공
- 파일/폴더 이름 판독 성공
- PPTX/XLSX 대상 분류 성공
- Explorer에서 PPTX 열기 성공
- PowerPoint의 `PPTX analyzer` 실행 성공
- Task Pane 열기 성공
- Task Pane의 `구조 분석 시작` 위치 식별 및 클릭 성공 경험 있음

현재 문제는 **각 성공한 조각들을 안정적인 단일 state machine으로 통합하는 과정의 코드 품질/PowerShell parser/Office UI timing 문제**다.

따라서 Claude Code는 새로운 접근을 처음부터 또 발명하기보다:

> **성공한 조각들을 재사용하고, parser/static validation을 먼저 통과시킨 뒤, 전체 state machine을 안정화하는 것**

에 집중해야 한다.
