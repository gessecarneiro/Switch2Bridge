param([string]$AssemblyPath = (Join-Path $PSScriptRoot 'bin\Switch2Bridge.exe'), [string]$OutputDirectory = (Join-Path $PSScriptRoot 'work\performance-benchmark'), [switch]$Legacy)
$ErrorActionPreference = 'Stop'
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$benchDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $benchDirectory | Out-Null
$copiedAssembly = Join-Path $benchDirectory ([IO.Path]::GetFileName($assemblyFile))
if ($assemblyFile -ne $copiedAssembly) { Copy-Item -LiteralPath $assemblyFile -Destination $copiedAssembly -Force }
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/optimize+',('/out:' + (Join-Path $benchDirectory 'PerformanceBench.exe')),('/reference:' + $copiedAssembly),(Join-Path $PSScriptRoot 'tests\PerformanceBench.cs'))
if ($Legacy) { $compilerArgs += '/define:LEGACY' } else { $compilerArgs += '/define:OPTIMIZED' }
& (Join-Path $frameworkPath 'csc.exe') $compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível compilar o benchmark.' }
& (Join-Path $benchDirectory 'PerformanceBench.exe') (Join-Path $benchDirectory 'resultado.txt')
if ($LASTEXITCODE -ne 0) { throw 'O benchmark falhou.' }
