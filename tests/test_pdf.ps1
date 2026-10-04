$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('audit-pdf-tests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp)|Out-Null
$pdf=Join-Path $temp 'synthetic-two-pages.pdf'
$objects=@(
  '<< /Type /Catalog /Pages 2 0 R >>',
  '<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>',
  '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 600] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>',
  '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 600] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>',
  '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
)
$first='BT /F1 20 Tf 40 500 Td (SYNTHETIC TEST PAGE ONE) Tj ET'
$second='BT /F1 20 Tf 40 500 Td (SYNTHETIC TEST PAGE TWO) Tj ET'
$objects+=('<< /Length '+$first.Length+">>`nstream`n"+$first+"`nendstream")
$objects+=('<< /Length '+$second.Length+">>`nstream`n"+$second+"`nendstream")
$body="%PDF-1.4`n"
$offsets=@(0)
for($i=0;$i -lt $objects.Count;$i++){$offsets+=[Text.Encoding]::ASCII.GetByteCount($body);$body+=($i+1).ToString()+" 0 obj`n"+$objects[$i]+"`nendobj`n"}
$xref=[Text.Encoding]::ASCII.GetByteCount($body)
$body+="xref`n0 8`n0000000000 65535 f `n"
for($i=1;$i -lt $offsets.Count;$i++){$body+=$offsets[$i].ToString('D10')+" 00000 n `n"}
$body+="trailer`n<< /Size 8 /Root 1 0 R >>`nstartxref`n"+$xref+"`n%%EOF`n"
[IO.File]::WriteAllBytes($pdf,[Text.Encoding]::ASCII.GetBytes($body))
$out=Join-Path $temp 'rendered'
$windowsPowerShell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Recognize.ps1') -PdfInput $pdf -OutputDirectory $out -MaxPages 2
if($LASTEXITCODE -ne 0){throw 'PDF rendering failed'}
$pages=@(Get-ChildItem -LiteralPath $out -Filter '*.png')
if($pages.Count -ne 2){throw 'Multi-page PDF count is wrong'}
Add-Type -AssemblyName System.Drawing
foreach($page in $pages){$image=[Drawing.Image]::FromFile($page.FullName);try{if($image.Width -lt 1000 -or $image.Height -lt 1000){throw 'PDF render resolution too low'}}finally{$image.Dispose()}}
$limitOut=Join-Path $temp 'limit'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'Recognize.ps1') -PdfInput $pdf -OutputDirectory $limitOut -MaxPages 1 2>$null
if($LASTEXITCODE -eq 0){throw 'PDF page limit must reject excess pages'}
Write-Output 'PDF tests passed: two pages, PNG decode, render resolution and page-limit rejection'
