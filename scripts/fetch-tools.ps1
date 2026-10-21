<#
.SYNOPSIS
  Downloads ffmpeg.exe and pandoc.exe into a folder (default: .\tools) so they can ship next to FileAction.exe.
.EXAMPLE
  ./scripts/fetch-tools.ps1
  ./scripts/fetch-tools.ps1 -Destination publish\tools
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\tools')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path $Destination).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ("fileaction-tools-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

$headers = @{ 'User-Agent' = 'FileAction-build' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

# Tries each URL a few times: BtbN's "latest" release is rebuilt daily and its files are missing for a while.
function Get-Zip([string[]]$Urls, [string]$Name) {
    $zip = Join-Path $temp "$Name.zip"
    foreach ($attempt in 1..3) {
        foreach ($url in $Urls) {
            try {
                Write-Host "Downloading $url"
                $h = if ($url -like 'https://github.com/*' -or $url -like 'https://api.github.com/*') { $headers } else { @{ 'User-Agent' = 'FileAction-build' } }
                Invoke-WebRequest -Uri $url -OutFile $zip -Headers $h
                $dir = Join-Path $temp $Name
                Expand-Archive -Path $zip -DestinationPath $dir -Force
                return $dir
            }
            catch {
                Write-Host "  failed: $($_.Exception.Message)"
            }
        }
        Start-Sleep -Seconds (15 * $attempt)
    }
    throw "Could not download $Name"
}

try {
    # FFmpeg: GPL build (x264, libvpx, opus, lame, vorbis) from https://github.com/BtbN/FFmpeg-Builds,
    # or gyan.dev's essentials build (the one the app downloads) while BtbN's is being rebuilt.
    $ffmpegDir = Get-Zip @(
        'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip',
        'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip') 'ffmpeg'
    $ffmpeg = Get-ChildItem $ffmpegDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $ffmpeg) { throw 'ffmpeg.exe not found in the archive' }
    Copy-Item $ffmpeg.FullName $Destination -Force

    # Pandoc: latest release from https://github.com/jgm/pandoc
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/jgm/pandoc/releases/latest' -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -like 'pandoc-*-windows-x86_64.zip' } | Select-Object -First 1
    if (-not $asset) { throw 'No Windows asset in the latest pandoc release' }
    $pandocDir = Get-Zip @($asset.browser_download_url) 'pandoc'
    $pandoc = Get-ChildItem $pandocDir -Recurse -Filter 'pandoc.exe' | Select-Object -First 1
    Copy-Item $pandoc.FullName $Destination -Force

    Write-Host "Tools in $Destination :"
    Get-ChildItem $Destination | ForEach-Object { Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
