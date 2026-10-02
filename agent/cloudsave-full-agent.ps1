$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class CSNative {
    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

    public static IntPtr[] Windows() {
        var list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static string Title(IntPtr h) {
        var s = new StringBuilder(512);
        GetWindowText(h, s, s.Capacity);
        return s.ToString();
    }

    public static string Class(IntPtr h) {
        var s = new StringBuilder(256);
        GetClassName(h, s, s.Capacity);
        return s.ToString();
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    public static void DoubleClick(int x, int y) {
        Click(x, y);
        System.Threading.Thread.Sleep(130);
        Click(x, y);
    }

    public static bool StopPressed() {
        return (GetAsyncKeyState(0x7B) & 0x8000) != 0;
    }
}
'@

function U([int[]]$Codes) {
    return -join ($Codes | ForEach-Object { [char]$_ })
}

$KFolder = U @(0xD30C,0xC77C,0x20,0xD3F4,0xB354)
$KBack   = U @(0xB4A4,0xB85C)
$KStart  = U @(0xAD6C,0xC870,0x20,0xBD84,0xC11D,0x20,0xC2DC,0xC791)
$KBusy   = U @(0xBD84,0xC11D,0x20,0xC911,0x2E,0x2E,0x2E)
$KDontSave = U @(0xC800,0xC7A5,0x20,0xC548,0x20,0xD568)

$Script:Config = [pscustomobject]@{
    MaxDepth = 10
    OfficeOpenTimeoutSec = 90
    AnalyzerDiscoveryTimeoutSec = 45
    AnalyzerPaneTimeoutSec = 30
    AnalysisTimeoutSec = 600
    CloseTimeoutSec = 20
    DryRun = $false
}

$Script:LogDir = Join-Path $PSScriptRoot '..\logs'
New-Item -ItemType Directory -Force -Path $Script:LogDir | Out-Null
$Script:LogFile = Join-Path $Script:LogDir ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$Script:Processed = New-Object 'System.Collections.Generic.HashSet[string]'

function Log([string]$Message) {
    $Line = '[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $Message
    Write-Host $Line
    Add-Content -LiteralPath $Script:LogFile -Value $Line -Encoding UTF8
}

function Stop-IfRequested {
    if ([CSNative]::StopPressed()) {
        throw 'EMERGENCY_STOP_F12'
    }
}

function RootFromHandle([IntPtr]$Handle) {
    return [System.Windows.Automation.AutomationElement]::FromHandle($Handle)
}

function Get-ExplorerWindows {
    $Result = @()
    foreach ($Handle in [CSNative]::Windows()) {
        if ([CSNative]::Class($Handle) -eq 'CabinetWClass') {
            $Result += $Handle
        }
    }
    return $Result
}

function Get-DescendantText($Element) {
    $Parts = @()
    try {
        $All = $Element.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition
        )
        for ($i = 0; $i -lt $All.Count; $i++) {
            try {
                $Name = $All.Item($i).Current.Name
                if ($Name) { $Parts += $Name }
            } catch {}
        }
    } catch {}
    return ($Parts -join ' | ')
}

function Classify-Row($Element, [string]$Name) {
    $Ext = [IO.Path]::GetExtension($Name).ToLowerInvariant()

    if ($Ext -in @('.pptx', '.xlsx', '.xls')) {
        return 'OFFICE'
    }

    $Meta = Get-DescendantText $Element
    try { $Help = $Element.Current.HelpText } catch { $Help = '' }

    if (($Meta -and $Meta.Contains($KFolder)) -or ($Help -and $Help.Contains($KFolder))) {
        return 'FOLDER'
    }

    if ([string]::IsNullOrWhiteSpace($Ext)) {
        return 'FOLDER'
    }

    return 'SKIP'
}

function Read-ExplorerRows([IntPtr]$Handle) {
    $Root = RootFromHandle $Handle
    $All = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $Rows = @()
    $Seen = @{}

    for ($i = 0; $i -lt $All.Count; $i++) {
        $Element = $All.Item($i)

        try {
            $Type = $Element.Current.ControlType.ProgrammaticName
            $Name = $Element.Current.Name

            if ($Type -notin @('ControlType.DataItem', 'ControlType.ListItem')) { continue }
            if ([string]::IsNullOrWhiteSpace($Name)) { continue }

            $Rect = $Element.Current.BoundingRectangle
            if ($Rect.Width -le 0 -or $Rect.Height -le 0) { continue }

            $Key = $Type + '|' + $Name
            if ($Seen.ContainsKey($Key)) { continue }
            $Seen[$Key] = $true

            $Rows += [pscustomobject]@{
                Name = $Name
                Kind = Classify-Row $Element $Name
                Element = $Element
                X = [int]$Rect.X
                Y = [int]$Rect.Y
                W = [int]$Rect.Width
                H = [int]$Rect.Height
            }
        } catch {}
    }

    return $Rows
}

function Select-StartExplorer {
    $Candidates = @()

    foreach ($Handle in Get-ExplorerWindows) {
        try {
            $Rows = @(Read-ExplorerRows $Handle)
            $OfficeCount = @($Rows | Where-Object { $_.Kind -eq 'OFFICE' }).Count
            $FolderCount = @($Rows | Where-Object { $_.Kind -eq 'FOLDER' }).Count
            $Score = $Rows.Count + ($OfficeCount * 10) + ($FolderCount * 2)

            $Candidates += [pscustomobject]@{
                H = $Handle
                Rows = $Rows
                Score = $Score
                Title = [CSNative]::Title($Handle)
            }
        } catch {}
    }

    if (!$Candidates) {
        throw 'No readable File Explorer window found. Open the desired start folder first.'
    }

    $Selected = $Candidates | Sort-Object Score -Descending | Select-Object -First 1
    $OfficeCount = @($Selected.Rows | Where-Object { $_.Kind -eq 'OFFICE' }).Count
    $FolderCount = @($Selected.Rows | Where-Object { $_.Kind -eq 'FOLDER' }).Count

    Log ('START_EXPLORER "' + $Selected.Title + '" items=' + $Selected.Rows.Count + ' office=' + $OfficeCount + ' folders=' + $FolderCount)
    return $Selected.H
}

function Activate-Element($Item, [bool]$DoubleClick = $false) {
    Stop-IfRequested

    $Element = $Item.Element
    $Pattern = $null

    if (!$DoubleClick -and $Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$Pattern)) {
        ([System.Windows.Automation.InvokePattern]$Pattern).Invoke()
        return 'InvokePattern'
    }

    $X = [int]($Item.X + ($Item.W / 2))
    $Y = [int]($Item.Y + ($Item.H / 2))

    if ($DoubleClick) {
        [CSNative]::DoubleClick($X, $Y)
    } else {
        [CSNative]::Click($X, $Y)
    }

    return ('UIA_RECT@' + $X + ',' + $Y)
}

function WindowMap {
    $Map = @{}
    foreach ($Handle in [CSNative]::Windows()) {
        $Map[[string]$Handle.ToInt64()] = [CSNative]::Title($Handle)
    }
    return $Map
}

function Find-OfficeWindow([string]$Ext, [string]$BaseName, [int]$TimeoutSec) {
    $Deadline = (Get-Date).AddSeconds($TimeoutSec)

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested

        foreach ($Handle in [CSNative]::Windows()) {
            $Class = [CSNative]::Class($Handle)
            $Title = [CSNative]::Title($Handle)

            if ($Ext -eq '.pptx' -and $Class -eq 'PPTFrameClass' -and $Title -like ('*' + $BaseName + '*')) {
                return $Handle
            }

            if ($Ext -in @('.xlsx', '.xls') -and $Class -eq 'XLMAIN' -and $Title -like ('*' + $BaseName + '*')) {
                return $Handle
            }
        }

        Start-Sleep -Milliseconds 300
    }

    return [IntPtr]::Zero
}

function Prepare-OfficeWindow([IntPtr]$Handle) {
    try {
        $Root = RootFromHandle $Handle
        $Pattern = $null

        if ($Root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) {
            try {
                ([System.Windows.Automation.WindowPattern]$Pattern).SetWindowVisualState(
                    [System.Windows.Automation.WindowVisualState]::Maximized
                )
            } catch {}
        }
    } catch {}

    [CSNative]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 1200
}

function Find-ElementsByNameRegex([IntPtr]$Handle, [string]$Regex, [bool]$VisibleOnly = $false) {
    $Root = RootFromHandle $Handle
    $All = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $Rows = @()

    for ($i = 0; $i -lt $All.Count; $i++) {
        $Element = $All.Item($i)

        try {
            $Name = $Element.Current.Name
            if ([string]::IsNullOrWhiteSpace($Name)) { continue }
            if ($VisibleOnly -and $Element.Current.IsOffscreen) { continue }
            if ($Name -notmatch $Regex) { continue }

            $Rect = $Element.Current.BoundingRectangle

            $Rows += [pscustomobject]@{
                Name = $Name
                Id = $Element.Current.AutomationId
                Type = $Element.Current.ControlType.ProgrammaticName
                Offscreen = $Element.Current.IsOffscreen
                Element = $Element
                X = [int]$Rect.X
                Y = [int]$Rect.Y
                W = [int]$Rect.Width
                H = [int]$Rect.Height
            }
        } catch {}
    }

    return $Rows
}

function Find-VisibleExactName([IntPtr]$Handle, [string]$Name) {
    $Root = RootFromHandle $Handle
    $All = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $Rows = @()

    for ($i = 0; $i -lt $All.Count; $i++) {
        $Element = $All.Item($i)

        try {
            if ($Element.Current.IsOffscreen) { continue }
            if ($Element.Current.Name -ne $Name) { continue }

            $Rect = $Element.Current.BoundingRectangle
            if ($Rect.Width -le 0 -or $Rect.Height -le 0) { continue }

            $Rows += [pscustomobject]@{
                Name = $Element.Current.Name
                Id = $Element.Current.AutomationId
                Type = $Element.Current.ControlType.ProgrammaticName
                Element = $Element
                X = [int]$Rect.X
                Y = [int]$Rect.Y
                W = [int]$Rect.Width
                H = [int]$Rect.Height
            }
        } catch {}
    }

    return $Rows
}

function Get-WindowRect([IntPtr]$Handle) {
    try {
        $Root = RootFromHandle $Handle
        $Rect = $Root.Current.BoundingRectangle
        return [pscustomobject]@{ X=[int]$Rect.X; Y=[int]$Rect.Y; W=[int]$Rect.Width; H=[int]$Rect.Height }
    } catch {
        return $null
    }
}

function Find-GlobalVisibleName([IntPtr]$OfficeHandle, [string]$Name, [bool]$Contains = $false) {
    $OfficeRect = Get-WindowRect $OfficeHandle
    if ($null -eq $OfficeRect) { return @() }

    $Rows = @()

    try {
        $Desktop = [System.Windows.Automation.AutomationElement]::RootElement
        $All = $Desktop.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition
        )
    } catch {
        Log ('UIA_RETRY desktop FindAll failed: ' + $_.Exception.Message)
        Start-Sleep -Milliseconds 500
        return @()
    }

    for ($i = 0; $i -lt $All.Count; $i++) {
        $Element = $All.Item($i)
        try {
            if ($Element.Current.IsOffscreen) { continue }
            $CurrentName = $Element.Current.Name
            if ([string]::IsNullOrWhiteSpace($CurrentName)) { continue }

            $Matches = if ($Contains) { $CurrentName.Contains($Name) } else { $CurrentName -eq $Name }
            if (!$Matches) { continue }

            $Rect = $Element.Current.BoundingRectangle
            if ($Rect.Width -le 0 -or $Rect.Height -le 0) { continue }

            $CenterX = [double]$Rect.X + ([double]$Rect.Width / 2.0)
            $CenterY = [double]$Rect.Y + ([double]$Rect.Height / 2.0)

            if (
                $CenterX -lt $OfficeRect.X -or
                $CenterX -gt ($OfficeRect.X + $OfficeRect.W) -or
                $CenterY -lt $OfficeRect.Y -or
                $CenterY -gt ($OfficeRect.Y + $OfficeRect.H)
            ) { continue }

            $Rows += [pscustomobject]@{
                Name=$CurrentName
                Id=$Element.Current.AutomationId
                Type=$Element.Current.ControlType.ProgrammaticName
                Element=$Element
                X=[int]$Rect.X
                Y=[int]$Rect.Y
                W=[int]$Rect.Width
                H=[int]$Rect.Height
            }
        } catch {}
    }

    return $Rows
}

function Find-AnalyzerCandidates([IntPtr]$Handle, [string]$Ext) {
    $Regex = if ($Ext -eq '.pptx') {
        '(?i)^PPTX\s*analyzer$|PPTX.*analy|analy.*PPTX'
    } else {
        '(?i)^Excel\s*analyzer$|Excel.*analy|analy.*Excel'
    }

    return @(Find-ElementsByNameRegex $Handle $Regex $false)
}

function Select-AnalyzerCandidate($Candidates, [string]$Label) {
    if ($Candidates.Count -eq 0) { return $null }

    $Exact = @($Candidates | Where-Object { $_.Name -eq $Label })
    if ($Exact.Count -eq 1) { return $Exact[0] }

    $Visible = @($Candidates | Where-Object {
        !$_.Offscreen -and $_.W -gt 0 -and $_.H -gt 0
    })
    if ($Visible.Count -eq 1) { return $Visible[0] }

    if ($Candidates.Count -eq 1) { return $Candidates[0] }

    return $null
}

function Ensure-AnalyzerPane([IntPtr]$OfficeHandle, [string]$Ext) {
    Prepare-OfficeWindow $OfficeHandle

    $StartVisible = @(Find-GlobalVisibleName $OfficeHandle $KStart $false)
    if ($StartVisible.Count -gt 0) {
        Log 'ANALYZER_PANE already open'
        return
    }

    $Label = if ($Ext -eq '.pptx') { 'PPTX analyzer' } else { 'Excel analyzer' }
    $Deadline = (Get-Date).AddSeconds($Script:Config.AnalyzerDiscoveryTimeoutSec)
    $Candidate = $null
    $LastCount = -1

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested
        Prepare-OfficeWindow $OfficeHandle

        $StartVisible = @(Find-GlobalVisibleName $OfficeHandle $KStart $false)
        if ($StartVisible.Count -gt 0) {
            Log 'ANALYZER_PANE appeared while waiting'
            return
        }

        $Candidates = @(Find-AnalyzerCandidates $OfficeHandle $Ext)

        if ($Candidates.Count -ne $LastCount) {
            Log ('ANALYZER_CANDIDATES ' + $Label + ' count=' + $Candidates.Count)
            foreach ($Item in $Candidates) {
                Log ('  candidate type=' + $Item.Type + ' name="' + $Item.Name + '" id="' + $Item.Id + '" offscreen=' + $Item.Offscreen + ' rect=' + $Item.X + ',' + $Item.Y + ',' + $Item.W + ',' + $Item.H)
            }
            $LastCount = $Candidates.Count
        }

        $Candidate = Select-AnalyzerCandidate $Candidates $Label
        if ($null -ne $Candidate) { break }

        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $Candidate) {
        $Diag = @(Find-ElementsByNameRegex $OfficeHandle '(?i)PPTX|Excel|analy|Add-in|Add-ins' $false)
        Log ('ANALYZER_DIAG count=' + $Diag.Count)

        foreach ($Item in ($Diag | Select-Object -First 40)) {
            Log ('  diag type=' + $Item.Type + ' name="' + $Item.Name + '" id="' + $Item.Id + '" offscreen=' + $Item.Offscreen + ' rect=' + $Item.X + ',' + $Item.Y + ',' + $Item.W + ',' + $Item.H)
        }

        throw ('Analyzer ribbon button not found after waiting: ' + $Label)
    }

    Log ('ANALYZER_OPEN ' + $Label)
    $Method = Activate-Element $Candidate $false
    Log ('ANALYZER_OPEN_METHOD ' + $Method)
    Start-Sleep -Milliseconds 1500

    $Deadline = (Get-Date).AddSeconds($Script:Config.AnalyzerPaneTimeoutSec)

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested

        $StartVisible = @(Find-GlobalVisibleName $OfficeHandle $KStart $false)
        if ($StartVisible.Count -gt 0) {
            Log 'ANALYZER_PANE ready'
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw 'Analyzer task pane did not expose the start button in time.'
}

function Run-Analysis([IntPtr]$OfficeHandle, [string]$LogicalPath) {
    $Ext = [IO.Path]::GetExtension($LogicalPath).ToLowerInvariant()
    Ensure-AnalyzerPane $OfficeHandle $Ext

    $Start = @(Find-GlobalVisibleName $OfficeHandle $KStart $false)
    if ($Start.Count -ne 1) {
        throw ('Analysis start button count=' + $Start.Count)
    }

    $BaselineWindows = WindowMap
    $Method = Activate-Element $Start[0] $false
    Log ('ANALYSIS_START ' + $LogicalPath + ' method=' + $Method)

    $Deadline = (Get-Date).AddSeconds($Script:Config.AnalysisTimeoutSec)
    $SeenWorking = $false
    $ReportedAuth = @{}

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested

        $Ready = @(Find-GlobalVisibleName $OfficeHandle $KStart $false).Count -gt 0
        $Busy = @(Find-GlobalVisibleName $OfficeHandle $KBusy $false).Count -gt 0

        if (!$Ready -or $Busy) {
            $SeenWorking = $true
        }

        $NowWindows = WindowMap

        foreach ($Key in $NowWindows.Keys) {
            if (!$BaselineWindows.ContainsKey($Key) -and !$ReportedAuth.ContainsKey($Key)) {
                $Title = $NowWindows[$Key]

                if ($Title -and $Title -notmatch 'PowerShell') {
                    Log ('INTERACTIVE_WINDOW "' + $Title + '"')
                    Log 'AUTH_WAIT user may complete sign-in or consent manually'
                    $ReportedAuth[$Key] = $true
                }
            }
        }

        if ($SeenWorking -and $Ready) {
            Log ('ANALYSIS_FINISHED ' + $LogicalPath)
            return
        }

        Start-Sleep -Milliseconds 300
    }

    throw 'Analysis timeout. A sign-in or consent window may still be waiting.'
}

function Close-Office([IntPtr]$Handle) {
    try {
        $Root = RootFromHandle $Handle
        $Pattern = $null

        if ($Root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$Pattern)) {
            ([System.Windows.Automation.WindowPattern]$Pattern).Close()
        } else {
            throw 'No WindowPattern'
        }
    } catch {
        Log ('WARN close failed: ' + $_.Exception.Message)
        return
    }

    $Deadline = (Get-Date).AddSeconds($Script:Config.CloseTimeoutSec)
    $DismissedSavePrompt = $false

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested

        $Exists = $false
        foreach ($Current in [CSNative]::Windows()) {
            if ($Current -eq $Handle) {
                $Exists = $true
                break
            }
        }

        if (!$Exists) {
            Log 'OFFICE_CLOSED'
            return
        }

        if (!$DismissedSavePrompt) {
            $DontSave = @(Find-GlobalVisibleName $Handle $KDontSave $true)
            if ($DontSave.Count -gt 0) {
                Log ('CLOSE_PROMPT dont-save candidates=' + $DontSave.Count)
                $Method = Activate-Element $DontSave[0] $false
                Log ('CLOSE_PROMPT dismissed method=' + $Method)
                $DismissedSavePrompt = $true
            }
        }

        Start-Sleep -Milliseconds 300
    }

    Log 'WARN Office window still open after close timeout'
}

function Process-OfficeItem([IntPtr]$ExplorerHandle, $Item, [string]$LogicalPath) {
    if ($Script:Processed.Contains($LogicalPath)) { return }

    $Ext = [IO.Path]::GetExtension($Item.Name).ToLowerInvariant()
    $BaseName = [IO.Path]::GetFileNameWithoutExtension($Item.Name)

    Log ('OPEN ' + $LogicalPath)

    if ($Script:Config.DryRun) {
        $Script:Processed.Add($LogicalPath) | Out-Null
        return
    }

    Activate-Element $Item $true | Out-Null

    $OfficeHandle = Find-OfficeWindow $Ext $BaseName $Script:Config.OfficeOpenTimeoutSec
    if ($OfficeHandle -eq [IntPtr]::Zero) {
        throw ('Office window timeout for ' + $Item.Name)
    }

    Log ('OFFICE_READY class=' + [CSNative]::Class($OfficeHandle) + ' title="' + [CSNative]::Title($OfficeHandle) + '"')

    try {
        Run-Analysis $OfficeHandle $LogicalPath
        $Script:Processed.Add($LogicalPath) | Out-Null
        Log ('SUCCESS ' + $LogicalPath)
    } finally {
        Close-Office $OfficeHandle
    }
}

function Click-Back([IntPtr]$ExplorerHandle) {
    $Root = RootFromHandle $ExplorerHandle
    $All = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $Hits = @()

    for ($i = 0; $i -lt $All.Count; $i++) {
        $Element = $All.Item($i)

        try {
            $Name = $Element.Current.Name
            if ($Element.Current.IsOffscreen) { continue }
            if ($Name -ne $KBack -and $Name -ne 'Back') { continue }

            $Rect = $Element.Current.BoundingRectangle
            if ($Rect.Width -le 0 -or $Rect.Height -le 0) { continue }

            $Hits += [pscustomobject]@{
                Element = $Element
                X = [int]$Rect.X
                Y = [int]$Rect.Y
                W = [int]$Rect.Width
                H = [int]$Rect.Height
            }
        } catch {}
    }

    if (!$Hits) {
        throw 'Explorer Back button not found.'
    }

    Activate-Element $Hits[0] $false | Out-Null
    Start-Sleep -Milliseconds 700
}

function Enter-Folder([IntPtr]$ExplorerHandle, $Item) {
    $BeforeTitle = [CSNative]::Title($ExplorerHandle)
    Activate-Element $Item $true | Out-Null

    $Deadline = (Get-Date).AddSeconds(10)

    while ((Get-Date) -lt $Deadline) {
        Stop-IfRequested
        $NowTitle = [CSNative]::Title($ExplorerHandle)

        if ($NowTitle -ne $BeforeTitle) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    Start-Sleep -Milliseconds 500
}

function Walk-Folder([IntPtr]$ExplorerHandle, [string]$LogicalPath, [int]$Depth) {
    Stop-IfRequested

    if ($Depth -gt $Script:Config.MaxDepth) {
        Log ('SKIP_DEPTH ' + $LogicalPath)
        return
    }

    $Rows = @(Read-ExplorerRows $ExplorerHandle)
    Log ('SCAN "' + $LogicalPath + '" items=' + $Rows.Count)

    foreach ($Item in @($Rows | Where-Object { $_.Kind -eq 'OFFICE' })) {
        Stop-IfRequested

        $Key = if ($LogicalPath) {
            $LogicalPath + '\' + $Item.Name
        } else {
            $Item.Name
        }

        try {
            Process-OfficeItem $ExplorerHandle $Item $Key
        } catch {
            Log ('ERROR ' + $Key + ' :: ' + $_.Exception.Message)
        }
    }

    $FolderNames = @(
        $Rows |
        Where-Object { $_.Kind -eq 'FOLDER' } |
        ForEach-Object { $_.Name }
    )

    foreach ($FolderName in $FolderNames) {
        Stop-IfRequested

        $Fresh = @(
            Read-ExplorerRows $ExplorerHandle |
            Where-Object { $_.Kind -eq 'FOLDER' -and $_.Name -eq $FolderName } |
            Select-Object -First 1
        )

        if (!$Fresh) {
            Log ('WARN folder disappeared: ' + $FolderName)
            continue
        }

        $Child = if ($LogicalPath) {
            $LogicalPath + '\' + $FolderName
        } else {
            $FolderName
        }

        try {
            Log ('ENTER ' + $Child)
            Enter-Folder $ExplorerHandle $Fresh[0]
            Walk-Folder $ExplorerHandle $Child ($Depth + 1)
            Click-Back $ExplorerHandle
            Log ('BACK ' + $LogicalPath)
        } catch {
            Log ('ERROR_FOLDER ' + $Child + ' :: ' + $_.Exception.Message)
            try { Click-Back $ExplorerHandle } catch {}
        }
    }
}

Write-Host ''
Write-Host 'CloudSave Full UI Agent v2.5'
Write-Host 'Resilient task-pane UIA discovery with retry + safe Office close prompt handling.'
Write-Host 'Emergency stop: press F12 at any time.'
Write-Host 'Open the desired START folder in File Explorer before running.'
Write-Host ''

$ExplorerHandle = Select-StartExplorer
Log ('RUN_START log=' + $Script:LogFile)

try {
    Walk-Folder $ExplorerHandle '' 0
    Log ('RUN_COMPLETE processed=' + $Script:Processed.Count)
} catch {
    Log ('RUN_ABORT ' + $_.Exception.Message)
}

Write-Host ''
Write-Host ('Finished. Processed Office files: ' + $Script:Processed.Count)
Write-Host ('Log: ' + $Script:LogFile)
