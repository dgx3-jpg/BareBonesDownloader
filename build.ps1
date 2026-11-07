$ErrorActionPreference = 'Stop'

$windowsDirectory = $env:WINDIR
if ([string]::IsNullOrWhiteSpace($windowsDirectory)) {
    $windowsDirectory = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Windows)
}
if ([string]::IsNullOrWhiteSpace($windowsDirectory)) {
    throw 'Windows could not determine its Windows directory.'
}

$framework = Join-Path $windowsDirectory 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fallback = Join-Path $windowsDirectory 'Microsoft.NET\Framework\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $framework)) {
    if (Test-Path -LiteralPath $fallback) {
        $framework = $fallback
    }
    else {
        throw 'The .NET Framework 4.x C# compiler was not found. Windows .NET Framework 4.8 is required.'
    }
}

$sourceDirectory = Join-Path $PSScriptRoot 'src'
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object { 'src\' + $_.Name })
if ($sources.Count -eq 0) {
    throw "No C# source files were found in $sourceDirectory."
}

$output = 'BareBonesDownloader.exe'
$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/out:' + $output),
    '/win32icon:assets\BareBonesDownloader.ico',
    '/resource:assets\BareBonesDownloader.ico,BareBonesDownloader.AppIcon.ico',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Core.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Net.Http.dll'
) + $sources

Write-Host 'Building BareBonesDownloader.exe...'
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $framework
$start.Arguments = $arguments -join ' '
$start.WorkingDirectory = $PSScriptRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
if (-not $process.Start()) {
    throw 'Windows could not start the .NET Framework C# compiler.'
}
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$stdout = $stdoutTask.Result
$stderr = $stderrTask.Result
if (-not [string]::IsNullOrWhiteSpace($stdout)) {
    Write-Output $stdout.TrimEnd()
}
if (-not [string]::IsNullOrWhiteSpace($stderr)) {
    [Console]::Error.WriteLine($stderr.TrimEnd())
}
if ($process.ExitCode -ne 0) {
    throw "The C# compiler exited with code $($process.ExitCode)."
}

Write-Host "Built $output"
