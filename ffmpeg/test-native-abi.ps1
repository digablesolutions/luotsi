$ErrorActionPreference = "Stop"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot "download-ffmpeg.ps1"), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
$function = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq "Test-RequiredNativeLibraries"
}, $true)
if ($null -eq $function) { throw "Required ABI check function is missing" }
# Execute only this pure file-presence check, never the downloader's top level.
$check = $function.Body.GetScriptBlock()
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("luotsi-abi-test-" + [guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $cases = @(
        @{ Platform = "win-x64"; Old = @("avcodec-62.dll", "avutil-60.dll", "swscale-9.dll"); New = @("avcodec-63.dll", "avutil-61.dll", "swscale-10.dll") },
        @{ Platform = "linux-x64"; Old = @("libavcodec.so.62", "libavutil.so.60", "libswscale.so.9"); New = @("libavcodec.so.63", "libavutil.so.61", "libswscale.so.10") },
        @{ Platform = "osx-arm64"; Old = @("libavcodec.62.dylib", "libavutil.60.dylib", "libswscale.9.dylib"); New = @("libavcodec.63.dylib", "libavutil.61.dylib", "libswscale.10.dylib") }
    )
    foreach ($case in $cases) {
        $directory = Join-Path $testRoot $case.Platform
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
        if (& $check -Directory $directory -ResolvedPlatform $case.Platform) { throw "Empty directory accepted" }
        foreach ($name in $case.Old) { [System.IO.File]::WriteAllText((Join-Path $directory $name), "fixture") }
        if (& $check -Directory $directory -ResolvedPlatform $case.Platform) { throw "Old ABI accepted" }
        foreach ($name in $case.New) { [System.IO.File]::WriteAllText((Join-Path $directory $name), "fixture") }
        if (-not (& $check -Directory $directory -ResolvedPlatform $case.Platform)) { throw "Matching ABI names rejected" }
        [System.IO.File]::Delete((Join-Path $directory $case.New[2]))
        if (& $check -Directory $directory -ResolvedPlatform $case.Platform) { throw "Incomplete ABI accepted" }
    }
    Write-Host "Passed 12 native-library name checks; no binaries loaded or downloaded."
} finally {
    [System.IO.Directory]::Delete($testRoot, $true)
}
