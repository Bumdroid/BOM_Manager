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
        private static string LogFile
        {
            get
            {
                try
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addin_debug.log");
                }
                catch
                {
                    return Path.Combine(Path.GetTempPath(), "addin_debug.log");
                }
            }
        }

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

                        string normPath = SafeNormalizePath(filePath);
                        bool isSub = normPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) ||
                                     filePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
                        if (!isSub)
                        {
                            try
                            {
                                var mDoc = comp.GetModelDoc2() as ModelDoc2;
                                if (mDoc != null && mDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                                {
                                    isSub = true;
                                }
                                else
                                {
                                    object[]? childObjs = (object[])comp.GetChildren();
                                    if (childObjs != null && childObjs.Length > 0)
                                    {
                                        isSub = true;
                                    }
                                }
                            }
                            catch { }
                        }
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
                                    parentPaths.Add(SafeNormalizePath(pPath));
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

                    string partName = ExtractCleanPartName(data.FilePath, comp != null ? comp.Name2 : null, (comp?.GetModelDoc2() as ModelDoc2)?.GetTitle());
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
                        IsExpanded = (data.Level == 0)
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
                    string norm = SafeNormalizePath(item.FilePath);
                    targetPaths.Add(norm);
                    string fn = SafeGetFileName(norm);
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
                string itemPath = SafeNormalizePath(item.FilePath);
                string itemPart = item.PartName.Trim().ToLowerInvariant();
                string itemFname = SafeGetFileName(itemPath);
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
                            string cP = SafeNormalizePath(cItem.FilePath);
                            targetPaths.Add(cP);
                            targetFnames.Add(SafeGetFileName(cP));
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
                string itemPath = SafeNormalizePath(item.FilePath);
                string itemPart = item.PartName.Trim().ToLowerInvariant();
                string itemFname = SafeGetFileName(itemPath);
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

        public (bool Success, int CreatedCount, List<string> Messages) ApplySubAssembliesToFile(IEnumerable<BOMItem> allItems, string? baseDirectory = null)
        {
            var messages = new List<string>();
            int createdCount = 0;

            var itemsList = allItems?.ToList() ?? new List<BOMItem>();
            var subAssies = itemsList.Where(i => i.IsSubassembly && i.ItemNo > 0).ToList();

            if (subAssies.Count == 0)
            {
                return (false, 0, new List<string> { "적용할 Sub-Assy가 정의되어 있지 않습니다. 먼저 [➕ Sub-Assy 만들기]로 서브어셈블리를 추가해주세요." });
            }

            if (_swApp == null)
            {
                Connect();
            }

            if (_swApp == null)
            {
                return (false, 0, new List<string> { "SolidWorks 2021에 연결되어 있지 않습니다. SolidWorks 실행 및 모델을 확인해주세요." });
            }

            ModelDoc2? model = null;
            ModelView? modelView = null;
            bool wasFeatureTreeEnabled = true;
            bool wasGraphicsUpdateEnabled = true;
            bool lockedSw = false;

            try
            {
                model = (ModelDoc2)_swApp.ActiveDoc;
                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    return (false, 0, new List<string> { "활성화된 SolidWorks 어셈블리(.sldasm) 문서가 없습니다." });
                }

                var assy = (AssemblyDoc)model;
                string modelPath = model.GetPathName();
                string activeDir = SafeGetDirectoryName(modelPath);
                if (string.IsNullOrWhiteSpace(activeDir))
                {
                    activeDir = SafeGetDirectoryName(baseDirectory);
                }
                if (string.IsNullOrWhiteSpace(activeDir))
                {
                    activeDir = @"C:\Temp";
                }

                try
                {
                    if (!Directory.Exists(activeDir))
                    {
                        Directory.CreateDirectory(activeDir);
                    }
                }
                catch { }

                // 1. SolidWorks 화면 갱신 및 피처 트리 리렌더링 중단 & 외부 조작 방지 잠금 (초고속 배치 모드)
                try
                {
                    _swApp.CommandInProgress = true;
                    _swApp.UserControl = false;
                    lockedSw = true;
                }
                catch { }

                try
                {
                    var featMgr = model.FeatureManager;
                    if (featMgr != null)
                    {
                        wasFeatureTreeEnabled = featMgr.EnableFeatureTree;
                        featMgr.EnableFeatureTree = false;
                        featMgr.EnableFeatureTreeWindow = false;
                    }
                }
                catch { }

                try
                {
                    modelView = (ModelView?)model.ActiveView;
                    if (modelView != null)
                    {
                        wasGraphicsUpdateEnabled = modelView.EnableGraphicsUpdate;
                        modelView.EnableGraphicsUpdate = false;
                    }
                }
                catch { }

                // 2. SolidWorks 저장 팝업 대화상자 차단 (가상 서브어셈블리로 조용히 생성 후 무음 저장)
                try
                {
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSaveNewComponentsToExternalFile, false);
                }
                catch { }

                EnsureCachedCompRecords(model);

                // 3. 각 Sub-Assy별로 계층 위치에 맞게 생성/구성 (Level 1 -> Level 2 순차 처리)
                foreach (var sub in subAssies)
                {
                    string safeName = string.Concat(sub.PartName.Split(Path.GetInvalidFileNameChars())).Trim();
                    if (string.IsNullOrWhiteSpace(safeName))
                    {
                        safeName = $"SubAssy_{sub.ItemNo}";
                    }
                    if (!safeName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                    {
                        safeName += ".sldasm";
                    }

                    string subFilePath = SafeCombine(activeDir, safeName);
                    sub.FileName = safeName;
                    sub.FilePath = subFilePath;

                    // 하위 부품 목록 수집
                    int pIdx = itemsList.IndexOf(sub);
                    var childParts = new List<BOMItem>();
                    if (pIdx >= 0)
                    {
                        for (int j = pIdx + 1; j < itemsList.Count; j++)
                        {
                            if (itemsList[j].Level > sub.Level)
                            {
                                childParts.Add(itemsList[j]);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (childParts.Count == 0)
                    {
                        messages.Add($"⚠️ '{sub.PartName}': 포함된 하위 부품이 없어 건너뛰었습니다.");
                        continue;
                    }

                    // 부모 Sub-Assy 식별 (Root가 아닌 상위 서브어셈블리 하위인 경우)
                    BOMItem? parentItem = null;
                    if (sub.Level > 1 && pIdx > 0)
                    {
                        for (int k = pIdx - 1; k >= 0; k--)
                        {
                            if (itemsList[k].Level < sub.Level && itemsList[k].IsSubassembly)
                            {
                                parentItem = itemsList[k];
                                break;
                            }
                        }
                    }

                    Component2? parentComp = null;
                    if (parentItem != null)
                    {
                        string pClean = (parentItem.PartName ?? "").Trim().ToLowerInvariant();
                        string pNorm = SafeNormalizePath(parentItem.FilePath);
                        foreach (var rec in _cachedCompRecords)
                        {
                            if (rec.Component == null) continue;
                            if (!string.IsNullOrEmpty(pNorm) && SafeNormalizePath(rec.Path) == pNorm)
                            {
                                parentComp = rec.Component;
                                break;
                            }
                            if (rec.CleanName.Equals(pClean, StringComparison.OrdinalIgnoreCase) ||
                                rec.Name.Equals(pClean, StringComparison.OrdinalIgnoreCase))
                            {
                                parentComp = rec.Component;
                                break;
                            }
                        }
                    }

                    // 상위 어셈블리 내 편집 모드 진입 (부모 Sub-Assy가 있는 경우)
                    bool enteredInContext = false;
                    if (parentComp != null)
                    {
                        try
                        {
                            model.ClearSelection2(true);
                            bool selP = parentComp.Select4(false, null, false);
                            if (selP)
                            {
                                assy.EditAssembly();
                                enteredInContext = true;
                            }
                        }
                        catch { }
                    }

                    // 해당 하위 부품들을 SolidWorks에서 다중 선택
                    try { model.ClearSelection2(true); } catch { }

                    var childPaths = new HashSet<string>(
                        childParts
                            .Where(c => !string.IsNullOrWhiteSpace(c.FilePath))
                            .Select(c => SafeNormalizePath(c.FilePath))
                            .Where(p => !string.IsNullOrEmpty(p))
                    );
                    var childNames = new HashSet<string>(
                        childParts
                            .Where(c => !string.IsNullOrWhiteSpace(c.PartName))
                            .Select(c => c.PartName.Trim()),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var childFnames = new HashSet<string>(
                        childParts
                            .Where(c => !string.IsNullOrWhiteSpace(c.FileName))
                            .Select(c => SafeGetFileName(c.FileName).ToLowerInvariant()),
                        StringComparer.OrdinalIgnoreCase
                    );

                    int selectedCompCount = 0;
                    Component2? firstSelComp = null;
                    foreach (var rec in _cachedCompRecords)
                    {
                        var comp = rec.Component;
                        if (comp == null) continue;

                        string recPath = SafeNormalizePath(rec.Path);
                        string recName = rec.CleanName;
                        string recFname = SafeGetFileName(rec.Path).ToLowerInvariant();

                        bool matches = false;
                        if (!string.IsNullOrEmpty(recPath) && childPaths.Contains(recPath)) matches = true;
                        else if (!string.IsNullOrEmpty(recFname) && childFnames.Contains(recFname)) matches = true;
                        else if (!string.IsNullOrEmpty(recName) && childNames.Contains(recName)) matches = true;
                        else if (!string.IsNullOrEmpty(rec.Name) && childNames.Contains(rec.Name)) matches = true;

                        if (matches)
                        {
                            try
                            {
                                bool selOk = comp.Select4(true, null, false);
                                if (selOk)
                                {
                                    if (firstSelComp == null) firstSelComp = comp;
                                    selectedCompCount++;
                                }
                                else
                                {
                                    if (model.Extension.SelectByID2(comp.Name2, "COMPONENT", 0, 0, 0, true, 0, null, 0))
                                    {
                                        if (firstSelComp == null) firstSelComp = comp;
                                        selectedCompCount++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (selectedCompCount == 0)
                    {
                        if (enteredInContext)
                        {
                            try { model.ClearSelection2(true); assy.EditAssembly(); } catch { }
                        }
                        messages.Add($"⚠️ '{sub.PartName}': SolidWorks 모델트리에서 일치하는 활성 부품을 찾지 못해 건너뛰었습니다.");
                        continue;
                    }

                    // 생성 직전 컴포넌트 목록 캡처 (상위 어셈블리가 아닌 새로 추가되는 서브어셈블리 컴포넌트만 정확히 특정)
                    var existingCompNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        object[]? curComps = (object[])assy.GetComponents(false);
                        if (curComps != null)
                        {
                            foreach (var oc in curComps)
                            {
                                if (oc is Component2 c && !string.IsNullOrEmpty(c.Name2))
                                {
                                    existingCompNames.Add(c.Name2);
                                }
                            }
                        }
                    }
                    catch { }

                    // 서브어셈블리 생성 시도 (MakeAssemblyFromSelectedComponents 및 RunCommand Fallback)
                    bool formed = false;
                    try
                    {
                        formed = assy.MakeAssemblyFromSelectedComponents(subFilePath);
                    }
                    catch (Exception ex)
                    {
                        Log($"MakeAssemblyFromSelectedComponents 오류: {ex.Message}");
                    }

                    if (!formed)
                    {
                        try
                        {
                            formed = _swApp.RunCommand(1259, ""); // Form New Subassembly
                        }
                        catch { }
                    }

                    // 새로 생성된 Sub-Assy 컴포넌트만 정확히 탐색 및 모델트리 이름 변경(Rename)
                    if (formed)
                    {
                        try
                        {
                            Component2? newSubComp = null;

                            // 1순위: 생성 후 새로 추가된 컴포넌트 탐색 (부모 어셈블리가 아닌 신규 생성된 어셈블리 컴포넌트)
                            try
                            {
                                object[]? afterComps = (object[])assy.GetComponents(false);
                                if (afterComps != null)
                                {
                                    foreach (var oc in afterComps)
                                    {
                                        if (oc is Component2 c && !string.IsNullOrEmpty(c.Name2))
                                        {
                                            if (!existingCompNames.Contains(c.Name2))
                                            {
                                                newSubComp = c;
                                                Log($"신규 생성된 서브어셈블리 컴포넌트 발견: {c.Name2}");
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }

                            string targetName = sub.PartName.Trim();
                            if (targetName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                            {
                                targetName = targetName.Substring(0, targetName.Length - 7).Trim();
                            }
                            if (string.IsNullOrWhiteSpace(targetName))
                            {
                                targetName = $"SubAssy_{sub.ItemNo}";
                            }

                            if (newSubComp != null)
                            {
                                // 1. SolidWorks 공식 SelectedFeatureProperties를 통한 피처/컴포넌트 이름 변경
                                try
                                {
                                    model.ClearSelection2(true);
                                    bool sel = model.Extension.SelectByID2(newSubComp.Name2, "COMPONENT", 0, 0, 0, false, 0, null, 0);
                                    if (sel)
                                    {
                                        model.SelectedFeatureProperties(0, 0, 0, 0, 0, 0, 0, true, false, targetName);
                                        model.ClearSelection2(true);
                                        Log($"SelectedFeatureProperties 이름 변경 완료: {targetName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"SelectedFeatureProperties 변경 예외: {ex.Message}");
                                }

                                // 2. Component2.Name2 이름 변경 시도
                                try
                                {
                                    newSubComp.Name2 = targetName;
                                    Log($"newSubComp.Name2 변경 완료: {newSubComp.Name2}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"newSubComp.Name2 변경 예외: {ex.Message}");
                                }

                                // 3. FeatureByName을 통한 피처 트리 이름 변경 시도
                                try
                                {
                                    var feat = (Feature?)assy.FeatureByName(newSubComp.Name2);
                                    if (feat != null)
                                    {
                                        feat.Name = targetName;
                                        Log($"Feature Name 변경 완료: {feat.Name}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"Feature Name 변경 예외: {ex.Message}");
                                }

                                // 4. SelectByID2를 통한 선택 후 SelectionManager 피처 이름 변경 재시도
                                try
                                {
                                    if (model.Extension.SelectByID2(newSubComp.Name2, "COMPONENT", 0, 0, 0, false, 0, null, 0))
                                    {
                                        var selMgr = (SelectionMgr)model.SelectionManager;
                                        if (selMgr != null)
                                        {
                                            var selFeat = (Feature?)selMgr.GetSelectedObject6(1, -1);
                                            if (selFeat != null)
                                            {
                                                selFeat.Name = targetName;
                                            }
                                        }
                                        model.ClearSelection2(true);
                                    }
                                }
                                catch { }

                                sub.IsVirtual = true;
                                sub.PartName = targetName;
                                sub.AssyCategory = targetName;

                                try
                                {
                                    string actualPath = newSubComp.GetPathName();
                                    if (!string.IsNullOrEmpty(actualPath))
                                    {
                                        sub.FilePath = actualPath;
                                        sub.FileName = SafeGetFileName(actualPath);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"신규 서브어셈블리 이름 변경 후처리 예외: {ex.Message}");
                        }
                    }

                    // 인컨텍스트 편집 모드 종료
                    if (enteredInContext)
                    {
                        try
                        {
                            model.ClearSelection2(true);
                            assy.EditAssembly();
                        }
                        catch { }
                    }

                    if (formed)
                    {
                        createdCount++;
                        sub.IsModified = true;
                        string parentDesc = parentItem != null ? $"'{parentItem.PartName}' 하위에 " : "";
                        messages.Add($"✅ {parentDesc}'{sub.PartName}' (선택 부품 {selectedCompCount}개) -> '{safeName}' 생성 및 모델트리 적용 완료");

                        // 다음 레벨 Sub-Assy 생성을 위해 캐시 즉시 갱신
                        _cachedCompRecords.Clear();
                        EnsureCachedCompRecords(model);
                    }
                    else
                    {
                        messages.Add($"❌ '{sub.PartName}' (선택 부품 {selectedCompCount}개): 서브어셈블리 생성 실패");
                    }
                }

                // 4. 전체 모델 리빌드 및 가상 부품/수정사항 무음(Silent) 저장
                try
                {
                    model.ClearSelection2(true);
                    model.EditRebuild3();

                    int swErrors = 0;
                    int swWarnings = 0;
                    model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref swErrors, ref swWarnings);
                }
                catch { }

                _cachedCompRecords.Clear();

                // 실제 수정된 항목(IsModified)만 속성 저장 반영
                try
                {
                    var modifiedItems = itemsList.Where(i => i.IsModified).ToList();
                    if (modifiedItems.Count > 0)
                    {
                        ApplyPropertiesToSolidWorks(modifiedItems);
                    }
                }
                catch { }

                return (createdCount > 0, createdCount, messages);
            }
            catch (COMException comEx)
            {
                Log($"ApplySubAssembliesToFile COM 예외: {comEx.Message}");
                HandleComDisconnection();
                return (false, createdCount, new List<string> { $"SolidWorks 통신 오류: {comEx.Message}" });
            }
            catch (Exception ex)
            {
                Log($"ApplySubAssembliesToFile 예외: {ex.Message}");
                return (false, createdCount, new List<string> { $"서브어셈블리 적용 중 오류: {ex.Message}" });
            }
            finally
            {
                if (model != null)
                {
                    try
                    {
                        var featMgr = model.FeatureManager;
                        if (featMgr != null)
                        {
                            featMgr.EnableFeatureTreeWindow = true;
                            featMgr.EnableFeatureTree = wasFeatureTreeEnabled;
                            featMgr.UpdateFeatureTree();
                        }
                    }
                    catch { }

                    try
                    {
                        if (modelView != null)
                        {
                            modelView.EnableGraphicsUpdate = wasGraphicsUpdateEnabled;
                        }
                        model.GraphicsRedraw2();
                    }
                    catch { }
                }

                if (lockedSw && _swApp != null)
                {
                    try
                    {
                        _swApp.CommandInProgress = false;
                        _swApp.UserControl = true;
                    }
                    catch { }
                }

                SafeReleaseCom(ref modelView);
                SafeReleaseCom(ref model);
            }
        }

        public (bool Success, int CopiedCount, string TargetAuto3DDir, List<string> Messages) ExportOrganizedAuto3DFiles(IEnumerable<BOMItem> allItems, string? baseDirectory = null)
        {
            var messages = new List<string>();
            int copiedCount = 0;

            if (_swApp == null)
            {
                Connect();
            }

            ModelDoc2? model = null;
            string baseDir = baseDirectory ?? string.Empty;
            bool lockedSw = false;

            try
            {
                if (_swApp != null)
                {
                    try
                    {
                        _swApp.CommandInProgress = true;
                        lockedSw = true;
                    }
                    catch { }

                    try
                    {
                        model = (ModelDoc2)_swApp.ActiveDoc;
                        if (model != null && string.IsNullOrWhiteSpace(baseDir))
                        {
                            string p = model.GetPathName();
                            baseDir = SafeGetDirectoryName(p);
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    baseDir = @"C:\Temp";
                }

                string auto3DDir = Path.Combine(baseDir, "Auto_3D");
                if (!Directory.Exists(auto3DDir))
                {
                    Directory.CreateDirectory(auto3DDir);
                }

                var itemsList = allItems?.ToList() ?? new List<BOMItem>();
                var parentStack = new List<(int Level, string FolderPath)>
                {
                    (0, auto3DDir)
                };

                // [성능 최적화] 파일 검색을 1회만 인덱싱하여 반복적인 디스크 전체 탐색 방지 (O(1) 매핑)
                var fileLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(baseDir))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories))
                        {
                            string fname = Path.GetFileName(f);
                            if (!fileLookup.ContainsKey(fname))
                            {
                                fileLookup[fname] = f;
                            }
                        }
                    }
                    catch { }
                }

                // 1. Root Assembly 파일 복사
                string rootModelPath = model != null ? (model.GetPathName() ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(rootModelPath) && File.Exists(rootModelPath))
                {
                    try
                    {
                        string rootDest = Path.Combine(auto3DDir, Path.GetFileName(rootModelPath));
                        File.Copy(rootModelPath, rootDest, overwrite: true);
                        copiedCount++;
                        messages.Add($"📦 [Root Assy] -> Auto_3D\\{Path.GetFileName(rootModelPath)}");
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"⚠️ Root 어셈블리 복사 예외: {ex.Message}");
                    }
                }
                else
                {
                    string title = model != null ? (model.GetTitle() ?? "Root_Assembly") : "Root_Assembly";
                    if (!title.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)) title += ".sldasm";
                    string rootDest = Path.Combine(auto3DDir, title);
                    if (!string.IsNullOrWhiteSpace(rootModelPath) && File.Exists(rootModelPath))
                    {
                        File.Copy(rootModelPath, rootDest, overwrite: true);
                        copiedCount++;
                        messages.Add($"📦 [Root Assy] -> Auto_3D\\{title}");
                    }
                }

                // 2. 계층별 하위 Sub-Assy 및 파트 파일 복사
                foreach (var item in itemsList)
                {
                    int itemLevel = item.Level;
                    while (parentStack.Count > 1 && parentStack.Last().Level >= itemLevel)
                    {
                        parentStack.RemoveAt(parentStack.Count - 1);
                    }
                    string currentParentDir = parentStack.Last().FolderPath;

                    if (item.IsSubassembly)
                    {
                        string safeSubName = string.Concat(item.PartName.Split(Path.GetInvalidFileNameChars())).Trim();
                        if (string.IsNullOrWhiteSpace(safeSubName)) safeSubName = $"SubAssy_{item.ItemNo}";
                        string subFolder = Path.Combine(currentParentDir, safeSubName);
                        if (!Directory.Exists(subFolder))
                        {
                            Directory.CreateDirectory(subFolder);
                        }

                        string fn = safeSubName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) ? safeSubName : safeSubName + ".sldasm";
                        string subDest = Path.Combine(subFolder, fn);

                        string subSrc = item.FilePath;
                        bool copied = false;
                        if (!string.IsNullOrWhiteSpace(subSrc) && File.Exists(subSrc))
                        {
                            try
                            {
                                File.Copy(subSrc, subDest, overwrite: true);
                                copied = true;
                            }
                            catch { }
                        }

                        if (!copied && fileLookup.TryGetValue(fn, out var foundSub) && File.Exists(foundSub))
                        {
                            try
                            {
                                File.Copy(foundSub, subDest, overwrite: true);
                                copied = true;
                            }
                            catch { }
                        }

                        copiedCount++;
                        string rel = subDest.Replace(baseDir, "").TrimStart('\\', '/');
                        messages.Add($"🧩 [Sub{item.Level} Assy] '{item.PartName}' -> {rel}" + (copied ? "" : " (파일 생성 대기)"));

                        parentStack.Add((itemLevel, subFolder));
                    }
                    else
                    {
                        // 파트 파일 (.sldprt)
                        string safePartName = string.Concat(item.PartName.Split(Path.GetInvalidFileNameChars())).Trim();
                        string fn = safePartName.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase) ? safePartName : safePartName + ".sldprt";
                        string partDest = Path.Combine(currentParentDir, fn);

                        if (!Directory.Exists(currentParentDir))
                        {
                            Directory.CreateDirectory(currentParentDir);
                        }

                        string partSrc = item.FilePath;
                        bool copied = false;
                        if (!string.IsNullOrWhiteSpace(partSrc) && File.Exists(partSrc))
                        {
                            try
                            {
                                File.Copy(partSrc, partDest, overwrite: true);
                                copied = true;
                            }
                            catch { }
                        }

                        if (!copied && fileLookup.TryGetValue(fn, out var foundPart) && File.Exists(foundPart))
                        {
                            try
                            {
                                File.Copy(foundPart, partDest, overwrite: true);
                                copied = true;
                            }
                            catch { }
                        }

                        copiedCount++;
                        string rel = partDest.Replace(baseDir, "").TrimStart('\\', '/');
                        messages.Add($"⚙️ [Part] '{item.PartName}' -> {rel}" + (copied ? "" : " (경로 참조)"));
                    }
                }

                return (true, copiedCount, auto3DDir, messages);
            }
            catch (Exception ex)
            {
                Log($"ExportOrganizedAuto3DFiles 예외: {ex.Message}");
                return (false, copiedCount, Path.Combine(baseDir, "Auto_3D"), new List<string> { $"Auto_3D 정리 사본 저장 실패: {ex.Message}" });
            }
            finally
            {
                if (lockedSw && _swApp != null)
                {
                    try
                    {
                        _swApp.CommandInProgress = false;
                    }
                    catch { }
                }
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

                    string normPath = SafeNormalizePath(filePath);
                    bool isSub = normPath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) ||
                                 filePath.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
                    if (!isSub)
                    {
                        try
                        {
                            var mDoc = comp.GetModelDoc2() as ModelDoc2;
                            if (mDoc != null && mDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                            {
                                isSub = true;
                            }
                            else
                            {
                                object[]? childObjs = (object[])comp.GetChildren();
                                if (childObjs != null && childObjs.Length > 0)
                                {
                                    isSub = true;
                                }
                            }
                        }
                        catch { }
                    }
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
                                parentPaths.Add(SafeNormalizePath(pPath));
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

        private static string SafeNormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                string trimmed = path!.Trim().Trim('"', '\'');
                if (Path.IsPathRooted(trimmed))
                {
                    return Path.GetFullPath(trimmed).ToLowerInvariant();
                }
                return trimmed.ToLowerInvariant();
            }
            catch
            {
                return (path ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        private static string SafeGetDirectoryName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                string trimmed = path!.Trim().Trim('"', '\'');
                return Path.GetDirectoryName(trimmed) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeGetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                string trimmed = path!.Trim().Trim('"', '\'');
                return Path.GetFileName(trimmed) ?? string.Empty;
            }
            catch
            {
                if (path == null) return string.Empty;
                int lastSlash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                if (lastSlash >= 0 && lastSlash < path.Length - 1)
                {
                    return path.Substring(lastSlash + 1);
                }
                return path;
            }
        }

        private static string SafeCombine(string directory, string filename)
        {
            try
            {
                string safeDir = string.IsNullOrWhiteSpace(directory) ? @"C:\Temp" : directory.Trim().Trim('"', '\'');
                string safeFile = string.Concat(filename.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrWhiteSpace(safeFile)) safeFile = "SubAssembly.sldasm";
                return Path.Combine(safeDir, safeFile);
            }
            catch
            {
                return @"C:\Temp\" + filename;
            }
        }

        public static string ExtractCleanPartName(string? filePath, string? compName, string? modelTitle = null)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(modelTitle)) candidates.Add(modelTitle!);
            if (!string.IsNullOrWhiteSpace(compName)) candidates.Add(compName!);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                string fn = SafeGetFileName(filePath);
                if (!string.IsNullOrWhiteSpace(fn)) candidates.Add(fn);
            }

            foreach (var raw in candidates)
            {
                string cleaned = CleanSingleName(raw);
                if (!string.IsNullOrWhiteSpace(cleaned) &&
                    !cleaned.StartsWith("(", StringComparison.Ordinal) &&
                    !cleaned.EndsWith(")", StringComparison.Ordinal) &&
                    !cleaned.Contains('^') &&
                    !cleaned.Contains('/') &&
                    !cleaned.Contains('\\'))
                {
                    return cleaned;
                }
            }

            foreach (var raw in candidates)
            {
                string cleaned = CleanSingleName(raw);
                if (!string.IsNullOrWhiteSpace(cleaned) && !cleaned.Contains('^'))
                {
                    return cleaned;
                }
            }

            if (!string.IsNullOrWhiteSpace(filePath)) return Path.GetFileNameWithoutExtension(filePath) ?? "UNKNOWN";
            if (!string.IsNullOrWhiteSpace(compName)) return compName!;
            return "UNKNOWN";
        }

        public static string CleanSingleName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw!.Trim().Trim('"', '\'');

            // 1. 파일 확장자 제거
            if (s.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase))
            {
                s = Path.GetFileNameWithoutExtension(s);
            }

            // 2. 경로 구분자 (/, \) -> 마지막 세그먼트
            if (s.Contains('/')) s = s.Split('/').Last();
            if (s.Contains('\\')) s = s.Split('\\').Last();

            // 3. 계층 접미사 (@Parent) 제거
            if (s.Contains('@')) s = s.Split('@')[0];

            // 4. 가상 컴포넌트 대괄호 및 부모 어셈블리 이름 분리 ([Child^Parent])
            int openB = s.IndexOf('[');
            int closeB = s.LastIndexOf(']');
            if (openB >= 0)
            {
                if (closeB > openB)
                {
                    s = s.Substring(openB + 1, closeB - openB - 1);
                }
                else
                {
                    s = s.Substring(openB + 1);
                }
            }

            // 5. 캐럿(^) 기준 앞부분 (자식 컴포넌트 이름)
            if (s.Contains('^'))
            {
                s = s.Split('^')[0];
            }

            s = s.Trim('[', ']', ' ');

            // 6. 컴포넌트 인스턴스 번호 제거 (예: Name-1, Name-2, Name<1> 등)
            int lastHyphen = s.LastIndexOf('-');
            if (lastHyphen > 0 && lastHyphen < s.Length - 1)
            {
                string suffix = s.Substring(lastHyphen + 1).Trim();
                if (int.TryParse(suffix, out _))
                {
                    s = s.Substring(0, lastHyphen).Trim();
                }
            }

            int lastAngle = s.LastIndexOf('<');
            if (lastAngle >= 0 && s.EndsWith(">"))
            {
                s = s.Substring(0, lastAngle).Trim();
            }

            return s.Trim();
        }

        private static string ExtractLeafName(string compName)
        {
            return CleanSingleName(compName).ToLowerInvariant();
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
