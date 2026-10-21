# Windows UI Automation adapters — implementation contract (Stage B / C)

These notes lock in the techniques already proven on the real VTW `U:` machine so
the real adapters reuse what worked instead of re-deriving it. All three adapters
use `System.Windows.Automation` (UIAutomationClient). No filesystem access on `U:`,
no SendKeys, no guessed coordinates.

## WindowsExplorerAdapter (Stage B, hardened Stage D/E)

- Start window: visible `CabinetWClass` windows in Z order (most recently used first);
  skip the agent's own folder (rows `cloudsave-agent*` / `CloudSave.Agent*`) and shell
  locations (내 PC, 홈, 네트워크, 휴지통 ...). Every candidate is logged (`explorer.candidate`).
- Rows: `DataItem`/`ListItem` elements of class `UIItem` (Explorer's `UIItemsView`), offscreen
  rows included. If those classes are absent, fall back to visible rows (positive rect), logged once.
- Classify with `DocumentClassifier.ClassifyRow(name, cellTexts)` (Core): cell texts are the row's
  descendant Names + ValuePattern values (type / size / date columns). `~$` lock files are ignored.
- `LogicalPath` = start location (address-bar path, else title) + `\` + names (UIA identity).
- Completeness: compare with the status bar "N개 항목"; if short, scroll the items view and re-read.
- Open a row: `ForceForeground` (AttachThreadInput), `ScrollItemPattern.ScrollIntoView`, re-read the
  rect, then double-click its centre only if `WindowFromPoint` resolves to the Explorer window.
- Enter folder: location (address/title, else listing) must change; then wait for the listing to settle.
- Back: invoke the Back button (`뒤로...` / "Alt+왼쪽" / "Back"); must arrive at the exact parent
  (address, else title + identical listing). If still in the child, Up once. Else `EXPLORER_LOCATION_LOST`.
- Re-read after every navigation — never cache AutomationElements across moves.

## WindowsOfficeAdapter (Stage C, hardened Stage D/E)

- PowerPoint window class `PPTFrameClass`; Excel `XLMAIN`.
- `PrepareForOpenAsync` (called before the open) records existing Office windows and dialogs.
  `WaitForDocumentAsync` accepts only a NEW window whose title starts with the file name
  (or, weaker, contains its base name); a pre-existing window only if Office replaced a blank
  document in it with this file. A user's own open document is never touched.
- Open-time prompts (`#32770` / `NUIDialog` of an Office process, not in the baseline):
  업데이트 안 함 / Don't Update, 읽기 전용 / Read Only. Nothing else.
- Protected View: invoke "Enable Editing" / `편집 사용` (the standard user action).
- `CloseWithoutSavingAsync`: `WindowPattern.Close`; Don't Save searched only in the document's
  window and its process's dialogs (never Save / Save As). Never force-kill; a window still open
  after the timeout is reported (`OFFICE_CLOSE_TIMEOUT`).

## WindowsAnalyzerAdapter (Stage C) — THE RISK GATE

The ribbon button (`PPTX analyzer` / `Excel analyzer`) is reliably found; the hard
part is the task-pane **`구조 분석 시작`** button, which lives in an Office.js WebView2.
Chromium/WebView2 does not build its UIA accessibility tree until an AT client asks,
and exposure is timing/focus-sensitive. **This is why v2.5 failed** — it scanned only
the PowerPoint window subtree. The v2 script that *succeeded* scanned the **desktop root**.

`EnsureAnalyzerOpenAsync`:
1. If the task-pane start button is already visible, use it.
2. Make the Office window foreground; select the **Home** tab (the analyzer group lives there).
3. Find the ribbon button by exact name (`PPTX analyzer` / `Excel analyzer`); invoke it.

Locating `구조 분석 시작` (ordered strategy):
1. Prefer a **desktop-root** UIA scan (`AutomationElement.RootElement`), then filter
   candidates to those whose `BoundingRectangle` falls inside the Office window bounds.
   (This is the step v2.5 was missing.)
2. Wake the WebView a11y tree before/again between scans: set the Office window
   foreground and give focus to the task-pane region; allow a short settle.
3. Retry transient UIA errors (`ElementNotAvailable`, COM `UnauthorizedAccess`,
   "Unrecognized error") with the Core `RetryPolicy` backoff.
4. Only after a single candidate is uniquely identified, click the centre of its
   **current** BoundingRectangle as the fallback (never a guessed coordinate).

`RunAnalysisAsync` (completion rules live in Core: `AnalyzerPane.cs` / `AnalysisTracker`):
- Snapshot the pane (baseline), click `구조 분석 시작`.
- A new sign-in/consent window appearing => report interactive auth; wait (bounded) for
  the user to finish; never enter credentials.
- Read the pane texts each poll — only inside the task pane (visible `Chrome_WidgetWin*`
  child rect of the Office window), falling back to the Office window bounds.
- Succeeded = "분석 리포트 완성" + at least one "✅" and no "❌". A "❌" destination line =
  `UPLOAD_FAILED` (one automatic retry). "검사 중단" = `ANALYSIS_FAILED`. Button restored
  after the run but no readable result line = completed but `UPLOAD_UNVERIFIED`.

If, after all of the above, the start button still never exposes in C#, escalate to
Plan B: MSAA `AccessibleObjectFromWindow` on the WebView host, or invoke the single
proven PowerShell UIA click as a last-resort sub-step. Decide at the Stage C gate.
