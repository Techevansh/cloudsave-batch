$ErrorActionPreference = 'Stop'

$Target = Join-Path $PSScriptRoot 'cloudsave-full-agent.ps1'
$Tokens = $null
$Errors = $null

[System.Management.Automation.Language.Parser]::ParseFile(
    $Target,
    [ref]$Tokens,
    [ref]$Errors
) | Out-Null

if ($Errors.Count -gt 0) {
    Write-Host 'CloudSave Full Agent validation FAILED.' -ForegroundColor Red

    foreach ($ErrorItem in $Errors) {
        Write-Host (
            'Line ' + $ErrorItem.Extent.StartLineNumber +
            ', Col ' + $ErrorItem.Extent.StartColumnNumber +
            ': ' + $ErrorItem.Message
        ) -ForegroundColor Red
    }

    exit 1
}

Write-Host 'CloudSave Full Agent validation PASSED.' -ForegroundColor Green
exit 0
