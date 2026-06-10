Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-EnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Default
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Test-Truthy {
    param([string]$Value)

    return @("1", "true", "yes", "y", "on") -contains $Value.ToLowerInvariant()
}

function Invoke-Luotsi {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    Write-Host ("+ {0} {1}" -f $script:LuotsiBin, ($Arguments -join " "))
    & $script:LuotsiBin @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Invoke-LuotsiAllowFailure {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    Write-Host ("+ {0} {1}" -f $script:LuotsiBin, ($Arguments -join " "))
    & $script:LuotsiBin @Arguments
    return $LASTEXITCODE
}

function Add-RunSummaryToGitHubStepSummary {
    $summaryPath = Join-Path $ArtifactsDir "run-summary.md"
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY) -or -not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
        return
    }

    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ""
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "## Luotsi Run Summary"
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ""
    Get-Content -LiteralPath $summaryPath | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

function Write-AndCheckRunSummaryPacket {
    if (-not (Test-Path -LiteralPath $ArtifactsDir -PathType Container)) {
        return
    }

    Invoke-Luotsi replay packet --artifacts $ArtifactsDir
    Invoke-Luotsi replay packet --artifacts $ArtifactsDir --check
    Add-RunSummaryToGitHubStepSummary
}

$script:LuotsiBin = Get-EnvValue -Name "LUOTSI_BIN" -Default "luotsi"
$DeviceQuery = Get-EnvValue -Name "LUOTSI_DEVICE_QUERY" -Default "state=online,type=physical,availability=available"
$ScenarioPath = Get-EnvValue -Name "LUOTSI_SCENARIO_PATH" -Default "examples/scenarios"
$TtlSec = Get-EnvValue -Name "LUOTSI_TTL_SEC" -Default "1800"
$ArtifactsDir = Get-EnvValue -Name "LUOTSI_ARTIFACTS_DIR" -Default "artifacts/luotsi-lab"
$JunitPath = Get-EnvValue -Name "LUOTSI_JUNIT_PATH" -Default (Join-Path $ArtifactsDir "junit.xml")
$DryRun = Get-EnvValue -Name "LUOTSI_DRY_RUN" -Default "false"

$DefaultOwner = "ci-local"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) {
    $attempt = if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ATTEMPT)) { "1" } else { $env:GITHUB_RUN_ATTEMPT }
    $DefaultOwner = "gh-actions-$($env:GITHUB_RUN_ID)-$attempt"
} elseif (-not [string]::IsNullOrWhiteSpace($env:BUILD_BUILDID)) {
    $DefaultOwner = "azure-pipelines-$($env:BUILD_BUILDID)"
} elseif (-not [string]::IsNullOrWhiteSpace($env:CI_PIPELINE_ID)) {
    $DefaultOwner = "ci-pipeline-$($env:CI_PIPELINE_ID)"
}
$Owner = Get-EnvValue -Name "LUOTSI_OWNER" -Default $DefaultOwner

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null
$junitDirectory = Split-Path -Parent $JunitPath
if (-not [string]::IsNullOrWhiteSpace($junitDirectory)) {
    New-Item -ItemType Directory -Force -Path $junitDirectory | Out-Null
}

Invoke-Luotsi version

if (Test-Truthy $DryRun) {
    Invoke-Luotsi scenario-validate --path $ScenarioPath
    Invoke-Luotsi run --path $ScenarioPath --dry-run --artifacts $ArtifactsDir
    exit 0
}

Invoke-Luotsi lab status --device-query $DeviceQuery
Invoke-Luotsi lab plan --device-query $DeviceQuery
Invoke-Luotsi scenario-validate --path $ScenarioPath
$runExitCode = Invoke-LuotsiAllowFailure run `
    --path $ScenarioPath `
    --device-query $DeviceQuery `
    --claim-device `
    --owner $Owner `
    --ttl-sec $TtlSec `
    --report-junit $JunitPath `
    --artifacts $ArtifactsDir
Write-AndCheckRunSummaryPacket
exit $runExitCode
