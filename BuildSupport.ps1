function Get-NativeWindowsReferences {
    $framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    if(!(Test-Path -LiteralPath $framework)){$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'}
    foreach($name in @('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.ObjectModel')) {
        $path=Join-Path $framework ($name+'.dll')
        if(Test-Path -LiteralPath $path){'/r:'+ $path}
    }
    $runtime=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Runtime.WindowsRuntime') -Filter 'System.Runtime.WindowsRuntime.dll' -Recurse | Select-Object -First 1
    if(!$runtime){throw 'Windows Runtime CLR adapter is missing'}
    '/r:'+ $runtime.FullName
    $sdk=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/UnionMetadata'
    $metadata=Get-ChildItem -LiteralPath $sdk -Filter 'Windows.winmd' -Recurse | Where-Object {$_.Directory.Name -ne 'Facade'} | Sort-Object FullName -Descending | Select-Object -First 1
    if(!$metadata){throw 'Windows 10/11 SDK UnionMetadata is required to build (not to run)'}
    '/r:'+ $metadata.FullName
}
