param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='1.2.0')
$ErrorActionPreference='Stop'
Push-Location -LiteralPath $PSScriptRoot
$infoPath=Join-Path $PSScriptRoot 'AppInfo.cs'
$infoText=[IO.File]::ReadAllText($infoPath,[Text.Encoding]::UTF8)
$infoText=[regex]::Replace($infoText,'Assembly(Version|FileVersion)\("[0-9.]+"\)',('Assembly$1("'+$Version+'.0")'))
[IO.File]::WriteAllText($infoPath,$infoText,[Text.UTF8Encoding]::new($false))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'}
. (Join-Path $PSScriptRoot 'BuildSupport.ps1')
$nativeRefs=@(Get-NativeWindowsReferences)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 '/out:Автопроверка_документов.exe' $nativeRefs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.dll /r:System.Web.Extensions.dll AuditProgram.cs AuditEngine.cs AdvancedAudit.cs FormAnalysis.cs QualityBenchmark.cs NativeWindows.cs ParallelWork.cs XlsxReader.cs Updates.cs AppInfo.cs
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
$ocrPath=Join-Path $PSScriptRoot 'Recognize.ps1'
[IO.File]::WriteAllText($ocrPath,[IO.File]::ReadAllText($ocrPath,[Text.Encoding]::UTF8),[Text.UTF8Encoding]::new($true))
$dist=Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($dist)|Out-Null
$package=Join-Path $dist 'document-photo-audit-windows.zip'
Compress-Archive -LiteralPath @((Join-Path $PSScriptRoot 'Автопроверка_документов.exe'),$ocrPath,(Join-Path $PSScriptRoot 'README.md')) -DestinationPath $package -Force
$hash=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($package+'.sha256'),$hash+'  document-photo-audit-windows.zip'+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Output $package
Pop-Location
