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
    public class SwConnector : ISolidWorksService, IDisposable
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
            // 기존 COM 핸들이 존재하면 안전하게 정리
            CleanupComState();

            // SolidWorks 2021 전용 연결 검사 (ProgID: SldWorks.Application.29 또는 활성 SldWorks.Application의 Revision 29.x / 2021)
            string[] candidateProgIds = { "SldWorks.Application.29", "SldWorks.Application" };

            foreach (var progId in candidateProgIds)
            {
                try
                {
                    var swCandidate = (SldWorks)Marshal.GetActiveObject(progId);
                    if (swCandidate != null)
                    {
                        string rev = swCandidate.RevisionNumber();
                        bool is2021 = progId.Equals("SldWorks.Application.29", StringComparison.OrdinalIgnoreCase) ||
                                      rev.StartsWith("29", StringComparison.OrdinalIgnoreCase) ||
                                      rev.Contains("2021");

                        if (is2021)
                        {
                            _swApp = swCandidate;
                            Log($"SolidWorks 2021 ({rev}) 연결 성공 (ProgId={progId})");
                            return (true, $"SolidWorks 2021 ({rev}) 연결 성공");
                        }
                        else
                        {
                            Log($"감지된 SolidWorks 버전({rev})은 2021이 아니므로 연결 거부");
                            SafeReleaseCom(ref swCandidate);
                        }
                    }
                }
                catch { }
            }

            // COM ROT에 아직 등록되지 않았지만 SLDWORKS.exe 프로세스가 기동 중인지 검사
            bool isProcessRunning = false;
            try
            {
                isProcessRunning = System.Diagnostics.Process.GetProcessesByName("SLDWORKS").Length > 0 ||
                                   System.Diagnostics.Process.GetProcessesByName("sldworks").Length > 0;
            }
            catch { }

            if (isProcessRunning)
            {
                Log("SolidWorks 2021 프로세스 실행 중이나 COM 초기화 대기 중");
                return (false, "SolidWorks 2021이 실행 중이나 초기화(로딩) 중입니다. 잠시 후 자동으로 연결됩니다.");
            }

            Log("SolidWorks 2021 인스턴스를 찾을 수 없습니다.");
            return (false, "실행 중인 SolidWorks 2021을 찾을 수 없습니다. PC에 설치된 SolidWorks 2021 버전이 있는지 확인해주세요.");
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
                info.ErrorMessage = "SolidWorks 2021이 실행 중이지 않거나 설치되어 있지 않습니다. PC에 설치된 SolidWorks 2021 버전을 확인해주세요.";
                return info;
            }

            info.IsConnected = true;

            ModelDoc2? model = null;
            Configuration? conf = null;

            try
            {
                model = (ModelDoc2)_swApp.ActiveDoc;
                if (model == null)
                {
                    info.Title = "문서 없음";
                    info.ErrorMessage = "열려 있는 SolidWorks 어셈블리(.sldasm)가 없습니다.";
                    return info;
                }

                int docType = model.GetType();
                if (docType != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    info.Title = model.GetTitle();
                    info.Path = model.GetPathName();
                    info.ErrorMessage = "활성 문서가 어셈블리(.sldasm)가 아닙니다 (파트 또는 도면).";
                    return info;
                }

                info.Title = model.GetTitle();
                info.Path = model.GetPathName();

                conf = model.GetActiveConfiguration() as Configuration;
                if (conf != null)
                {
                    info.ActiveConfiguration = conf.Name;
                }

                var assy = (AssemblyDoc)model;
                info.TotalComponentsCount = assy.GetComponentCount(false);
            }
            catch (COMException comEx)
            {
                Log($"GetActiveAssemblyInfo COM 예외 (HResult=0x{comEx.ErrorCode:X8}): {comEx.Message}. 재연결 시도 대기");
                info.ErrorMessage = $"SolidWorks 통신 오류: {comEx.Message}";
                HandleComDisconnection();
            }
            catch (Exception ex)
            {
                info.ErrorMessage = $"어셈블리 정보 조회 오류: {ex.Message}";
            }
            finally
            {
                // 로컬 COM 임시 참조 해제 및 가비지 컬렉션 트리거
                SafeReleaseCom(ref conf);
                SafeReleaseCom(ref model);
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

            ClearComponentCache();
            var partMap = new Dictionary<(string Path, string Config), PartAggregateData>();
            var orderedKeys = new List<(string Path, string Config)>();

            ModelDoc2? model = null;

            try
            {
                model = (ModelDoc2)_swApp!.ActiveDoc;
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
                                var nextParent = parentComp.GetParent();
                                SafeReleaseCom(ref parentComp);
                                parentComp = nextParent;
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
                    catch (Exception ex)
                    {
                        Log($"컴포넌트 탐색 개별 예외: {ex.Message}");
                    }
                }

                // BOMItem 목록 구성 및 커스텀 속성 고속 읽기 (컴포넌트당 ModelDoc 1회 오픈)
                var items = new List<BOMItem>();
                int itemNo = 1;

                foreach (var key in orderedKeys)
                {
                    var data = partMap[key];
                    var comp = data.Component;

                    string partName = Path.GetFileNameWithoutExtension(data.FilePath);
                    var propDict = ReadAllCustomProperties(comp);

                    string GetProp(params string[] propKeys)
                    {
                        foreach (var pk in propKeys)
                        {
                            if (propDict.TryGetValue(pk, out var v) && !string.IsNullOrEmpty(v))
                                return v;
                        }
                        return "";
                    }

                    string drawingNo = GetProp("DrawingNo", "도면번호", "DWG_NO", "DwgNo", "Drawing_No");
                    string material = GetProp("Material", "재질");
                    string rev = GetProp("Rev", "Revision");
                    string explanation = GetProp("설명충", "설명", "Explanation", "Description");
                    string remark = GetProp("Remark", "비고");
                    string assyCategory = GetProp("Assy", "AssyCategory", "Assy_Category", "어셈블리구분");
                    string isCommonStr = GetProp("CommonPart", "공용품");
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
                        AssyCategory = assyCategory,
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

                // 가비지 컬렉션 및 COM 참조 메모리 정돈
                TriggerGarbageCollection();

                return (items, null);
            }
            catch (COMException comEx)
            {
                Log($"LoadBom COM 예외 발생 (HResult=0x{comEx.ErrorCode:X8}): {comEx.Message}");
                HandleComDisconnection();
                return (new List<BOMItem>(), $"SolidWorks 통신 오류: {comEx.Message}");
            }
            catch (Exception ex)
            {
                Log($"LoadBom 예외: {ex.Message}");
                return (new List<BOMItem>(), $"BOM 데이터 읽기 실패: {ex.Message}");
            }
            finally
            {
                SafeReleaseCom(ref model);
            }
        }

        public (int SuccessCount, int FailCount, List<string> Errors) ApplyPropertiesToSolidWorks(IEnumerable<BOMItem> items)
        {
            if (_swApp == null)
            {
                var (ok, _) = Connect();
                if (!ok)
                {
                    return (0, items.Count(), new List<string> { "SolidWorks에 연결되어 있지 않습니다." });
                }
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

                ModelDoc2? modelDoc = null;
                ModelDocExtension? ext = null;
                CustomPropertyManager? propMgr = null;

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

                    modelDoc = (ModelDoc2)_swApp!.OpenDoc6(
                        item.FilePath,
                        docType,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                        item.Configuration,
                        ref errorsDoc,
                        ref warningsDoc
                    );

                    if (modelDoc != null)
                    {
                        ext = modelDoc.Extension;
                        propMgr = ext.get_CustomPropertyManager(item.Configuration) ?? ext.get_CustomPropertyManager("");

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
                            SetCustomProperty(propMgr, "Assy", item.AssyCategory);
                            SetCustomProperty(propMgr, "AssyCategory", item.AssyCategory);
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
                        errors.Add($"문서 열기 실패 (에러코드 {errorsDoc}): {item.PartName}");
                    }
                }
                catch (COMException comEx)
                {
                    fail++;
                    errors.Add($"COM 속성 반영 오류 ({item.PartName}): {comEx.Message}");
                    Log($"ApplyProperties COMException ({item.PartName}): {comEx}");
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.Add($"속성 반영 오류 ({item.PartName}): {ex.Message}");
                }
                finally
                {
                    SafeReleaseCom(ref propMgr);
                    SafeReleaseCom(ref ext);
                    SafeReleaseCom(ref modelDoc);
                }
            }

            // 대량 속성 저장 완료 후 GC 실행
            TriggerGarbageCollection();

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
                ModelDoc2? model = null;
                try
                {
                    model = (ModelDoc2)_swApp.ActiveDoc;
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
                catch (COMException comEx)
                {
                    Log($"SetComponentsTransparency COM 예외: {comEx.Message}");
                    HandleComDisconnection();
                }
                catch (Exception ex)
                {
                    Log($"SetComponentsTransparency 예외: {ex.Message}");
                }
                finally
                {
                    SafeReleaseCom(ref model);
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
                ModelDoc2? model = null;
                try
                {
                    model = (ModelDoc2)_swApp.ActiveDoc;
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
                catch (COMException comEx)
                {
                    Log($"ShowAllOpaque COM 예외: {comEx.Message}");
                    HandleComDisconnection();
                }
                catch (Exception ex)
                {
                    Log($"ShowAllOpaque 예외: {ex.Message}");
                }
                finally
                {
                    SafeReleaseCom(ref model);
                }
            }

            return true;
        }

        public (bool Success, string Message) OpenDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return (false, $"지정된 파일을 찾을 수 없습니다: {filePath}");
            }

            if (_swApp == null)
            {
                var (ok, _) = Connect();
                if (!ok)
                {
                    try
                    {
                        var t = Type.GetTypeFromProgID("SldWorks.Application.29");
                        if (t != null)
                        {
                            _swApp = (SldWorks)Activator.CreateInstance(t);
                            if (_swApp != null)
                            {
                                _swApp.Visible = true;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (_swApp == null)
            {
                return (false, "SolidWorks 2021이 설치되어 있지 않거나 실행할 수 없습니다. PC에 설치된 SolidWorks 2021 버전이 있는지 확인해주세요.");
            }

            ModelDoc2? model = null;
            try
            {
                int errors = 0;
                int warnings = 0;
                int docType = filePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocASSEMBLY
                    : (int)swDocumentTypes_e.swDocPART;

                model = (ModelDoc2)_swApp.OpenDoc6(
                    filePath,
                    docType,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings
                );

                if (model != null)
                {
                    _swApp.ActivateDoc2(Path.GetFileName(filePath), false, ref errors);
                    _swApp.Visible = true;
                    return (true, $"SolidWorks에서 '{Path.GetFileName(filePath)}' 문서를 열었습니다.");
                }
                else
                {
                    return (false, $"문서 열기 실패 (에러 코드: {errors}, 경고: {warnings})");
                }
            }
            catch (COMException comEx)
            {
                Log($"OpenDocument COM 예외: {comEx.Message}");
                HandleComDisconnection();
                return (false, $"SolidWorks 통신 오류: {comEx.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"문서 열기 중 예외 발생: {ex.Message}");
            }
            finally
            {
                SafeReleaseCom(ref model);
            }
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
                            var nextParent = parentComp.GetParent();
                            SafeReleaseCom(ref parentComp);
                            parentComp = nextParent;
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

        private static Dictionary<string, string> ReadAllCustomProperties(Component2? comp)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (comp == null) return dict;

            ModelDoc2? model = null;
            ModelDocExtension? ext = null;
            CustomPropertyManager? configPropMgr = null;
            CustomPropertyManager? docPropMgr = null;

            try
            {
                model = comp.GetModelDoc2() as ModelDoc2;
                if (model != null)
                {
                    ext = model.Extension;
                    if (ext != null)
                    {
                        // 1. 파일 수준 일반 속성 읽기
                        docPropMgr = ext.get_CustomPropertyManager("");
                        if (docPropMgr != null)
                        {
                            ReadPropertiesIntoDict(docPropMgr, dict);
                        }

                        // 2. 설정(Configuration)별 속성 읽기 (우선순위 높음)
                        if (!string.IsNullOrEmpty(comp.ReferencedConfiguration))
                        {
                            configPropMgr = ext.get_CustomPropertyManager(comp.ReferencedConfiguration);
                            if (configPropMgr != null)
                            {
                                ReadPropertiesIntoDict(configPropMgr, dict);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                SafeReleaseCom(ref configPropMgr);
                SafeReleaseCom(ref docPropMgr);
                SafeReleaseCom(ref ext);
                SafeReleaseCom(ref model);
            }

            return dict;
        }

        private static void ReadPropertiesIntoDict(CustomPropertyManager propMgr, Dictionary<string, string> dict)
        {
            try
            {
                string[] targetKeys = {
                    "DrawingNo", "도면번호", "DWG_NO", "DwgNo", "Drawing_No",
                    "Material", "재질",
                    "Rev", "Revision",
                    "설명충", "설명", "Explanation", "Description",
                    "Remark", "비고",
                    "CommonPart", "공용품"
                };

                foreach (var key in targetKeys)
                {
                    string valOut = "";
                    string resValOut = "";
                    bool wasResolved = false;
                    bool linkToProp = false;
                    int ret = propMgr.Get6(key, false, out valOut, out resValOut, out wasResolved, out linkToProp);
                    string finalVal = !string.IsNullOrEmpty(resValOut) ? resValOut : valOut;
                    if (!string.IsNullOrEmpty(finalVal))
                    {
                        dict[key] = finalVal;
                    }
                }
            }
            catch { }
        }

        private static void SetCustomProperty(CustomPropertyManager propMgr, string propName, string value)
        {
            try
            {
                propMgr.Add3(propName, (int)swCustomInfoType_e.swCustomInfoText, value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch { }
        }

        private void ClearComponentCache()
        {
            foreach (var rec in _cachedCompRecords)
            {
                var comp = rec.Component;
                SafeReleaseCom(ref comp);
            }
            _cachedCompRecords.Clear();
        }

        private void HandleComDisconnection()
        {
            Log("SolidWorks COM 연결 끊김 감지: 내부 상태 초기화 및 가비지 컬렉션 수행");
            CleanupComState();
        }

        private void CleanupComState()
        {
            ClearComponentCache();
            if (_swApp != null)
            {
                SafeReleaseCom(ref _swApp);
            }
            TriggerGarbageCollection();
        }

        private static void SafeReleaseCom<T>(ref T? comObj) where T : class
        {
            if (comObj == null) return;
            try
            {
                if (Marshal.IsComObject(comObj))
                {
                    Marshal.ReleaseComObject(comObj);
                }
            }
            catch { }
            finally
            {
                comObj = null;
            }
        }

        private static void TriggerGarbageCollection()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        public void Dispose()
        {
            CleanupComState();
            GC.SuppressFinalize(this);
        }

        private class CachedComponentRecord
        {
            public Component2? Component { get; set; }
            public string Path { get; set; } = string.Empty;
            public bool IsSubassembly { get; set; }
            public string Name { get; set; } = string.Empty;
            public string CleanName { get; set; } = string.Empty;
            public int Level { get; set; }
            public List<string> ParentPaths { get; set; } = new();
            public List<string> ParentNames { get; set; } = new();
        }

        private class PartAggregateData
        {
            public int Count { get; set; }
            public Component2? Component { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public string Configuration { get; set; } = string.Empty;
            public bool IsSubassembly { get; set; }
            public int Level { get; set; }
        }
    }
}
