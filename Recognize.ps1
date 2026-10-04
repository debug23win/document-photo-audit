param([string]$InputDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$PdfInput,[ValidateRange(1,1000)][int]$MaxPages=1000,[switch]$UseCache,[ValidateRange(0,8)][int]$Workers=0)
# Compatibility entry point for older packages and developer tests.
# The graphical application uses NativeWindows directly and never launches PowerShell.
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'Автопроверка_документов.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Native OCR application is missing'}
function Quote-Path([string]$value){if($value.Contains('"')){throw 'Invalid quote in path'};'"'+[IO.Path]::GetFullPath($value)+'"'}
if($PdfInput){$arguments=@('--render-pdf',(Quote-Path $PdfInput),(Quote-Path $OutputDirectory),$MaxPages,$Workers)}
elseif($InputDirectory){$arguments=@('--ocr',(Quote-Path $InputDirectory),(Quote-Path $OutputDirectory),$Workers,([int][bool]$UseCache))}
else{throw 'InputDirectory or PdfInput is required'}
$process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
exit $process.ExitCode
