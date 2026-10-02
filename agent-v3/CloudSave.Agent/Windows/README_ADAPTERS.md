# Windows UI Automation adapters — implementation contract (Stage B / C)

These notes lock in the techniques already proven on the real VTW `U:` machine so
the real adapters reuse what worked instead of re-deriving it. All three adapters
use `System.Windows.Automation` (UIAutomationClient). No filesystem access on `U:`,
no SendKeys, no guessed coordinates.

## WindowsExplorerAdapter (Stage B)

- Enumerate top-level windows; keep class `CabinetWClass` (File Explorer).
- Score each by rows read + Office count; pick the highest (the folder the user opened).
- Read rows: UIA descendants with ControlType `DataItem`/`ListItem`, non-empty Name,
  positive `BoundingRectangle`. De-duplicate by `type|name`.
- Classify with `DocumentClassifier` (Core). For dotted names that are actually
  folders, confirm via the UIA "file folder" type text / HelpText.
- `LogicalPath` = parent logical path + `\` + name (UIA identity, **not** a `U:` path).
- Activate a row: `InvokePattern` if present, else `SelectionItemPattern.Select`,
  else click the centre of its **current** `BoundingRectangle`.
- Enter folder: double-activate; wait for the window title / row signature to change.
- Back: invoke the Explorer **Back** button element (`뒤로` / "Back"); never Alt+Left.
- Re-read after every navigation — never cache AutomationElements across moves.

## WindowsOfficeAdapter (Stage C)

- PowerPoint window class `PPTFrameClass`; Excel `XLMAIN`. Match title to the file base name.
- `WaitForDocumentAsync`: poll up to the configured timeout for the matching window.
- Maximize + foreground only when needed.
- Protected View: if an "Enable Editing" / `편집 사용` button is present, invoke it
  (that is the standard Office button the user would click; not a security bypass).
- `CloseWithoutSavingAsync`: `WindowPattern.Close`. If a save/discard prompt appears,
  choose **Don't Save** (the agent never modifies the source file). Never force-kill.

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

`RunAnalysisAsync` (completion is multi-signal — no single signal is trusted):
- Click `구조 분석 시작`.
- A new sign-in/consent window appearing => report interactive auth; wait (bounded) for
  the user to finish; never enter credentials.
- Completion = known report text appears, OR the start button disappeared then returned
  (the add-in restores it in `finally`). Failure = known failure text.
- Map outcomes to `AnalysisResult` (+ `AuthenticationWasInteractive`).

If, after all of the above, the start button still never exposes in C#, escalate to
Plan B: MSAA `AccessibleObjectFromWindow` on the WebView host, or invoke the single
proven PowerShell UIA click as a last-resort sub-step. Decide at the Stage C gate.
