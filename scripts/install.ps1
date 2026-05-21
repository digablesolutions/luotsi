<#
.SYNOPSIS
    Installs Luotsi from GitHub Releases into the current user's profile.

.DESCRIPTION
    Downloads the latest published Luotsi release (or an explicit version tag),
    verifies the published SHA-256 checksum, installs the archive under the
    current user's LocalAppData directory, writes a small command shim, and
    optionally updates the user PATH.

.EXAMPLE
    iex (irm https://raw.githubusercontent.com/digablesolutions/luotsi/main/scripts/install.ps1)

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/digablesolutions/luotsi/main/scripts/install.ps1))) -Version v1.2.3 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$InstallRoot,
    [string]$Owner = "digablesolutions",
    [string]$Repository = "luotsi",
    [switch]$SkipPathUpdate,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Set-StrictMode -Version Latest

function Get-DefaultInstallRoot {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not available in this session. Pass -InstallRoot explicitly."
    }

    return Join-Path $env:LOCALAPPDATA "Luotsi"
}

function Get-PlatformRid {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        return "win-x64"
    }

    return "win-x64"
}

function Normalize-ReleaseTag([string]$RequestedVersion) {
    if ([string]::IsNullOrWhiteSpace($RequestedVersion) -or $RequestedVersion -eq "latest") {
        return $null
    }

    return if ($RequestedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $RequestedVersion
    }
    else {
        "v$RequestedVersion"
    }
}

function Invoke-GitHubJson([string]$Uri) {
    $response = Invoke-WebRequest -Headers @{
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "luotsi-installer"
        "X-GitHub-Api-Version" = "2022-11-28"
    } -Uri $Uri

    if ([string]::IsNullOrWhiteSpace($response.Content)) {
        return $null
    }

    return $response.Content | ConvertFrom-Json
}

function Resolve-ReleaseTag([string]$OwnerName, [string]$RepositoryName, [string]$RequestedTag) {
    if (-not [string]::IsNullOrWhiteSpace($RequestedTag)) {
        $release = Invoke-GitHubJson "https://api.github.com/repos/$OwnerName/$RepositoryName/releases/tags/$RequestedTag"
        if ([string]::IsNullOrWhiteSpace($release.tag_name)) {
            throw "GitHub did not return a tag name for release '$RequestedTag'."
        }

        return [string]$release.tag_name
    }

    $releases = @(Invoke-GitHubJson "https://api.github.com/repos/$OwnerName/$RepositoryName/releases?per_page=1")
    if ($releases.Count -lt 1 -or [string]::IsNullOrWhiteSpace($releases[0].tag_name)) {
        throw "No published GitHub Releases were found for $OwnerName/$RepositoryName. Create a release or pass -Version <tag>."
    }

    return [string]$releases[0].tag_name
}

function Invoke-Download([string]$Uri, [string]$DestinationPath) {
    Invoke-WebRequest -Headers @{ "User-Agent" = "luotsi-installer" } -Uri $Uri -OutFile $DestinationPath
}

function Get-Checksum([string]$ChecksumFile, [string]$AssetName) {
    $pattern = '^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>.+)$'
    foreach ($line in Get-Content -LiteralPath $ChecksumFile) {
        if ($line -match $pattern -and $Matches.name -eq $AssetName) {
            return $Matches.hash.ToLowerInvariant()
        }
    }

    throw "Could not find a SHA-256 entry for $AssetName in $ChecksumFile."
}

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Update-UserPath([string]$TargetDirectory) {
    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $entries = @()
    if (-not [string]::IsNullOrWhiteSpace($currentPath)) {
        $entries = $currentPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries)
    }

    $normalizedTarget = [IO.Path]::GetFullPath($TargetDirectory).TrimEnd('\\')
    foreach ($entry in $entries) {
        $candidate = $entry.Trim()
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        try {
            if ([IO.Path]::GetFullPath($candidate).TrimEnd('\\') -ieq $normalizedTarget) {
                return $false
            }
        }
        catch {
            if ($candidate.TrimEnd('\\') -ieq $normalizedTarget) {
                return $false
            }
        }
    }

    $updatedPath = if ([string]::IsNullOrWhiteSpace($currentPath)) {
        $TargetDirectory
    }
    else {
        "$currentPath;$TargetDirectory"
    }

    [Environment]::SetEnvironmentVariable("Path", $updatedPath, "User")
    return $true
}

function Write-CommandShim([string]$ShimPath) {
    $content = "@echo off`r`n`"%~dp0..\current\luotsi.exe`" %*`r`n"
    [IO.File]::WriteAllText($ShimPath, $content, [Text.Encoding]::ASCII)
}

function Write-Manifest(
    [string]$ManifestPath,
    [string]$InstallDirectory,
    [string]$BinDirectory,
    [string]$CommandPath,
    [string]$Tag,
    [string]$Rid,
    [string]$ArchiveName,
    [string]$ArchiveUrl,
    [string]$ChecksumUrl) {

    $manifest = [ordered]@{
        schema = "luotsi-install.v1"
        tool = "luotsi"
        tag = $Tag
        version = $Tag.TrimStart('v')
        rid = $Rid
        install_root = $InstallDirectory
        current_root = (Join-Path $InstallDirectory "current")
        bin_directory = $BinDirectory
        command_path = $CommandPath
        archive_name = $ArchiveName
        archive_url = $ArchiveUrl
        checksum_url = $ChecksumUrl
        installed_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
}

$resolvedInstallRoot = if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Get-DefaultInstallRoot
}
else {
    [Environment]::ExpandEnvironmentVariables($InstallRoot)
}

$resolvedInstallRoot = [IO.Path]::GetFullPath($resolvedInstallRoot)
$resolvedTag = Resolve-ReleaseTag $Owner $Repository (Normalize-ReleaseTag $Version)
$resolvedVersion = $resolvedTag.TrimStart('v')
$rid = Get-PlatformRid

$archiveName = "luotsi-cli-$resolvedVersion-$rid.zip"
$archiveUrl = "https://github.com/$Owner/$Repository/releases/download/$resolvedTag/$archiveName"
$checksumUrl = "https://github.com/$Owner/$Repository/releases/download/$resolvedTag/SHA256SUMS"

$binDirectory = Join-Path $resolvedInstallRoot "bin"
$currentDirectory = Join-Path $resolvedInstallRoot "current"
$previousDirectory = Join-Path $resolvedInstallRoot "previous"
$commandPath = Join-Path $binDirectory "luotsi.cmd"
$manifestPath = Join-Path $resolvedInstallRoot "install.json"

Write-Host "Luotsi installer"
Write-Host "  Release:      $resolvedTag"
Write-Host "  Runtime:      $rid"
Write-Host "  Install root: $resolvedInstallRoot"
Write-Host "  Command dir:  $binDirectory"
Write-Host "  Asset:        $archiveName"

if ($DryRun) {
    Write-Host "Dry run only. No files were downloaded or changed."
    return
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("luotsi-install-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot $archiveName
$checksumPath = Join-Path $tempRoot "SHA256SUMS"
$extractDirectory = Join-Path $tempRoot "payload"

try {
    Ensure-Directory $tempRoot
    Ensure-Directory $resolvedInstallRoot
    Ensure-Directory $binDirectory

    Write-Host "Downloading release archive..."
    Invoke-Download $archiveUrl $archivePath
    Invoke-Download $checksumUrl $checksumPath

    $expectedHash = Get-Checksum $checksumPath $archiveName
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -ne $actualHash) {
        throw "SHA-256 mismatch for $archiveName. Expected $expectedHash but got $actualHash."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory -Force

    $payloadRoot = $extractDirectory
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot "luotsi.exe"))) {
        $children = @(Get-ChildItem -LiteralPath $extractDirectory -Force)
        if ($children.Count -eq 1 -and $children[0].PSIsContainer -and (Test-Path -LiteralPath (Join-Path $children[0].FullName "luotsi.exe"))) {
            $payloadRoot = $children[0].FullName
        }
        else {
            throw "The release archive did not contain luotsi.exe at its root."
        }
    }

    if (Test-Path -LiteralPath $previousDirectory) {
        Remove-Item -LiteralPath $previousDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $currentDirectory) {
        Move-Item -LiteralPath $currentDirectory -Destination $previousDirectory
    }

    Move-Item -LiteralPath $payloadRoot -Destination $currentDirectory
    if (Test-Path -LiteralPath $previousDirectory) {
        Remove-Item -LiteralPath $previousDirectory -Recurse -Force
    }

    Write-CommandShim $commandPath
    Write-Manifest $manifestPath $resolvedInstallRoot $binDirectory $commandPath $resolvedTag $rid $archiveName $archiveUrl $checksumUrl

    $pathUpdated = $false
    if (-not $SkipPathUpdate) {
        $pathUpdated = Update-UserPath $binDirectory
    }

    Write-Host "Install complete."
    Write-Host "  Command: $commandPath"
    if ($pathUpdated) {
        Write-Host "  User PATH was updated. Open a new terminal before running 'luotsi'."
    }
    elseif ($SkipPathUpdate) {
        Write-Host "  PATH was not modified. Add '$binDirectory' to your user PATH to run 'luotsi'."
    }
    else {
        Write-Host "  PATH already contains '$binDirectory'."
    }
}
catch {
    if (-not (Test-Path -LiteralPath $currentDirectory) -and (Test-Path -LiteralPath $previousDirectory)) {
        Move-Item -LiteralPath $previousDirectory -Destination $currentDirectory
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}