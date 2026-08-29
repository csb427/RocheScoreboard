[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SkipRestore,
    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "artifacts\release"
}
$projectPath = Join-Path $PSScriptRoot "Roche_Scoreboard\Roche_Scoreboard.csproj"
$quickStartPath = Join-Path $PSScriptRoot "packaging\README-FIRST.txt"

[xml]$project = Get-Content -LiteralPath $projectPath
$version = [string]$project.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Roche_Scoreboard.csproj must contain a three-part <Version>."
}
if ($Runtime -ne "win-x64") {
    throw "The supported public release runtime is win-x64."
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$packageFolderName = "Roche Scoreboard $version"
$stagingDirectory = Join-Path $resolvedOutputRoot $packageFolderName
$archiveName = "Roche-Scoreboard-v$version-Windows-x64.zip"
$archivePath = Join-Path $resolvedOutputRoot $archiveName
$checksumPath = Join-Path $resolvedOutputRoot "Roche-Scoreboard-v$version-SHA256.txt"

function Remove-ReleasePathSafely([string]$PathToRemove) {
    if (-not (Test-Path -LiteralPath $PathToRemove)) { return }

    $resolvedTarget = [System.IO.Path]::GetFullPath($PathToRemove)
    $outputPrefix = $resolvedOutputRoot.TrimEnd('\') + '\'
    if (-not $resolvedTarget.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the release output directory: $resolvedTarget"
    }
    if ($resolvedTarget -eq $resolvedOutputRoot) {
        throw "Refusing to remove the release output root."
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
Remove-ReleasePathSafely $stagingDirectory
Remove-ReleasePathSafely $archivePath
Remove-ReleasePathSafely $checksumPath
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$publishArguments = @(
    "publish", $projectPath,
    "--configuration", $Configuration,
    "--runtime", $Runtime,
    "--self-contained", "true",
    "--output", $stagingDirectory,
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false"
)
if ($SkipRestore) { $publishArguments += "--no-restore" }

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $quickStartPath -Destination (Join-Path $stagingDirectory "README-FIRST.txt") -Force

$exePath = Join-Path $stagingDirectory "Roche Scoreboard.exe"
$requiredFiles = @(
    $exePath,
    (Join-Path $stagingDirectory "Roche Scoreboard.dll"),
    (Join-Path $stagingDirectory "Roche Scoreboard.deps.json"),
    (Join-Path $stagingDirectory "Roche Scoreboard.runtimeconfig.json"),
    (Join-Path $stagingDirectory "coreclr.dll"),
    (Join-Path $stagingDirectory "hostfxr.dll"),
    (Join-Path $stagingDirectory "libvlc\win-x64\libvlc.dll"),
    (Join-Path $stagingDirectory "libvlc\win-x64\libvlccore.dll"),
    (Join-Path $stagingDirectory "README-FIRST.txt")
)
$missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missingFiles.Count -gt 0) {
    throw "Release validation failed. Missing files: $($missingFiles -join ', ')"
}

$pluginDirectory = Join-Path $stagingDirectory "libvlc\win-x64\plugins"
$pluginCount = @(Get-ChildItem -LiteralPath $pluginDirectory -Filter "*.dll" -File -Recurse).Count
if ($pluginCount -lt 100) {
    throw "Release validation failed: only $pluginCount LibVLC plugin DLLs were published."
}

$depsText = Get-Content -LiteralPath (Join-Path $stagingDirectory "Roche Scoreboard.deps.json") -Raw
if ($depsText -match 'SessionHandleIPCProxyStub|Microsoft\.WindowsAppSDK|Microsoft\.WindowsAppRuntime') {
    throw "Release validation failed: an unwanted Windows App SDK activation dependency is still present."
}

$publishedVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
if ([string]::IsNullOrWhiteSpace($publishedVersion) -or -not $publishedVersion.StartsWith($version, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release validation failed: EXE version '$publishedVersion' does not match '$version'."
}

if (-not $SkipSelfTest) {
    $selfTestPath = Join-Path $resolvedOutputRoot "startup-self-test-$version.json"
    Remove-ReleasePathSafely $selfTestPath
    # Start-Process flattens ArgumentList to one command line. Quote the value
    # explicitly because Windows user/profile paths commonly contain spaces.
    $selfTestArgument = "--startup-self-test-output=`"$selfTestPath`""
    $process = Start-Process -FilePath $exePath -ArgumentList $selfTestArgument -WorkingDirectory $stagingDirectory -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $selfTestPath)) {
        throw "Published application startup self-test failed."
    }
    $selfTest = Get-Content -LiteralPath $selfTestPath -Raw | ConvertFrom-Json
    if ($selfTest.startupException -or -not $selfTest.sportSelectionVisible -or $selfTest.windowCount -ne 1) {
        throw "Published application startup self-test returned an invalid state."
    }
    Remove-ReleasePathSafely $selfTestPath
}

Compress-Archive -LiteralPath $stagingDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $archiveName" -Encoding ascii

$archiveSizeMb = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 1)
Write-Host "Release package ready"
Write-Host "  Version:       $version"
Write-Host "  Package:       $archivePath"
Write-Host "  Checksum:      $checksumPath"
Write-Host "  Size:          $archiveSizeMb MB"
Write-Host "  LibVLC plugins: $pluginCount"
