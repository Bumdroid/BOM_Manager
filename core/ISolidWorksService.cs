using System.Collections.Generic;
using BOMManager.Models;

namespace BOMManager.Core
{
    public interface ISolidWorksService
    {
        bool IsConnected { get; }
        (bool Success, string Message) Connect();
        AssemblyInfo GetActiveAssemblyInfo();
        (List<BOMItem> Items, string? Error) LoadBom(bool topLevelOnly = false, bool includeSuppressed = false);
        (int SuccessCount, int FailCount, List<string> Errors) ApplyPropertiesToSolidWorks(IEnumerable<BOMItem> items);
        bool SetComponentsTransparency(IEnumerable<BOMItem> targetItems, IEnumerable<BOMItem> allItems, bool isolateMode = true);
        bool ShowAllOpaque(IEnumerable<BOMItem> allItems);
        (bool Success, string Message) OpenDocument(string filePath);
        (bool Success, int CreatedCount, List<string> Messages) ApplySubAssembliesToFile(IEnumerable<BOMItem> allItems, string? baseDirectory = null);
        (bool Success, int CopiedCount, string TargetAuto3DDir, List<string> Messages) ExportOrganizedAuto3DFiles(IEnumerable<BOMItem> allItems, string? baseDirectory = null);
    }
}
