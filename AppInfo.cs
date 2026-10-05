using System.Reflection;
[assembly: AssemblyTitle("Автопроверка документов")]
[assembly: AssemblyVersion("1.7.1.0")]
[assembly: AssemblyFileVersion("1.7.1.0")]
namespace DesktopUpdates {
    internal static class AppInfo {
        public const string Repository="debug23win/document-photo-audit",Product="document-photo-audit",Asset="document-photo-audit-windows.zip",Executable="Автопроверка_документов.exe";
        public static readonly string[] UpdateFiles={Executable,"Recognize.ps1","README.md"};
    }
}
