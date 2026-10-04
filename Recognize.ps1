param([string]$InputDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$PdfInput,[ValidateRange(1,1000)][int]$MaxPages=1000,[switch]$UseCache)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]
$asTask=([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($operation,$type) { $task=$asTask.MakeGenericMethod($type).Invoke($null,@($operation));$task.Wait();$task.Result }
if ($PdfInput) {
  $null=[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
  $null=[Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime]
  $actionTask=([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and !$_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
  $pdfFile=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync([IO.Path]::GetFullPath($PdfInput))) ([Windows.Storage.StorageFile])
  $document=Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($pdfFile)) ([Windows.Data.Pdf.PdfDocument])
  if ($document.IsPasswordProtected) { throw 'PDF защищён паролем. Сохраните доступную для чтения копию и загрузите её.' }
  if ($document.PageCount -gt $MaxPages) { throw ('Слишком много страниц PDF: '+$document.PageCount+'. Осталось допустимых страниц: '+$MaxPages) }
  [IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
  for ($i=0;$i -lt $document.PageCount;$i++) {
    $page=$document.GetPage([uint32]$i)
    $target=Join-Path $OutputDirectory ('page-'+($i+1).ToString('D4')+'.png')
    [IO.File]::WriteAllBytes($target,[byte[]]@())
    $targetFile=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($target)) ([Windows.Storage.StorageFile])
    $targetStream=Await ($targetFile.OpenAsync([Windows.Storage.FileAccessMode]::ReadWrite)) ([Windows.Storage.Streams.IRandomAccessStream])
    try {
      $options=[Windows.Data.Pdf.PdfPageRenderOptions]::new()
      $scale=[Math]::Min(3.125,5000.0/[Math]::Max($page.Size.Width,$page.Size.Height))
      $options.DestinationWidth=[uint32][Math]::Max(1,[Math]::Round($page.Size.Width*$scale))
      $options.DestinationHeight=[uint32][Math]::Max(1,[Math]::Round($page.Size.Height*$scale))
      $task=$actionTask.Invoke($null,@($page.RenderToStreamAsync($targetStream,$options)));$task.Wait()
    } finally { $targetStream.Dispose();$page.Dispose() }
    Write-Output (($i+1).ToString()+'/'+$document.PageCount.ToString()+' PDF')
  }
  exit 0
}
if (!$InputDirectory) { throw 'Не указана папка изображений.' }
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('ru'))
if ($null -eq $engine) { throw 'В Windows недоступно распознавание русского текста. Установите русский язык и компонент OCR в параметрах языков Windows.' }
$englishEngine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('en'))
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$images=@(Get-ChildItem -LiteralPath $InputDirectory -File | Where-Object { $_.Extension -in '.jpg','.png' } | Sort-Object Name)
$done=0
foreach($p in $images) {
  $cached=Join-Path $OutputDirectory ($p.BaseName+'.json')
  if ($UseCache -and (Test-Path -LiteralPath $cached)) {
    try { $prior=[IO.File]::ReadAllText($cached,[Text.Encoding]::UTF8) | ConvertFrom-Json
      if ($prior.width -gt 0 -and $prior.height -gt 0 -and $null -ne $prior.lines) { $done++;Write-Output ($done.ToString()+'/'+$images.Count.ToString()+' cache '+$p.Name);continue }
    } catch {}
  }
  $file=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($p.FullName)) ([Windows.Storage.StorageFile])
  $stream=Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  $bitmap=$null
  try {
    $decoder=Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    if ($bitmap.PixelWidth -gt [Windows.Media.Ocr.OcrEngine]::MaxImageDimension -or $bitmap.PixelHeight -gt [Windows.Media.Ocr.OcrEngine]::MaxImageDimension) { throw ('Размер изображения превышает предел Windows OCR: '+$p.Name) }
    $activeEngine=$engine
    if ($p.Name -match '-CRC-[23]\.png$' -and $null -ne $englishEngine) { $activeEngine=$englishEngine }
    $recognized=Await ($activeEngine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines=@($recognized.Lines | ForEach-Object {
      $ws=@($_.Words | ForEach-Object { @{text=$_.Text;x=$_.BoundingRect.X;y=$_.BoundingRect.Y;width=$_.BoundingRect.Width;height=$_.BoundingRect.Height} })
      @{text=$_.Text;words=$ws}
    })
    $page=@{language=$activeEngine.RecognizerLanguage.LanguageTag;file=$p.Name;width=$bitmap.PixelWidth;height=$bitmap.PixelHeight;text=($lines.text -join "`n");lines=$lines}
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory ($p.BaseName+'.json')),($page | ConvertTo-Json -Depth 7),[System.Text.UTF8Encoding]::new($false))
  } finally { if($null -ne $bitmap){$bitmap.Dispose()};$stream.Dispose() }
  $done++
  Write-Output ($done.ToString()+'/'+$images.Count.ToString()+' '+$p.Name)
}
