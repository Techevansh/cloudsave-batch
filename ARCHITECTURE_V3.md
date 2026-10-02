# CloudSave Agent v3 — Architecture & Delivery Plan

## Why v3 exists

The current v2.x prototype proved that the target workflow is possible, but it also exposed the wrong delivery model: a large PowerShell script was being patched interactively against a timing-sensitive Windows UI. That created regressions, parser/encoding issues, and repeated one-step fixes.

v3 is a controlled rewrite on a separate branch. The existing `main` branch is kept intact as a reference. The original add-in repositories remain read-only.

## Product goal

Given a File Explorer window already opened by the user at the desired Cloudium-backed folder:

1. Read visible folders/files through Windows UI Automation.
2. Traverse subfolders without using filesystem APIs on U:.
3. For each .pptx/.xlsx/.xls:
   - open the file from Explorer,
   - wait for the correct Office window,
   - open the correct existing Office add-in (`PPTX analyzer` / `Excel analyzer`),
   - locate and invoke `구조 분석 시작`,
   - support both cached-auth and interactive-auth states,
   - wait for the analysis/upload cycle to finish,
   - close the Office document without modifying the original,
   - continue.
4. Persist progress so interrupted runs can resume.
5. Stream structured progress to the Electron GUI.
6. Allow immediate cancellation.

## Non-goals / hard constraints

- Do not modify `Techevansh/cloudsave`.
- Do not modify `Techevansh/cloudsave-excel`.
- Do not read U: through Node fs, .NET File APIs, PowerShell Get-ChildItem, or direct path traversal.
- Do not use SendKeys, Ctrl+A, F6 navigation, or guessed screen coordinates.
- Do not bypass/disable Cloudium or corporate controls.
- Do not store passwords.
- Do not let UI timing exceptions crash the entire run.

## Architecture

### 1. Electron UI (existing)

The current Electron app remains the user-facing shell.

Responsibilities:
- Start / Pause / Stop
- Show current logical folder
- Show current file
- Success / failed / skipped counts
- Show auth-required state
- Show logs
- Launch the Windows Agent process

The GUI should communicate with the agent through JSON Lines over stdout/stdin.

### 2. Windows Agent — C#/.NET

PowerShell is no longer the primary runtime.

Reason:
- UI Automation is a native Windows/.NET API.
- PowerShell 5.1 introduced encoding/parser regressions.
- A typed state machine is easier to test and reason about.
- Cancellation, retries, timers, and structured logging are safer.

Target:
- .NET 8 Windows executable
- Publish as self-contained win-x64 so the user does not need to install the .NET runtime.

### 3. Core modules

#### ExplorerAdapter
- Discover readable Explorer windows.
- Select the start window using deterministic scoring.
- Read current rows via UI Automation.
- Classify rows as Folder / Office / Other.
- Activate a row through InvokePattern when available; otherwise use the row's actual BoundingRectangle.
- Enter a folder.
- Navigate Back.
- Re-read after every navigation to avoid stale UIA elements.

#### OfficeWindowAdapter
- Detect PowerPoint (`PPTFrameClass`) and Excel (`XLMAIN`) windows.
- Match the opened file by title.
- Wait for the window to become responsive.
- Maximize/foreground only when needed.
- Detect and handle save/discard prompts.
- Never force-kill unless a future explicit recovery policy permits it.

#### AnalyzerAdapter
- PowerPoint ribbon target: `PPTX analyzer`
- Excel ribbon target: `Excel analyzer`
- Task-pane target: `구조 분석 시작`

Discovery strategy:
1. Existing task pane already open? use it.
2. Search Office subtree for exact analyzer name.
3. Search desktop UIA for the task-pane control, but scope candidates to the Office window bounds.
4. Retry transient UIA COM/ElementNotAvailable/Unrecognized errors with bounded backoff.
5. Use actual BoundingRectangle only as a fallback after a unique element has been identified.

#### AuthMonitor
States:
- NotObserved
- CachedAuthLikely
- InteractiveAuthRequired
- WaitingForUser
- AuthCompleted
- AuthTimeout

It never enters credentials.

#### AnalysisMonitor
States:
- Idle
- StartRequested
- Working
- InteractiveAuth
- Completed
- Failed
- TimedOut

Completion signals can include:
- start button disappeared then returned,
- known completion text appears,
- known failure text appears.

No single signal is trusted by itself when UIA exposure is inconsistent.

#### TraversalEngine
Depth-first traversal.

Rules:
- Snapshot file names first.
- Process Office files.
- Snapshot folder names.
- For each folder, re-find by name immediately before entering.
- After return, verify parent context before continuing.
- Never retain AutomationElement references across navigation.

#### StateStore
Use SQLite or JSON initially.

Record:
- logicalPath
- extension
- status
- attempts
- lastError
- startedAt
- completedAt

This allows resume/retry without rescanning already successful items.

#### Orchestrator
Owns the state machine and cancellation token.

A single document cycle:

`Discover -> Open -> WaitOffice -> EnsureAnalyzer -> StartAnalysis -> AuthWait(if needed) -> WaitComplete -> Close -> Persist -> Next`

Every state has:
- timeout
- retry policy
- recovery action
- failure category

## Error taxonomy

Errors must be categorized, not emitted as raw generic exceptions:

- EXPLORER_NOT_FOUND
- EXPLORER_ROW_NOT_FOUND
- EXPLORER_NAVIGATION_TIMEOUT
- OFFICE_OPEN_TIMEOUT
- OFFICE_WRONG_WINDOW
- OFFICE_PROTECTED_VIEW
- ANALYZER_BUTTON_NOT_FOUND
- ANALYZER_TASKPANE_TIMEOUT
- TASKPANE_UIA_TRANSIENT
- ANALYSIS_BUTTON_NOT_FOUND
- AUTH_WAIT_TIMEOUT
- ANALYSIS_TIMEOUT
- OFFICE_CLOSE_TIMEOUT
- UIA_ELEMENT_STALE
- UIA_TRANSIENT
- USER_CANCELLED

Each category has a retry/recovery policy.

## Retry policy

Transient UIA errors:
- retry 250 ms, 500 ms, 1 s, 2 s, then fail the current operation.

Office/add-in loading:
- poll with bounded timeout.
- refresh the UIA tree on every attempt.

Stale element:
- never retry the same element object.
- re-discover by semantic identity.

## Safety behavior

- Global emergency stop available from the GUI and a fallback hotkey.
- No operation uses guessed coordinates.
- Any click based on coordinates is derived from a uniquely identified UIA element's current BoundingRectangle.
- If more than one candidate exists and cannot be disambiguated, do not click.
- If a save prompt appears, choose the no-save/discard path because the agent itself should not modify the source file.

## Delivery stages

### Stage A — compile-clean skeleton
- C# solution/project
- logging
- JSONL protocol
- cancellation
- state machine interfaces
- unit tests for state transitions

### Stage B — Explorer adapter
Acceptance:
- Reads the known U: Explorer page.
- Classifies the same rows the earlier PowerShell reader successfully classified.
- No direct filesystem call is made.

### Stage C — single PowerPoint cycle
Acceptance:
- Open one PPTX.
- Detect the correct PowerPoint window.
- Open `PPTX analyzer`.
- Open/locate task pane.
- Invoke `구조 분석 시작`.
- Handle cached or interactive auth.
- Wait for cycle completion.
- Close without saving.

### Stage D — single Excel cycle
Same acceptance flow for `Excel analyzer`.

### Stage E — traversal + resume
- nested folders
- failure isolation
- persistent state

### Stage F — Electron integration
- Start/Stop/Progress
- no PowerShell window required

### Stage G — packaging
- self-contained Windows build
- one launcher / installer

## Test matrix

Before asking the user to run a build, automated checks must pass:

1. Build succeeds.
2. Unit tests succeed.
3. Static analysis succeeds.
4. No PowerShell parser dependency in the primary path.
5. UIA mock/state-machine tests cover:
   - already-authenticated
   - auth-required
   - analyzer already open
   - analyzer delayed
   - task pane delayed
   - transient UIA exception
   - stale element
   - Office save prompt
   - analysis timeout
   - user cancellation
6. The user is asked to test only a packaged milestone, not an intermediate patch.

## Repository policy

- `main`: current working history / stable reference.
- `v3-agent-rewrite`: all v3 work until a milestone passes validation.
- Original add-in repos: read-only.

## Current known-good evidence from v2 work

The following are already proven in the target machine:
- U: Explorer rows can be read via Windows UI Automation.
- PPTX/XLSX classification is possible.
- A PPTX can be opened from Explorer.
- `PPTX analyzer` can be found and invoked.
- The analyzer task pane can open.
- `구조 분석 시작` has been observed and successfully clicked through UIA-derived coordinates.
- Office save/discard prompt can be detected and dismissed.

v3 should preserve these working techniques while replacing the fragile integration layer.

---

## Stage A — implementation decisions (done)

Stage A is implemented under `agent-v3/` and validated locally (build + 46 unit tests green) and in CI.

Two refinements were made to the plan above, both to strengthen "validate before the user runs":

1. **Core / Windows split.** The solution is now three projects:
   - `CloudSave.Agent.Core` (`net8.0`, platform-agnostic): orchestrator state machine,
     models, error taxonomy (`ErrorCodes`), `RetryPolicy`, `DocumentClassifier`, JSONL
     event protocol, and state stores. Builds and unit-tests on any OS / CI runner.
   - `CloudSave.Agent` (`net8.0-windows`): the exe entry point plus the UI Automation
     adapters (Stage B/C). This is the only Windows-only code.
   - `CloudSave.Agent.Tests` (`net8.0`): references Core only; the whole decision-logic
     matrix runs without a Windows desktop.
   This shrinks the untestable surface to the thin UIA glue and makes the state-machine
   test suite authoritative before any milestone build.

2. **Resume store = JSON, not SQLite.** `JsonStateStore` (atomic temp-file replace,
   tolerant load) keeps the self-contained publish free of native dependencies.

Phased-testing knobs are driven by environment variables so the launcher/GUI can set
them without rebuilding: `CLOUDSAVE_RECURSION`, `CLOUDSAVE_MAX_FILES` (set `1` for a
single-file milestone), `CLOUDSAVE_RESUME`, `CLOUDSAVE_MAX_DEPTH`, `CLOUDSAVE_STATE_FILE`.

**Stage C is the risk gate.** The one still-unsolved bug — the task-pane `구조 분석 시작`
button not appearing in the UIA tree — is a WebView2/Chromium accessibility issue, **not**
a PowerShell issue, so moving to C# does not fix it by itself. The proven v2 technique
(scan from the **desktop root**, filter to the Office window bounds, wake the WebView a11y
tree, retry transient UIA errors) must be the primary path, not a fallback. This is
documented as the adapter contract in `agent-v3/CloudSave.Agent/Windows/README_ADAPTERS.md`
and will be the first thing proven in Stage C.

### Test matrix status (Stage A)

46 tests passing, covering: resume skip / resume-off reprocess, Office-only selection,
per-file failure isolation, Office always closed (success / analysis throw / office-open
failure / cancellation), recursion on/off, nested traverse + GoBack, GoBack after subtree
failure, folder-enter failure isolation, folder-disappeared handling, max-depth, file cap,
interactive-auth surfacing, transient-retry-then-succeed, transient-exhaustion, pre-cancel
and mid-analysis cancel, failure recorded to state; plus `RetryPolicy`, `DocumentClassifier`,
and `JsonStateStore` unit tests.

---

## Stage C — PROVEN on the real VTW U: machine (2026-10-02)

The single-PPTX milestone completed end to end on the target environment:

```
explorer.start -> folder.scan(office=1) -> file.open -> office.ready(PowerPoint)
-> analyzer.open(PPTX analyzer) -> analyzer.pane "Task pane ready"
-> analysis.start -> analysis.working -> auth.window(Google) -> auth.interactive
-> file.complete -> office.closed -> agent.complete Processed=1 Failed=0
```

The WebView2 task-pane gate (the v2.5 blocker) is cracked. What fixed it:
- Match the start button by Contains("구조 분석 시작") — its accessible name carries
  the "🔍" emoji, so exact equality missed it.
- Wake the Office.js WebView2 accessibility tree with WM_GETOBJECT(UiaRootObjectId)
  on the Office window and its Chrome/WebView2 child windows before each scan.

Interactive auth also worked: with Google not signed in, the agent detected the
sign-in window, waited, and resumed after the user completed it — no credentials
stored.

Remaining phases: D (single Excel), E (multi-file + recursion + resume), F
(Electron GUI). The Excel and recursion code paths already exist (XLMAIN +
"Excel analyzer"; EnableRecursion/MaxOfficeFiles), so D/E are mostly validation.
Test on progressively larger TEST folders; the real work folder is the last step.
