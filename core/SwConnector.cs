using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BOMManager.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BOMManager.Core
{
    public class SwConnector : ISolidWorksService
    {
        private SldWorks? _swApp;
        private readonly List<CachedComponentRecord> _cachedCompRecords = new();
        private static readonly string LogFile = @"c:\Temp\BOM_Manager\addin_debug.log";

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SwConnector] {message}\r\n");
            }
            catch { }
        }

        public bool IsConnected => _swApp != null;

        public (bool Success, string Message) Connect()
        {
            string[] progIds = { "SldWorks.Application", "SldWorks.Application.29", "SldWorks.Application.30" };
            foreach (var progId in progIds)
            {
                try
                {
                    _swApp = (SldWorks)Marshal.GetActiveObject(progId);
                    if (_swApp != null)
                    {
                        string rev = _swApp.RevisionNumber();
                        Log($"SolidWorks {rev} 연결 성공 (ProgId={progId})");
                        return (true, $"SolidWorks {rev} 연결 성공");
                    }
                }
                catch { }
            }

            Log("실행 중인 SolidWorks 인스턴스를 찾을 수 없습니다.");
            return (false, "실행 중인 SolidWorks 인스턴스를 찾을 수 없습니다.");
        }

        public AssemblyInfo GetActiveAssemblyInfo()
        {
            var info = new AssemblyInfo();
            if (_swApp == null)
            {
                Connect();
            }

            if (_swApp == null)
            {
                info.ErrorMessage = "SolidWorks에 연결되지 않았습니다.";
                return info;
            }

            try
            {
                var model = (ModelDoc2)_swApp.ActiveDoc;
                if (model == null)
                {
                    info.ErrorMessage = "열려 있는 문서가 없습니다.";
                    return info;
                }

                int docType = model.GetType();
                if (docType != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    info.ErrorMessage = "활성 문서가 어셈블리(.sldasm)가 아닙니다.";
                    return info;
                }

                info.Title = model.GetTitle();
                info.Path = model.GetPathName();
                info.IsConnected = true;

                var conf = model.GetActiveConfiguration() as Configuration;
                if (conf != null)
                {
                    info.ActiveConfiguration = conf.Name;
                }

                var assy = (AssemblyDoc)model;
                info.TotalComponentsCount = assy.GetComponentCount(false);
            }
            catch (Exception ex)
            {
                info.ErrorMessage = $"어셈블리 정보 조회 오류: {ex.Message}";
            }

            return info;
        }

        public (List<BOMItem> Items, string? Error) LoadBom(bool topLevelOnly = false, bool includeSuppressed = false)
        {
            if (_swApp == null)
            {
                var (ok, msg) = Connect();
                if (!ok) return (new List<BOMItem>(), msg);
            }

            _cachedCompRecords.Clear();
            var partMap = new Dictionary<(string Path, string Config), PartAggregateData>();
            var orderedKeys = new List<(string Path, string Config)>();

            try
            {
                var model = (ModelDoc2)_swApp!.ActiveDoc;
                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    return (new List<BOMItem>(), "활성 SolidWorks 어셈블리가 없습니다.");
                }

                var assy = (AssemblyDoc)model;
                object[]? rawComponents = (object[])assy.GetComponents(topLevelOnly);
                if (rawComponents == null || rawComponents.Length == 0)
                {
                    return (new List<BOMItem>(), null);
                }

                foreach (var obj in rawComponents)
                {
                    if (obj is not Component2 comp) continue;

                    try
                    {
                        if (comp.IsEnvelope()) continue;
                        if (!includeSuppressed && comp.IsSuppressed()) continue;

                        string filePath = comp.GetPathName();
                        if (string.IsNullOrEmpty(filePath)) continue;

                        string normPath = Path.GetFullPath(filePath).ToLowerInvariant();
                        bool isSub = normPath.EndsWith(".sldasm");
                        string compName = comp.Name2 ?? string.Empty;
                        string cleanName = ExtractLeafName(compName);

                        // 계층 깊이(level) 계산
                        int level = 0;
                        if (!string.IsNullOrEmpty(compName))
                        {
                            int atCount = compName.Count(c => c == '@');
                            int slashCount = compName.Count(c => c == '/');
                            level = Math.Max(atCount, slashCount);
                        }

                        // GetParent() 순회를 통한 부모 경로 수집
                        var parentPaths = new List<string>();
                        var parentNames = new List<string>();
                        try
                        {
                            var parentComp = comp.GetParent();
                            int pDepth = 0;
                            while (parentComp != null)
                            {
                                pDepth++;
                                string pPath = parentComp.GetPathName();
                                if (!string.IsNullOrEmpty(pPath))
                                {
                                    parentPaths.Add(Path.GetFullPath(pPath).ToLowerInvariant());
                                }
                                string pName = parentComp.Name2 ?? string.Empty;
                                if (!string.IsNullOrEmpty(pName))
                                {
                                    parentNames.Add(pName.ToLowerInvariant().Trim());
                                }
                                parentComp = parentComp.GetParent();
                            }
                            level = Math.Max(level, pDepth);
                        }
                        catch { }

                        // Isolate 초고속 처리를 위한 캐싱
                        _cachedCompRecords.Add(new CachedComponentRecord
                        {
                            Component = comp,
                            Path = normPath,
                            IsSubassembly = isSub,
                            Name = compName.ToLowerInvariant().Trim(),
                            CleanName = cleanName,
                            Level = level,
                            ParentPaths = parentPaths,
                            ParentNames = parentNames
                        });

                        string configName = comp.ReferencedConfiguration ?? "Default";
                        var key = (normPath, configName.ToLowerInvariant());

                        if (!partMap.ContainsKey(key))
                        {
                            partMap[key] = new PartAggregateData
                            {
                                Count = 1,
                                Component = comp,
                                FilePath = filePath,
                                Configuration = configName,
                                IsSubassembly = isSub,
                                Level = level
                            };
                            orderedKeys.Add(key);
                        }
                        else
                        {
                            partMap[key].Count++;
                            if (level > partMap[key].Level)
                            {
                                partMap[key].Level = level;
                            }
                        }
                    }
                    catch { }
                }

                // BOMItem 목록 구성 및 커스텀 속성 읽기
                var items = new List<BOMItem>();
                int itemNo = 1;

                foreach (var key in orderedKeys)
                {
                    var data = partMap[key];
                    var comp = data.Component;

                    string partName = Path.GetFileNameWithoutExtension(data.FilePath);
                    string drawingNo = ReadCustomProperty(comp, "DrawingNo") ?? ReadCustomProperty(comp, "도면번호") ?? ReadCustomProperty(comp, "DWG_NO") ?? ReadCustomProperty(comp, "DwgNo") ?? ReadCustomProperty(comp, "Drawing_No") ?? "";
                    string material = ReadCustomProperty(comp, "Material") ?? ReadCustomProperty(comp, "재질") ?? "";
                    string rev = ReadCustomProperty(comp, "Rev") ?? ReadCustomProperty(comp, "Revision") ?? "";
                    string explanation = ReadCustomProperty(comp, "설명충") ?? ReadCustomProperty(comp, "설명") ?? ReadCustomProperty(comp, "Explanation") ?? ReadCustomProperty(comp, "Description") ?? "";
                    string remark = ReadCustomProperty(comp, "Remark") ?? ReadCustomProperty(comp, "비고") ?? "";
                    string isCommonStr = ReadCustomProperty(comp, "CommonPart") ?? ReadCustomProperty(comp, "공용품") ?? "";
                    bool isCommon = isCommonStr.Equals("Y", StringComparison.OrdinalIgnoreCase) || isCommonStr.Equals("True", StringComparison.OrdinalIgnoreCase);

                    var item = new BOMItem
                    {
                        ItemNo = itemNo++,
                        PartName = partName,
                        DrawingNo = drawingNo,
                        Material = material,
                        Qty = data.Count,
                        Rev = rev,
                        Explanation = explanation,
                        Remark = remark,
                        FileName = Path.GetFileName(data.FilePath),
                        FilePath = data.FilePath,
                        Configuration = data.Configuration,
                        IsCommonPart = isCommon,
                        IsSubassembly = data.IsSubassembly,
                        Level = data.Level,
                        IsOpaque = true,
                        IsExpanded = true
                    };

                    item.SnapshotOriginalValues();
                    items.Add(item);
                }

                return (items, null);
            }
            catch (Exception ex)
            {
                return (new List<BOMItem>(), $"BOM 데이터 읽기 실패: {ex.Message}");
            }
        }

        public (int SuccessCount, int FailCount, List<string> Errors) ApplyPropertiesToSolidWorks(IEnumerable<BOMItem> items)
        {
            if (_swApp == null)
            {
                return (0, items.Count(), new List<string> { "SolidWorks에 연결되어 있지 않습니다." });
            }

            int success = 0;
            int fail = 0;
            var errors = new List<string>();

            foreach (var item in items)
            {
                if (!item.IsModified)
                {
                    success++;
                    continue;
                }

                try
                {
                    if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath))
                    {
                        fail++;
                        errors.Add($"파일 경로를 찾을 수 없음: {item.PartName}");
                        continue;
                    }

                    int errorsDoc = 0;
                    int warningsDoc = 0;
                    int docType = item.IsSubassembly ? (int)swDocumentTypes_e.swDocASSEMBLY : (int)swDocumentTypes_e.swDocPART;

                    var modelDoc = (ModelDoc2)_swApp.OpenDoc6(
                        item.FilePath,
                        docType,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                        item.Configuration,
                        ref errorsDoc,
                        ref warningsDoc
                    );

                    if (modelDoc != null)
                    {
                        var ext = modelDoc.Extension;
                        var propMgr = ext.get_CustomPropertyManager(item.Configuration) ?? ext.get_CustomPropertyManager("");

                        if (propMgr != null)
                        {
                            SetCustomProperty(propMgr, "DrawingNo", item.DrawingNo);
                            SetCustomProperty(propMgr, "도면번호", item.DrawingNo);
                            SetCustomProperty(propMgr, "Material", item.Material);
                            SetCustomProperty(propMgr, "Rev", item.Rev);
                            SetCustomProperty(propMgr, "설명충", item.Explanation);
                            SetCustomProperty(propMgr, "설명", item.Explanation);
                            SetCustomProperty(propMgr, "Explanation", item.Explanation);
                            SetCustomProperty(propMgr, "Remark", item.Remark);
                            SetCustomProperty(propMgr, "CommonPart", item.IsCommonPart ? "Y" : "N");
                        }

                        modelDoc.SetSaveFlag();
                        modelDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errorsDoc, ref warningsDoc);
                        _swApp.CloseDoc(item.FilePath);

                        item.SnapshotOriginalValues();
                        success++;
                    }
                    else
                    {
                        fail++;
                        errors.Add($"문서 열기 실패: {item.PartName}");
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.Add($"속성 반영 오류 ({item.PartName}): {ex.Message}");
                }
            }

            return (success, fail, errors);
        }

        public bool SetComponentsTransparency(IEnumerable<BOMItem> targetItems, IEnumerable<BOMItem> allItems, bool isolateMode = true)
        {
            var targetList = targetItems.ToList();
            var allList = allItems.ToList();

            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetFnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetPnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetSubassemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetSubassemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetSubassemblyFnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in targetList)
            {
                bool isSub = item.IsSubassembly || (!string.IsNullOrEmpty(item.FilePath) && item.FilePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    string norm = Path.GetFullPath(item.FilePath).ToLowerInvariant();
                    targetPaths.Add(norm);
                    string fn = Path.GetFileName(norm);
                    targetFnames.Add(fn);
                    if (isSub)
                    {
                        targetSubassemblyPaths.Add(norm);
                        targetSubassemblyFnames.Add(fn);
                    }
                }
                if (!string.IsNullOrEmpty(item.FileName))
                {
                    string fn = item.FileName.Trim().ToLowerInvariant();
                    targetFnames.Add(fn);
                    string baseName = Path.GetFileNameWithoutExtension(fn);
                    targetNames.Add(baseName);
                    if (isSub || fn.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                    {
                        targetSubassemblyFnames.Add(fn);
                        targetSubassemblyNames.Add(baseName);
                    }
                }
                if (!string.IsNullOrEmpty(item.PartName))
                {
                    string pClean = item.PartName.Trim().ToLowerInvariant();
                    targetPnames.Add(pClean);
                    if (isSub)
                    {
                        targetSubassemblyNames.Add(pClean);
                    }
                }
            }

            // 1단계: 직접 선택된 서브어셈블리 식별
            var targetSubIndices = new HashSet<int>();
            for (int idx = 0; idx < allList.Count; idx++)
            {
                var item = allList[idx];
                string itemPath = !string.IsNullOrEmpty(item.FilePath) ? Path.GetFullPath(item.FilePath).ToLowerInvariant() : "";
                string itemPart = item.PartName.Trim().ToLowerInvariant();
                string itemFname = !string.IsNullOrEmpty(itemPath) ? Path.GetFileName(itemPath) : "";
                bool itemIsSub = item.IsSubassembly || itemPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);

                bool isDirect = targetPaths.Contains(itemPath) ||
                                targetFnames.Contains(itemFname) ||
                                targetPnames.Contains(itemPart) ||
                                targetNames.Contains(itemPart);

                if (isDirect && itemIsSub)
                {
                    targetSubIndices.Add(idx);
                }
            }

            // 2단계: 대상 서브어셈블리의 모든 하위 자식(descendants)을 불투명(🟢) 대상에 포함
            var matchedIndices = new HashSet<int>();
            foreach (int subIdx in targetSubIndices)
            {
                matchedIndices.Add(subIdx);
                int subLevel = allList[subIdx].Level;
                for (int j = subIdx + 1; j < allList.Count; j++)
                {
                    int childLevel = allList[j].Level;
                    if (childLevel > subLevel)
                    {
                        matchedIndices.Add(j);
                        var cItem = allList[j];
                        if (!string.IsNullOrEmpty(cItem.FilePath))
                        {
                            string cP = Path.GetFullPath(cItem.FilePath).ToLowerInvariant();
                            targetPaths.Add(cP);
                            targetFnames.Add(Path.GetFileName(cP));
                        }
                        if (!string.IsNullOrEmpty(cItem.PartName))
                        {
                            targetPnames.Add(cItem.PartName.Trim().ToLowerInvariant());
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // 3단계: 각 BOMItem is_opaque 플래그 결정
            for (int idx = 0; idx < allList.Count; idx++)
            {
                var item = allList[idx];
                string itemPath = !string.IsNullOrEmpty(item.FilePath) ? Path.GetFullPath(item.FilePath).ToLowerInvariant() : "";
                string itemPart = item.PartName.Trim().ToLowerInvariant();
                string itemFname = !string.IsNullOrEmpty(itemPath) ? Path.GetFileName(itemPath) : "";
                bool itemIsSub = item.IsSubassembly || itemPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);

                if (matchedIndices.Contains(idx))
                {
                    item.IsOpaque = true;
                }
                else
                {
                    bool isDirect = targetPaths.Contains(itemPath) ||
                                    targetFnames.Contains(itemFname) ||
                                    targetPnames.Contains(itemPart) ||
                                    targetNames.Contains(itemPart);

                    if (itemIsSub && !targetSubIndices.Contains(idx))
                    {
                        isDirect = false;
                    }
                    item.IsOpaque = isDirect;
                }
            }

            // 4단계: SolidWorks CAD 뷰포트에 투명도(Transparency) 적용
            if (_swApp == null)
            {
                Connect();
            }

            if (_swApp != null)
            {
                try
                {
                    var model = (ModelDoc2)_swApp.ActiveDoc;
                    if (model != null && model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                    {
                        EnsureCachedCompRecords(model);

                        // 무색 투명 유리 머티리얼 (85% 투명도)
                        // [R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission]
                        double[] glassMat = new double[] { 1.0, 1.0, 1.0, 0.5, 0.5, 0.5, 0.3, 0.85, 0.0 };

                        foreach (var rec in _cachedCompRecords)
                        {
                            var comp = rec.Component;
                            if (comp == null) continue;

                            if (rec.IsSubassembly)
                            {
                                try { comp.RemoveMaterialProperty2(1, null); } catch { }
                                try { comp.RemoveMaterialProperty2(2, null); } catch { }
                                continue;
                            }

                            bool isUnderTargetSub = false;
                            foreach (var pp in rec.ParentPaths)
                            {
                                if (targetSubassemblyPaths.Contains(pp) || targetSubassemblyFnames.Contains(Path.GetFileName(pp)))
                                {
                                    isUnderTargetSub = true;
                                    break;
                                }
                            }
                            if (!isUnderTargetSub)
                            {
                                foreach (var pn in rec.ParentNames)
                                {
                                    if (targetSubassemblyNames.Contains(pn) || targetSubassemblyNames.Any(sn => sn.Length >= 3 && (pn == sn || pn.Contains(sn))))
                                    {
                                        isUnderTargetSub = true;
                                        break;
                                    }
                                }
                            }

                            string compFname = !string.IsNullOrEmpty(rec.Path) ? Path.GetFileName(rec.Path) : "";
                            bool isTarget = targetPaths.Contains(rec.Path) ||
                                            targetFnames.Contains(compFname) ||
                                            targetNames.Contains(rec.CleanName) ||
                                            targetPnames.Contains(rec.CleanName) ||
                                            targetNames.Contains(rec.Name) ||
                                            isUnderTargetSub;

                            if (isTarget)
                            {
                                // 100% 완전 불투명 (원본 CAD 색상 복원)
                                try { comp.RemoveMaterialProperty2(1, null); } catch { }
                                try { comp.RemoveMaterialProperty2(2, null); } catch { }
                            }
                            else
                            {
                                // 85% 투명 유리 적용
                                try
                                {
                                    comp.MaterialPropertyValues = glassMat;
                                }
                                catch { }
                            }
                        }

                        model.ClearSelection2(true);
                        model.GraphicsRedraw2();
                    }
                }
                catch (Exception ex)
                {
                    Log($"SetComponentsTransparency 예외: {ex.Message}");
                }
            }

            return true;
        }

        public bool ShowAllOpaque(IEnumerable<BOMItem> allItems)
        {
            foreach (var item in allItems)
            {
                item.IsOpaque = true;
            }

            if (_swApp == null)
            {
                Connect();
            }

            if (_swApp != null)
            {
                try
                {
                    var model = (ModelDoc2)_swApp.ActiveDoc;
                    if (model != null && model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                    {
                        EnsureCachedCompRecords(model);

                        foreach (var rec in _cachedCompRecords)
                        {
                            var comp = rec.Component;
                            if (comp == null) continue;
                            try { comp.RemoveMaterialProperty2(1, null); } catch { }
                            try { comp.RemoveMaterialProperty2(2, null); } catch { }
                        }

                        model.ClearSelection2(true);
                        model.GraphicsRedraw2();
                    }
                }
                catch (Exception ex)
                {
                    Log($"ShowAllOpaque 예외: {ex.Message}");
                }
            }

            return true;
        }

        private void EnsureCachedCompRecords(ModelDoc2 model)
        {
            if (_cachedCompRecords.Count > 0) return;

            var assy = (AssemblyDoc)model;
            object[]? rawComponents = (object[])assy.GetComponents(false);
            if (rawComponents == null || rawComponents.Length == 0) return;

            foreach (var obj in rawComponents)
            {
                if (obj is not Component2 comp) continue;
                try
                {
                    if (comp.IsEnvelope()) continue;
                    string filePath = comp.GetPathName();
                    if (string.IsNullOrEmpty(filePath)) continue;

                    string normPath = Path.GetFullPath(filePath).ToLowerInvariant();
                    bool isSub = normPath.EndsWith(".sldasm");
                    string compName = comp.Name2 ?? string.Empty;
                    string cleanName = ExtractLeafName(compName);

                    int level = 0;
                    if (!string.IsNullOrEmpty(compName))
                    {
                        int atCount = compName.Count(c => c == '@');
                        int slashCount = compName.Count(c => c == '/');
                        level = Math.Max(atCount, slashCount);
                    }

                    var parentPaths = new List<string>();
                    var parentNames = new List<string>();
                    try
                    {
                        var parentComp = comp.GetParent();
                        int pDepth = 0;
                        while (parentComp != null)
                        {
                            pDepth++;
                            string pPath = parentComp.GetPathName();
                            if (!string.IsNullOrEmpty(pPath))
                            {
                                parentPaths.Add(Path.GetFullPath(pPath).ToLowerInvariant());
                            }
                            string pName = parentComp.Name2 ?? string.Empty;
                            if (!string.IsNullOrEmpty(pName))
                            {
                                parentNames.Add(pName.ToLowerInvariant().Trim());
                            }
                            parentComp = parentComp.GetParent();
                        }
                        level = Math.Max(level, pDepth);
                    }
                    catch { }

                    _cachedCompRecords.Add(new CachedComponentRecord
                    {
                        Component = comp,
                        Path = normPath,
                        IsSubassembly = isSub,
                        Name = compName.ToLowerInvariant().Trim(),
                        CleanName = cleanName,
                        Level = level,
                        ParentPaths = parentPaths,
                        ParentNames = parentNames
                    });
                }
                catch { }
            }
        }

        private static string ExtractLeafName(string compName)
        {
            if (string.IsNullOrEmpty(compName)) return string.Empty;
            string seg = compName.Contains('/') ? compName.Split('/').Last() : compName.Split('@').First();
            return seg.Split('-').First().Trim().ToLowerInvariant();
        }

        private static string? ReadCustomProperty(Component2 comp, string propName)
        {
            try
            {
                var model = comp.GetModelDoc2() as ModelDoc2;
                if (model != null)
                {
                    var ext = model.Extension;
                    var propMgr = ext.get_CustomPropertyManager(comp.ReferencedConfiguration) ?? ext.get_CustomPropertyManager("");
                    if (propMgr != null)
                    {
                        string valOut = "";
                        string resValOut = "";
                        bool wasResolved = false;
                        bool linkToProp = false;
                        int ret = propMgr.Get6(propName, false, out valOut, out resValOut, out wasResolved, out linkToProp);
                        if (!string.IsNullOrEmpty(resValOut)) return resValOut;
                        if (!string.IsNullOrEmpty(valOut)) return valOut;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SetCustomProperty(CustomPropertyManager propMgr, string propName, string value)
        {
            try
            {
                propMgr.Add3(propName, (int)swCustomInfoType_e.swCustomInfoText, value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch { }
        }

        private class CachedComponentRecord
        {
            public Component2? Component { get; set; }
            public string Path { get; set; } = "";
            public bool IsSubassembly { get; set; }
            public string Name { get; set; } = "";
            public string CleanName { get; set; } = "";
            public int Level { get; set; }
            public List<string> ParentPaths { get; set; } = new();
            public List<string> ParentNames { get; set; } = new();
        }

        private class PartAggregateData
        {
            public int Count { get; set; }
            public Component2 Component { get; set; } = null!;
            public string FilePath { get; set; } = "";
            public string Configuration { get; set; } = "Default";
            public bool IsSubassembly { get; set; }
            public int Level { get; set; }
        }
    }
}
