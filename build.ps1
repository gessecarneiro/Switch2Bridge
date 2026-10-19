param(
    [string]$OutputName = 'Switch2Bridge.exe',
    [switch]$Test,
    [switch]$UiTest
)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x de 64 bits não encontrado.' }
$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$application = Join-Path $outputDirectory $OutputName
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$referenceArgs = @($refs | ForEach-Object { '/reference:' + (Join-Path $framework $_) })
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:' + $application),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/resource:' + (Join-Path $PSScriptRoot 'src\MainWindow.xaml') + ',MainWindow.xaml'))
$compilerArgs += $referenceArgs
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination ($application + '.config') -Force
Write-Output ('Compilado: ' + $application)
if ($Test -or $UiTest) {
    $testDirectory = Join-Path $outputDirectory 'tests'
    New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
    Copy-Item -LiteralPath $application -Destination $testDirectory -Force
    Copy-Item -LiteralPath ($application + '.config') -Destination $testDirectory -Force
    $testArgs = @('/nologo','/target:exe','/platform:x64','/utf8output',('/out:' + (Join-Path $testDirectory 'ProtocolTests.exe')),('/reference:' + $application),(Join-Path $PSScriptRoot 'tests\ProtocolTests.cs'))
    & $compiler $testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar testes.' }
    Push-Location $PSScriptRoot
    try {
        & (Join-Path $testDirectory 'ProtocolTests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
    } finally { Pop-Location }
}
if ($UiTest) {
    $uiDirectory = Join-Path $outputDirectory 'ui-tests'
    New-Item -ItemType Directory -Force -Path $uiDirectory | Out-Null
    $uiArgs = @('/nologo','/target:exe','/platform:x64','/utf8output',('/out:' + (Join-Path $uiDirectory 'UiSmoke.exe')))
    $uiArgs += $referenceArgs
    $uiArgs += Join-Path $PSScriptRoot 'tests\UiSmoke.cs'
    & $compiler $uiArgs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar teste de interface.' }
    & (Join-Path $uiDirectory 'UiSmoke.exe') $application (Join-Path $uiDirectory 'renders')
    if ($LASTEXITCODE -ne 0) { throw 'O teste de interface falhou.' }
}
