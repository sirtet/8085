param(
    [Parameter(Mandatory = $true)][string]$RomPath,
    [string]$HelloSource,
    [string]$FirmwareSource,
    [switch]$Ui
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$romFile = (Resolve-Path -LiteralPath $RomPath).Path
$msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($msbuildCommand) {
    $buildTool = $msbuildCommand.Source
} else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'MSBuild not found. Install Visual Studio with .NET desktop development.' }
    $buildTool = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $buildTool) { throw 'No Visual Studio MSBuild installation found.' }
}
& $buildTool (Join-Path $repoRoot 'Src\8085.csproj') /t:Build /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
& $buildTool (Join-Path $repoRoot 'Tests\Pkw3000Tests.csproj') /t:Build /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
$testArguments = @($romFile)
if ($HelloSource) { $testArguments += (Resolve-Path -LiteralPath $HelloSource).Path }
if ($FirmwareSource) { $testArguments += '--firmware'; $testArguments += (Resolve-Path -LiteralPath $FirmwareSource).Path }
if ($Ui) { $testArguments += '--ui' }
& (Join-Path $repoRoot 'Tests\bin\Pkw3000Tests.exe') @testArguments |
    Tee-Object -FilePath (Join-Path $repoRoot 'Tests\bin\latest-test.log')
if ($LASTEXITCODE -ne 0) { throw 'PKW-3000 tests failed. See Tests/bin/latest-test.log.' }
