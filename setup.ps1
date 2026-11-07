param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$toolsDirectory = Join-Path $PSScriptRoot 'tools'
$stageDirectory = $null
$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'BareBonesDownloader-Setup'
}

function Get-Release {
    param([string]$ApiUrl)
    return Invoke-RestMethod -Uri $ApiUrl -Headers $headers -TimeoutSec 60
}

function Get-ReleaseAsset {
    param(
        [object]$Release,
        [string]$Name
    )
    $asset = @($Release.assets) |
        Where-Object { $_.name -eq $Name } |
        Select-Object -First 1
    if ($null -eq $asset) {
        throw "The expected release file '$Name' was not found in $($Release.html_url)."
    }
    return $asset
}

function Get-AssetSha256 {
    param(
        [object]$Release,
        [object]$Asset,
        [string]$Directory
    )

    if ($Asset.digest -and $Asset.digest -match '^sha256:([0-9a-fA-F]{64})$') {
        return $Matches[1].ToLowerInvariant()
    }

    $checksumAssets = @($Release.assets) |
        Where-Object { $_.name -match '^(SHA2-256SUMS|SHA256SUMS|SHA256SUMS\.txt)$' }
    foreach ($checksumAsset in $checksumAssets) {
        $checksumFile = Join-Path $Directory $checksumAsset.name
        try {
            Invoke-WebRequest -Uri $checksumAsset.browser_download_url -Headers $headers -OutFile $checksumFile -UseBasicParsing -TimeoutSec 120
            foreach ($line in Get-Content -LiteralPath $checksumFile) {
                $match = [regex]::Match($line, '^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<file>.+?)\s*$')
                if ($match.Success) {
                    $fileName = $match.Groups['file'].Value.Trim()
                    if ($fileName -eq $Asset.name -or $fileName.EndsWith('/' + $Asset.name)) {
                        return $match.Groups['hash'].Value.ToLowerInvariant()
                    }
                }
            }
        }
        catch {
            Write-Host "Could not read $($checksumAsset.name): $($_.Exception.Message)"
        }
    }
    return $null
}

function Get-HttpsDownloadUrl {
    param([object]$Asset)
    $uri = [Uri]$Asset.browser_download_url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
        throw "The release asset URL is not an expected HTTPS GitHub address: $($Asset.browser_download_url)"
    }
    return $uri.AbsoluteUri
}

function Download-ReleaseAsset {
    param(
        [object]$Release,
        [object]$Asset,
        [string]$Destination
    )

    $url = Get-HttpsDownloadUrl $Asset
    $expectedHash = Get-AssetSha256 $Release $Asset (Split-Path -Parent $Destination)
    $partial = $Destination + '.partial'
    $downloaded = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            if (Test-Path -LiteralPath $partial) {
                Remove-Item -LiteralPath $partial -Force
            }
            Write-Host "Downloading $($Asset.name) (attempt $attempt of 3)..."
            Invoke-WebRequest -Uri $url -Headers $headers -OutFile $partial -UseBasicParsing -TimeoutSec 900
            Move-Item -LiteralPath $partial -Destination $Destination -Force
            $downloaded = $true
            break
        }
        catch {
            if ($attempt -eq 3) {
                throw "Download failed for $($Asset.name): $($_.Exception.Message)"
            }
            Start-Sleep -Seconds 2
        }
    }

    if (-not $downloaded -or -not (Test-Path -LiteralPath $Destination)) {
        throw "Download did not produce $Destination."
    }

    if ($expectedHash) {
        $actualHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "SHA-256 verification failed for $($Asset.name)."
        }
        Write-Host "SHA-256 verified for $($Asset.name)."
    }
    else {
        Write-Host "No published SHA-256 checksum was available for $($Asset.name); HTTPS and version checks will be used."
    }
}

function Download-TextFile {
    param(
        [string[]]$Urls,
        [string]$Destination
    )
    for ($index = 0; $index -lt $Urls.Count; $index++) {
        $url = $Urls[$index]
        $uri = [Uri]$url
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'raw.githubusercontent.com') {
            throw "Unexpected upstream notice URL: $url"
        }

        try {
            Write-Host "Downloading upstream notice from $url..."
            Invoke-WebRequest -Uri $uri.AbsoluteUri -Headers $headers -OutFile $Destination -UseBasicParsing -TimeoutSec 120
            return
        }
        catch {
            $statusCode = $null
            if ($_.Exception.Response) {
                try {
                    $statusCode = [int]$_.Exception.Response.StatusCode
                }
                catch { }
            }
            if ($statusCode -eq 404 -and $index -lt ($Urls.Count - 1)) {
                Write-Host 'That version-specific notice was not found; trying the upstream fallback.'
                continue
            }
            throw "Notice download failed at $($uri.AbsoluteUri): $($_.Exception.Message)"
        }
    }

    throw "No upstream notice URL was supplied for $Destination."
}

function Invoke-VersionCheck {
    param(
        [string]$Path,
        [string]$Arguments,
        [switch]$FullOutput
    )

    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Path
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) {
        throw "Could not start $Path."
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    if ($process.ExitCode -ne 0) {
        throw "$Path $Arguments failed with exit code $($process.ExitCode): $stderr"
    }
    if ([string]::IsNullOrWhiteSpace($stdout)) {
        $stdout = $stderr
    }
    if ($FullOutput) {
        return $stdout.Trim()
    }
    return ($stdout.Trim() -split '\r?\n')[0]
}

function Restore-PreviousTools {
    param(
        [string]$Stage,
        [string[]]$Installed,
        [string[]]$BackedUp
    )

    foreach ($name in $Installed) {
        $target = Join-Path $toolsDirectory $name
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    }
    foreach ($name in $BackedUp) {
        $backup = Join-Path (Join-Path $Stage 'backup') $name
        if (Test-Path -LiteralPath $backup) {
            Move-Item -LiteralPath $backup -Destination (Join-Path $toolsDirectory $name) -Force
        }
    }
}

try {
    if (Get-Process -Name 'BareBonesDownloader' -ErrorAction SilentlyContinue) {
        throw 'Close BareBonesDownloader before running setup.'
    }

    New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
    $stageDirectory = Join-Path $toolsDirectory ('.setup-staging-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    $downloadDirectory = Join-Path $stageDirectory 'downloads'
    $extractDirectory = Join-Path $stageDirectory 'extract'
    $payloadDirectory = Join-Path $stageDirectory 'payload'
    $licensesDirectory = Join-Path $payloadDirectory 'licenses'
    New-Item -ItemType Directory -Path $downloadDirectory, $extractDirectory, $licensesDirectory -Force | Out-Null

    $ytRelease = Get-Release 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest'
    $ytAsset = Get-ReleaseAsset $ytRelease 'yt-dlp.exe'
    $ytFile = Join-Path $downloadDirectory 'yt-dlp.exe'
    Download-ReleaseAsset $ytRelease $ytAsset $ytFile
    Copy-Item -LiteralPath $ytFile -Destination (Join-Path $payloadDirectory 'yt-dlp.exe')

    $ffmpegRelease = Get-Release 'https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/tags/latest'
    $ffmpegAsset = Get-ReleaseAsset $ffmpegRelease 'ffmpeg-master-latest-win64-gpl.zip'
    $ffmpegZip = Join-Path $downloadDirectory 'ffmpeg-win64-gpl.zip'
    Download-ReleaseAsset $ffmpegRelease $ffmpegAsset $ffmpegZip
    $ffmpegExtract = Join-Path $extractDirectory 'ffmpeg'
    Expand-Archive -LiteralPath $ffmpegZip -DestinationPath $ffmpegExtract -Force
    $ffmpegExe = Get-ChildItem -LiteralPath $ffmpegExtract -Filter 'ffmpeg.exe' -File -Recurse |
        Select-Object -First 1
    $ffprobeExe = Get-ChildItem -LiteralPath $ffmpegExtract -Filter 'ffprobe.exe' -File -Recurse |
        Select-Object -First 1
    if ($null -eq $ffmpegExe -or $null -eq $ffprobeExe) {
        throw 'The FFmpeg archive did not contain both ffmpeg.exe and ffprobe.exe.'
    }
    Copy-Item -LiteralPath $ffmpegExe.FullName -Destination (Join-Path $payloadDirectory 'ffmpeg.exe')
    Copy-Item -LiteralPath $ffprobeExe.FullName -Destination (Join-Path $payloadDirectory 'ffprobe.exe')

    $denoRelease = Get-Release 'https://api.github.com/repos/denoland/deno/releases/latest'
    $denoAsset = Get-ReleaseAsset $denoRelease 'deno-x86_64-pc-windows-msvc.zip'
    $denoZip = Join-Path $downloadDirectory 'deno-win-x64.zip'
    Download-ReleaseAsset $denoRelease $denoAsset $denoZip
    $denoExtract = Join-Path $extractDirectory 'deno'
    Expand-Archive -LiteralPath $denoZip -DestinationPath $denoExtract -Force
    $denoExe = Get-ChildItem -LiteralPath $denoExtract -Filter 'deno.exe' -File -Recurse |
        Select-Object -First 1
    if ($null -eq $denoExe) {
        throw 'The Deno archive did not contain deno.exe.'
    }
    Copy-Item -LiteralPath $denoExe.FullName -Destination (Join-Path $payloadDirectory 'deno.exe')

    $rsgainRelease = Get-Release 'https://api.github.com/repos/complexlogic/rsgain/releases/latest'
    $rsgainVersionTag = ([string]$rsgainRelease.tag_name).TrimStart('v')
    $rsgainAssetName = 'rsgain-' + $rsgainVersionTag + '-win64.zip'
    $rsgainAsset = Get-ReleaseAsset $rsgainRelease $rsgainAssetName
    $rsgainZip = Join-Path $downloadDirectory $rsgainAssetName
    Download-ReleaseAsset $rsgainRelease $rsgainAsset $rsgainZip
    $rsgainExtract = Join-Path $extractDirectory 'rsgain'
    Expand-Archive -LiteralPath $rsgainZip -DestinationPath $rsgainExtract -Force
    $rsgainExe = Get-ChildItem -LiteralPath $rsgainExtract -Filter 'rsgain.exe' -File -Recurse |
        Select-Object -First 1
    if ($null -eq $rsgainExe) {
        throw 'The rsgain archive did not contain rsgain.exe.'
    }
    $rsgainPayload = Join-Path $payloadDirectory 'rsgain'
    New-Item -ItemType Directory -Path $rsgainPayload -Force | Out-Null
    Get-ChildItem -LiteralPath $rsgainExe.Directory.FullName -Force |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $rsgainPayload -Recurse -Force
        }

    $ytTag = [Uri]::EscapeDataString([string]$ytRelease.tag_name)
    Download-TextFile -Urls @(
        "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$ytTag/LICENSE",
        'https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/LICENSE'
    ) -Destination (Join-Path $licensesDirectory 'yt-dlp-LICENSE.txt')
    Download-TextFile -Urls @(
        "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$ytTag/THIRD_PARTY_LICENSES.txt",
        'https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/THIRD_PARTY_LICENSES.txt'
    ) -Destination (Join-Path $licensesDirectory 'yt-dlp-THIRD_PARTY_LICENSES.txt')
    Download-TextFile -Urls @(
        'https://raw.githubusercontent.com/yt-dlp/FFmpeg-Builds/master/LICENSE'
    ) -Destination (Join-Path $licensesDirectory 'yt-dlp-FFmpeg-Builds-LICENSE.txt')
    $denoTag = [Uri]::EscapeDataString([string]$denoRelease.tag_name)
    Download-TextFile -Urls @(
        "https://raw.githubusercontent.com/denoland/deno/$denoTag/LICENSE.md",
        'https://raw.githubusercontent.com/denoland/deno/main/LICENSE.md'
    ) -Destination (Join-Path $licensesDirectory 'deno-LICENSE.txt')
    $rsgainTag = [Uri]::EscapeDataString([string]$rsgainRelease.tag_name)
    Download-TextFile -Urls @(
        "https://raw.githubusercontent.com/complexlogic/rsgain/$rsgainTag/COPYING",
        'https://raw.githubusercontent.com/complexlogic/rsgain/master/COPYING',
        'https://raw.githubusercontent.com/complexlogic/rsgain/master/LICENSE'
    ) -Destination (Join-Path $licensesDirectory 'rsgain-COPYING.txt')

    $ytVersion = Invoke-VersionCheck (Join-Path $payloadDirectory 'yt-dlp.exe') '--version'
    $ffmpegVersion = Invoke-VersionCheck (Join-Path $payloadDirectory 'ffmpeg.exe') '-version'
    $ffprobeVersion = Invoke-VersionCheck (Join-Path $payloadDirectory 'ffprobe.exe') '-version'
    $denoVersionText = Invoke-VersionCheck (Join-Path $payloadDirectory 'deno.exe') '--version'
    $rsgainVersion = Invoke-VersionCheck (Join-Path $rsgainPayload 'rsgain.exe') '--version'
    $denoMatch = [regex]::Match($denoVersionText, '^deno\s+(?<version>\d+\.\d+\.\d+)')
    if (-not $denoMatch.Success -or ([version]$denoMatch.Groups['version'].Value -lt [version]'2.3.0')) {
        throw "Deno 2.3.0 or later is required for current yt-dlp YouTube support. Found: $denoVersionText"
    }

    $licenseOutput = Invoke-VersionCheck (Join-Path $payloadDirectory 'ffmpeg.exe') '-L' -FullOutput
    Set-Content -LiteralPath (Join-Path $licensesDirectory 'ffmpeg-runtime-LICENSE.txt') -Value $licenseOutput -Encoding UTF8
    $notice = @"
Downloaded tools and upstream sources

yt-dlp: $($ytRelease.html_url)
yt-dlp release: $($ytRelease.tag_name)
FFmpeg build: $($ffmpegRelease.html_url)
FFmpeg asset: $($ffmpegAsset.name)
Deno: $($denoRelease.html_url)
Deno release: $($denoRelease.tag_name)
rsgain: $($rsgainRelease.html_url)
rsgain release: $($rsgainRelease.tag_name)

The original license and third-party notice files from yt-dlp, Deno, rsgain, and the
yt-dlp FFmpeg-Builds project are stored in this folder. ffmpeg-runtime-LICENSE.txt
contains the license notice reported by the downloaded FFmpeg binary.

Versions checked successfully:
$ytVersion
$ffmpegVersion
$ffprobeVersion
$denoVersionText
$rsgainVersion
"@
    Set-Content -LiteralPath (Join-Path $licensesDirectory 'UPSTREAM-SOURCES-AND-NOTICES.txt') -Value $notice -Encoding UTF8

    $requiredPayload = @(
        'yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe',
        (Join-Path 'rsgain' 'rsgain.exe')
    )
    foreach ($name in $requiredPayload) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $name))) {
            throw "The staged tool $name is missing."
        }
    }

    $backupDirectory = Join-Path $stageDirectory 'backup'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $managedNames = @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe', 'rsgain', 'licenses')
    $backedUp = New-Object System.Collections.Generic.List[string]
    $installed = New-Object System.Collections.Generic.List[string]

    try {
        foreach ($name in $managedNames) {
            $target = Join-Path $toolsDirectory $name
            if (Test-Path -LiteralPath $target) {
                Move-Item -LiteralPath $target -Destination (Join-Path $backupDirectory $name) -Force
                $backedUp.Add($name)
            }
        }
        foreach ($name in $managedNames) {
            $source = Join-Path $payloadDirectory $name
            Move-Item -LiteralPath $source -Destination (Join-Path $toolsDirectory $name) -Force
            $installed.Add($name)
        }
    }
    catch {
        Restore-PreviousTools $stageDirectory $installed.ToArray() $backedUp.ToArray()
        throw
    }

    Write-Host ''
    Write-Host 'Setup succeeded. Installed versions:'
    Write-Host "  yt-dlp: $ytVersion"
    Write-Host "  FFmpeg: $ffmpegVersion"
    Write-Host "  ffprobe: $ffprobeVersion"
    Write-Host "  Deno: $denoVersionText"
    Write-Host "  rsgain: $rsgainVersion"
    Write-Host 'You can now open BareBonesDownloader.exe.'
}
catch {
    Write-Host ''
    Write-Host "Setup failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Existing working tools were left in place unless the install phase had already begun; that phase rolls back on failure.'
    exit 1
}
finally {
    if ($stageDirectory -and (Test-Path -LiteralPath $stageDirectory)) {
        $resolvedTools = [IO.Path]::GetFullPath($toolsDirectory).TrimEnd('\') + '\'
        $resolvedStage = [IO.Path]::GetFullPath($stageDirectory)
        if (-not $resolvedStage.StartsWith($resolvedTools, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a staging directory outside tools/.'
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
