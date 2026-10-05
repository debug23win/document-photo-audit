$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('audit-parallel-tests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $temp 'tests.exe'
& $compiler /nologo /codepage:65001 /target:exe (('/out:'+ $exe)) (Join-Path $root 'ParallelWork.cs') (Join-Path $PSScriptRoot 'ParallelTest.cs')
if($LASTEXITCODE -ne 0){throw 'Parallel test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Parallel tests failed'}
