param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $LuotsiArgs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Luotsi.Cli'

dotnet run --project $project -- @LuotsiArgs
exit $LASTEXITCODE
