$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('inventory-selection-tests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
$app=Join-Path $root 'Автопроверка_документов.exe'
Copy-Item -LiteralPath $app -Destination $temp
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $temp 'tests.exe'
& $compiler /nologo /codepage:65001 /target:exe ('/out:'+$exe) ('/r:'+$app) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'InventorySelectionTest.cs')
if($LASTEXITCODE -ne 0){throw 'Inventory selection test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Inventory selection tests failed'}
