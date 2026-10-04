$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('audit-tests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $temp 'tests.exe'
. (Join-Path $root 'BuildSupport.ps1')
$nativeRefs=@(Get-NativeWindowsReferences)
& $compiler /nologo /codepage:65001 /target:exe (('/out:'+ $exe)) $nativeRefs /r:System.Drawing.dll /r:System.Web.dll /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $root 'AuditEngine.cs') (Join-Path $root 'AdvancedAudit.cs') (Join-Path $root 'FormAnalysis.cs') (Join-Path $root 'QualityBenchmark.cs') (Join-Path $root 'NativeWindows.cs') (Join-Path $root 'ParallelWork.cs') (Join-Path $root 'XlsxReader.cs') (Join-Path $PSScriptRoot 'AuditTest.cs')
if($LASTEXITCODE -ne 0){throw 'Audit test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Audit tests failed'}
