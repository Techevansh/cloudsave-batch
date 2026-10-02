# CloudSave Batch / UI Agent

CloudSave Batch is a Windows **UI Automation** agent for Cloudium-protected `U:`
drive workflows. On `U:` the normal filesystem APIs are blocked
(`Get-ChildItem` → PermissionDenied, Node `fs.readdir` → EPERM), but Windows
File Explorer can still see and open the files. So this agent never touches the
filesystem on `U:` — it drives the **UI that Explorer already shows**.

See `CLAUDE_CODE_HANDOFF.md` for the full history, the approaches that failed,
and the pieces already proven on the real environment.

## What the full agent does (v3.0)

`agent/cloudsave-full-agent.ps1`, launched by `Run-CloudSave-Full-Agent.bat`:

1. Picks the File Explorer window you already opened on the start folder.
2. Reads its rows through UI Automation (no filesystem access on `U:`).
3. Classifies rows: Office (`.pptx` / `.xlsx` / `.xls`), folder, or skip.
4. Opens each Office file through the Explorer UI (InvokePattern, not coordinates).
5. Finds the ribbon button — **PPTX analyzer** (PowerPoint) or **Excel analyzer**
   (Excel) — waits for the add-in to load, and opens the task pane.
6. Clicks the task-pane **structure-analysis** start button.
7. Handles both auth cases: cached login continues automatically; if a sign-in /
   consent window appears, the agent **waits for you to finish it** (it never
   stores or types a password).
8. Detects cycle completion (report text, or the start button returning).
9. Closes Office with `WindowPattern.Close` (never a process kill).
10. Records each file in a JSON state DB and, when enabled, recurses into subfolders.

**Emergency stop: press `F12` at any time.**

## Safety / rules enforced in code

- No `U:` filesystem access, no security bypass, no Cloudium workaround.
- No SendKeys, no Ctrl+A / F6, no guessed screen coordinates. The only click
  fallback is the center of an element's real UIA `BoundingRectangle`.
- No Korean string literals in the source (assembled from Unicode code points),
  so Windows PowerShell 5.1 + a UTF-8 Git checkout never misparses Korean.
- The original `cloudsave` and `cloudsave-excel` add-in repositories are
  reference-only and are never modified.

## Run order (do this, in order)

Always start from the local drive — `.bat` files cannot run from `U:`.

### 1. Verify syntax first (required before every run)

```
cd C:\cloudsave-batch
.\Run-Verify-Syntax.bat
```

This runs the real PowerShell parser over every script and reports any parser
error with its line/column. Only run the agent when it prints `all scripts
passed`.

### 2. Phase 1 — one PPTX, full cycle

The shipped `agent/full-agent.config.json` is pre-set for Phase 1:
`enableRecursion = false`, `maxOfficeFiles = 1`. Put **one** `.pptx` in a small
test folder, open that folder in File Explorer, then:

```
cd C:\cloudsave-batch
.\Run-CloudSave-Full-Agent.bat
```

Expected log shape:

```
START_EXPLORER ... office=1
SCAN  items=1 office=1
OPEN  sample.pptx
OFFICE_READY PowerPoint
ANALYZER_OPEN PPTX analyzer
ANALYZER_PANE ready
ANALYSIS_START sample.pptx
ANALYZING            (or INTERACTIVE_WINDOW if sign-in is needed)
ANALYSIS_FINISHED sample.pptx
OFFICE_CLOSE
SUCCESS sample.pptx
RUN_COMPLETE processed=1
```

### 3. Open up gradually

Only after Phase 1 succeeds, edit `agent/full-agent.config.json`:

- Phase 2–3: keep `enableRecursion: false`, raise `maxOfficeFiles` (e.g. `0` for
  all files in the folder), add an `.xlsx`.
- Phase 4+: set `enableRecursion: true` to walk subfolders.

The JSON state DB (`logs/state.json`) remembers completed files, so re-runs skip
them (`stateResume: true`).

## Config reference (`agent/full-agent.config.json`)

| key | meaning |
|-----|---------|
| `enableRecursion` | walk into subfolders (Phase 4+) |
| `maxOfficeFiles` | cap files processed this run; `0` = unlimited |
| `stateResume` | skip files already `SUCCESS` in `logs/state.json` |
| `enableProtectedViewEdit` | click Office "Enable Editing" when a file opens in Protected View |
| `dryRun` | classify and log only; do not open Office |
| `maxDepth` | recursion depth limit |
| `*TimeoutSec` | per-stage timeouts |

## Files

- `agent/cloudsave-full-agent.ps1` — the integrated v3.0 agent.
- `agent/verify-syntax.ps1` — parser gate (run before the agent).
- `agent/explorer-walker.ps1` — proven read-only Explorer reader.
- `agent/single-office-open.ps1` — proven single-file open test.
- `agent/powerpoint-pptx-analyzer-open.ps1` — proven analyzer-open test.
- `agent/single-pptx-structure-analysis.ps1` — proven start-button + auth test.

The `src/` Electron GUI and scanner are for ordinary local drives and are a
separate path from the `U:` UI agent.
