param([Parameter(Mandatory=$true)][string]$InputDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
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
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('ru'))
if ($null -eq $engine) { throw 'В Windows недоступно распознавание русского текста. Установите русский язык и компонент OCR в параметрах языков Windows.' }
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$images=@(Get-ChildItem -LiteralPath $InputDirectory -Filter '*.jpg' | Sort-Object Name)
$done=0
foreach($p in $images) {
  $file=Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($p.FullName)) ([Windows.Storage.StorageFile])
  $stream=Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  $bitmap=$null
  try {
    $decoder=Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap=Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $recognized=Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines=@($recognized.Lines | ForEach-Object {
      $ws=@($_.Words | ForEach-Object { @{text=$_.Text;x=$_.BoundingRect.X;y=$_.BoundingRect.Y;width=$_.BoundingRect.Width;height=$_.BoundingRect.Height} })
      @{text=$_.Text;words=$ws}
    })
    $page=@{file=$p.Name;width=$bitmap.PixelWidth;height=$bitmap.PixelHeight;text=($lines.text -join "`n");lines=$lines}
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory ($p.BaseName+'.json')),($page | ConvertTo-Json -Depth 7),[System.Text.UTF8Encoding]::new($false))
  } finally { if($null -ne $bitmap){$bitmap.Dispose()};$stream.Dispose() }
  $done++
  Write-Output ($done.ToString()+'/'+$images.Count.ToString()+' '+$p.Name)
}
