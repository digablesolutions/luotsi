<#
.SYNOPSIS
    Stages host-native FFmpeg shared libraries for the native libav backend.

.DESCRIPTION
    On Windows, downloads a pinned shared FFmpeg build and extracts the native
    libraries into ffmpeg/bin. On macOS, ensures Homebrew ffmpeg is installed
    and copies the dylibs from the active formula into ffmpeg/bin. On Linux,
    downloads a shared build archive and extracts the shared objects into
    ffmpeg/bin.

.PARAMETER Version
    FFmpeg release line to provision. Defaults to 8.1.

.PARAMETER Platform
    Optional host override. Defaults to the current OS/architecture.
    Supported values: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64.

.PARAMETER Force
    Overwrite existing staged native libraries.

.EXAMPLE
    .\ffmpeg\download-ffmpeg.ps1
    .\ffmpeg\download-ffmpeg.ps1 -Platform osx-arm64 -Force
    .\ffmpeg\download-ffmpeg.ps1 -Version 8.1 -Force
#>

param(
    [string]$Version = "8.1",
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Platform,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Get-DefaultPlatform {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    if ($IsWindows) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "win-arm64"
        }

        return "win-x64"
    }

    if ($IsMacOS) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "osx-arm64"
        }

        return "osx-x64"
    }

    if ($IsLinux) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "linux-arm64"
        }

        return "linux-x64"
    }

    throw "Unsupported host OS '$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)'."
}

function Get-ExistingLibraryCount {
    param(
        [string]$Directory,
        [string[]]$Patterns
    )

    $count = 0
    foreach ($pattern in $Patterns) {
        $count += @(Get-ChildItem -Path $Directory -Filter $pattern -ErrorAction SilentlyContinue).Count
    }

    return $count
}

function Reset-BinDirectory {
    param([string]$Directory)

    if (Test-Path $Directory) {
        Remove-Item -Recurse -Force $Directory
    }

    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
}

function Copy-NativeLibraries {
    param(
        [string]$SourceDirectory,
        [string]$DestinationDirectory,
        [string[]]$Patterns
    )

    $copied = 0
    foreach ($pattern in $Patterns) {
        $files = Get-ChildItem -Path $SourceDirectory -Filter $pattern -File -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            Copy-Item -LiteralPath $file.FullName -Destination $DestinationDirectory -Force
            $copied += 1
        }
    }

    return $copied
}

function Expand-ArchiveByExtension {
    param(
        [string]$ArchivePath,
        [string]$DestinationPath
    )

    if ($ArchivePath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -Path $ArchivePath -DestinationPath $DestinationPath -Force
        return
    }

    if ($ArchivePath.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase)) {
        $tarCommand = Get-Command tar -ErrorAction SilentlyContinue
        if (-not $tarCommand) {
            throw "The 'tar' command is required to extract $ArchivePath."
        }

        & $tarCommand.Source -xf $ArchivePath -C $DestinationPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to extract $ArchivePath with tar."
        }

        return
    }

    throw "Unsupported archive format: $ArchivePath"
}

function Resolve-GitHubReleaseAsset {
    param(
        [string]$Owner,
        [string]$Repository,
        [string]$AssetPattern
    )

    $releaseUri = "https://api.github.com/repos/$Owner/$Repository/releases/latest"
    $headers = @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "luotsi-ffmpeg-stager"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers
    $asset = @($release.assets | Where-Object { $_.name -match $AssetPattern } | Select-Object -First 1)
    if (-not $asset) {
        throw "Could not find a GitHub release asset matching '$AssetPattern' in $Owner/$Repository latest release '$($release.tag_name)'."
    }

    return [pscustomobject]@{
        ArchiveName = $asset.name
        Url = $asset.browser_download_url
    }
}

function Invoke-DownloadWithRetry {
    param(
        [string]$Uri,
        [string]$DestinationPath,
        [int]$MaxAttempts = 5
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Uri -OutFile $DestinationPath -UseBasicParsing
            if ((Test-Path -LiteralPath $DestinationPath) -and ((Get-Item -LiteralPath $DestinationPath).Length -gt 0)) {
                return
            }

            throw "Downloaded file is empty: $DestinationPath"
        }
        catch {
            Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction SilentlyContinue
            if ($attempt -eq $MaxAttempts) {
                throw
            }

            Write-Warning "Download attempt $attempt failed for $Uri. Retrying... $($_.Exception.Message)"
            Start-Sleep -Seconds ([Math]::Min(10, 2 * $attempt))
        }
    }
}

function Get-DownloadPlan {
    param(
        [string]$ResolvedPlatform,
        [string]$ResolvedVersion
    )

    switch ($ResolvedPlatform) {
        "win-x64" {
            return [pscustomobject]@{
                Strategy = "archive"
                ArchiveName = "ffmpeg-$ResolvedVersion-full_build-shared.zip"
                Url = "https://github.com/GyanD/codexffmpeg/releases/download/$ResolvedVersion/ffmpeg-$ResolvedVersion-full_build-shared.zip"
                ExtractSubdirectory = "bin"
                FilePatterns = @("*.dll")
            }
        }

        "win-arm64" {
            return [pscustomobject]@{
                Strategy = "archive"
                GitHubOwner = "BtbN"
                GitHubRepository = "FFmpeg-Builds"
                AssetPattern = "^ffmpeg-n.+-winarm64-lgpl-shared-$([regex]::Escape($ResolvedVersion))\.zip$"
                ExtractSubdirectory = "bin"
                FilePatterns = @("*.dll")
            }
        }

        "linux-x64" {
            return [pscustomobject]@{
                Strategy = "archive"
                GitHubOwner = "BtbN"
                GitHubRepository = "FFmpeg-Builds"
                AssetPattern = "^ffmpeg-n.+-linux64-lgpl-shared-$([regex]::Escape($ResolvedVersion))\.tar\.xz$"
                ExtractSubdirectory = "lib"
                FilePatterns = @("*.so", "*.so.*")
            }
        }

        "linux-arm64" {
            return [pscustomobject]@{
                Strategy = "archive"
                GitHubOwner = "BtbN"
                GitHubRepository = "FFmpeg-Builds"
                AssetPattern = "^ffmpeg-n.+-linuxarm64-lgpl-shared-$([regex]::Escape($ResolvedVersion))\.tar\.xz$"
                ExtractSubdirectory = "lib"
                FilePatterns = @("*.so", "*.so.*")
            }
        }

        "osx-x64" {
            return [pscustomobject]@{
                Strategy = "homebrew"
                Formula = "ffmpeg"
                FilePatterns = @("*.dylib")
            }
        }

        "osx-arm64" {
            return [pscustomobject]@{
                Strategy = "homebrew"
                Formula = "ffmpeg"
                FilePatterns = @("*.dylib")
            }
        }
    }

    throw "Unsupported platform '$ResolvedPlatform'."
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinDir = Join-Path $ScriptDir "bin"
$TempDir = Join-Path $ScriptDir "temp"
$ResolvedPlatform = if ([string]::IsNullOrWhiteSpace($Platform)) { Get-DefaultPlatform } else { $Platform }
$Plan = Get-DownloadPlan -ResolvedPlatform $ResolvedPlatform -ResolvedVersion $Version
if ($Plan.PSObject.Properties.Name -contains "GitHubOwner") {
    $asset = Resolve-GitHubReleaseAsset -Owner $Plan.GitHubOwner -Repository $Plan.GitHubRepository -AssetPattern $Plan.AssetPattern
    $Plan | Add-Member -NotePropertyName ArchiveName -NotePropertyValue $asset.ArchiveName
    $Plan | Add-Member -NotePropertyName Url -NotePropertyValue $asset.Url
}

if ((Get-ExistingLibraryCount -Directory $BinDir -Patterns $Plan.FilePatterns) -gt 0 -and -not $Force) {
    Write-Host "FFmpeg native libraries already exist in $BinDir"
    Write-Host "Re-run with -Force to overwrite them."
    exit 0
}

try {
    if ($Plan.Strategy -eq "homebrew") {
        $brewCommand = Get-Command brew -ErrorAction SilentlyContinue
        if (-not $brewCommand) {
            throw "Homebrew is required on macOS to provision FFmpeg dylibs. Install brew, then rerun this script."
        }

        Write-Host "Ensuring Homebrew formula '$($Plan.Formula)' is installed for $ResolvedPlatform"
        & $brewCommand.Source install $Plan.Formula
        if ($LASTEXITCODE -ne 0) {
            throw "brew install $($Plan.Formula) failed."
        }

        $brewPrefix = (& $brewCommand.Source --prefix $Plan.Formula).Trim()
        if ([string]::IsNullOrWhiteSpace($brewPrefix)) {
            throw "Unable to resolve Homebrew prefix for formula '$($Plan.Formula)'."
        }

        $librarySource = Join-Path $brewPrefix "lib"
        if (-not (Test-Path $librarySource)) {
            throw "Expected Homebrew FFmpeg library directory was not found: $librarySource"
        }

        Reset-BinDirectory -Directory $BinDir
        $copiedCount = Copy-NativeLibraries -SourceDirectory $librarySource -DestinationDirectory $BinDir -Patterns $Plan.FilePatterns
        if ($copiedCount -eq 0) {
            throw "No FFmpeg dylibs were found under $librarySource."
        }

        Write-Host "Done. Staged $copiedCount native libraries to $BinDir from Homebrew prefix $brewPrefix"
        exit 0
    }

    Write-Host "Downloading FFmpeg $Version for $ResolvedPlatform from $($Plan.Url)"

    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
    $archivePath = Join-Path $TempDir $Plan.ArchiveName
    Invoke-DownloadWithRetry -Uri $Plan.Url -DestinationPath $archivePath

    Write-Host "Extracting archive..."
    Expand-ArchiveByExtension -ArchivePath $archivePath -DestinationPath $TempDir

    $extractedDir = Get-ChildItem -Path $TempDir -Directory | Where-Object { $_.Name -like "ffmpeg-*" } | Select-Object -First 1
    if (-not $extractedDir) {
        throw "Could not find the extracted FFmpeg directory in $TempDir"
    }

    $librarySource = Join-Path $extractedDir.FullName $Plan.ExtractSubdirectory
    if (-not (Test-Path $librarySource)) {
        throw "Expected FFmpeg library directory was not found: $librarySource"
    }

    Reset-BinDirectory -Directory $BinDir
    $copiedCount = Copy-NativeLibraries -SourceDirectory $librarySource -DestinationDirectory $BinDir -Patterns $Plan.FilePatterns
    if ($copiedCount -eq 0) {
        throw "No FFmpeg native libraries matching $($Plan.FilePatterns -join ', ') were found under $librarySource."
    }

    Write-Host "Done. Extracted $copiedCount native libraries to $BinDir"
}
catch {
    Write-Error "Failed to provision FFmpeg native libraries for $ResolvedPlatform.`n$_"
    exit 1
}
finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}
