using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BOMManager.Models;

namespace BOMManager.Core
{
    public class MockSwConnector : ISolidWorksService
    {
        public bool IsConnected => true;

        public (bool Success, string Message) Connect()
        {
            return (true, "SolidWorks 2021 가상 연결 (Mock Mode)");
        }

        public AssemblyInfo GetActiveAssemblyInfo()
        {
            return new AssemblyInfo
            {
                Title = "PUMP_UNIT_ASSY.SLDASM",
                Path = @"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM",
                ActiveConfiguration = "Default",
                TotalComponentsCount = 28,
                UniquePartsCount = 10,
                IsConnected = true
            };
        }

        public (List<BOMItem> Items, string? Error) LoadBom(bool topLevelOnly = false, bool includeSuppressed = false)
        {
            var rawData = new (string Name, string Dwg, string Mat, int Qty, string Exp, string Rem, string Path, bool IsSub, int Lvl, string Assy)[]
            {
                ("PUMP_UNIT_ASSY", "PUM-10000A0010001", "", 1, "메인 펌프 유닛 조립체", "메인 펌프 유닛", @"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM", true, 0, "LID Assy"),
                ("BASE_FRAME", "PUM-10100A0010002", "AL60", 1, "하부 베이스 프레임 구조물", "화약도장 (아이보리)", @"C:\CAD_Projects\PumpUnit\BASE_FRAME.SLDPRT", false, 1, "BSS Assy."),
                ("MOTOR_MODULE_ASSY", "PUM-20000A0010003", "", 1, "구동 모터 모듈 서브조립체", "구동 모터 모듈 서브Assy", @"C:\CAD_Projects\PumpUnit\MOTOR_MODULE_ASSY.SLDASM", true, 1, "Device & PCB"),
                ("MOTOR_BRACKET", "PUM-20100A0010004", "AL60", 2, "모터 고정용 브래킷 가공품", "아노다이징 (흑색)", @"C:\CAD_Projects\PumpUnit\MOTOR_BRACKET.SLDPRT", false, 2, "Device & PCB"),
                ("MAIN_SHAFT_D25", "PUM-20200A0010005", "SUS", 1, "메인 구동축 D25 연마품", "고주파 열처리 HRC55", @"C:\CAD_Projects\PumpUnit\MAIN_SHAFT_D25.SLDPRT", false, 2, "BSS Assy."),
                ("IMPELLER_HOUSING", "PUM-30100A0010006", "ULTEM 2300", 1, "펌프 임펠러 밀폐 하우징", "내식 가공", @"C:\CAD_Projects\PumpUnit\IMPELLER_HOUSING.SLDPRT", false, 1, "LID Assy"),
                ("FLANGE_COUPLING", "PUM-40100A0010007", "SUS", 2, "동력 전달 플랜지 커플링", "무전해 니켈도금", @"C:\CAD_Projects\PumpUnit\FLANGE_COUPLING.SLDPRT", false, 0, "BSS Assy."),
                ("SEAL_COVER", "PUM-50100A0010008", "Kapton Film", 4, "누유 방지 오일 씰 커버", "정밀 가공품", @"C:\CAD_Projects\PumpUnit\SEAL_COVER.SLDPRT", false, 0, "Elastomer Assy."),
                ("HEX_BOLT_M8x25", "STD-00825B0010009", "SUS", 12, "육각 볼트 M8 x 25L", "규격품 / 툴박스", @"C:\CAD_Projects\PumpUnit\HEX_BOLT_M8x25.SLDPRT", false, 0, "FRAME bolt"),
                ("SPRING_WASHER_M8", "STD-00008W0010010", "SUS", 12, "스프링 와셔 M8", "규격품", @"C:\CAD_Projects\PumpUnit\SPRING_WASHER_M8.SLDPRT", false, 0, "FRAME bolt"),
            };

            var items = new List<BOMItem>();
            int no = 1;
            foreach (var d in rawData)
            {
                if (topLevelOnly && d.Lvl > 0) continue;
                items.Add(new BOMItem(no++, d.Name, d.Mat, d.Qty, d.Rem, d.Path, d.IsSub, d.Lvl, d.Dwg, d.Exp, d.Assy));
            }

            return (items, null);
        }

        public (int SuccessCount, int FailCount, List<string> Errors) ApplyPropertiesToSolidWorks(IEnumerable<BOMItem> items)
        {
            var list = items.ToList();
            foreach (var item in list)
            {
                item.SnapshotOriginalValues();
            }
            return (list.Count, 0, new List<string>());
        }

        public bool SetComponentsTransparency(IEnumerable<BOMItem> targetItems, IEnumerable<BOMItem> allItems, bool isolateMode = true)
        {
            var targets = new HashSet<BOMItem>(targetItems);
            var allList = allItems.ToList();

            var targetSubs = allList.Where(i => targets.Contains(i) && i.IsSubassembly).ToList();
            foreach (var sub in targetSubs)
            {
                int subIdx = allList.IndexOf(sub);
                if (subIdx >= 0)
                {
                    int subLevel = sub.Level;
                    for (int j = subIdx + 1; j < allList.Count; j++)
                    {
                        if (allList[j].Level > subLevel)
                        {
                            targets.Add(allList[j]);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            foreach (var item in allList)
            {
                item.IsOpaque = targets.Contains(item);
            }
            return true;
        }

        public bool ShowAllOpaque(IEnumerable<BOMItem> allItems)
        {
            foreach (var item in allItems)
            {
                item.IsOpaque = true;
            }
            return true;
        }

        public (bool Success, string Message) OpenDocument(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return (false, "파일 경로가 지정되지 않았습니다.");
            }
            return (true, $"가상 모드: '{Path.GetFileName(filePath)}' 문서를 성공적으로 열었습니다.");
        }

        public (bool Success, int CreatedCount, List<string> Messages) ApplySubAssembliesToFile(IEnumerable<BOMItem> allItems, string? baseDirectory = null)
        {
            var messages = new List<string>();
            int createdCount = 0;
            string baseDir = !string.IsNullOrEmpty(baseDirectory) ? baseDirectory! : @"C:\CAD_Projects\PumpUnit";

            var itemsList = allItems.ToList();
            var subAssies = itemsList.Where(i => i.IsSubassembly && i.ItemNo > 0).ToList();

            foreach (var sub in subAssies)
            {
                string safeName = string.Concat(sub.PartName.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrWhiteSpace(safeName)) safeName = $"SubAssy_{sub.ItemNo}";
                if (!safeName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase))
                {
                    safeName += ".sldasm";
                }
                sub.FileName = safeName;
                try
                {
                    sub.FilePath = Path.Combine(baseDir, safeName);
                }
                catch
                {
                    sub.FilePath = @"C:\Temp\" + safeName;
                }
                sub.IsModified = true;
                createdCount++;

                // 하위 파트 카운트 집계
                int childCount = 0;
                int pIdx = itemsList.IndexOf(sub);
                if (pIdx >= 0)
                {
                    for (int j = pIdx + 1; j < itemsList.Count; j++)
                    {
                        if (itemsList[j].Level > sub.Level) childCount++;
                        else break;
                    }
                }

                messages.Add($"'{sub.PartName}' (하위 부품 {childCount}개) -> '{safeName}' 생성 및 모델트리 적용");
            }

            if (createdCount == 0)
            {
                messages.Add("적용할 Sub-Assy가 없습니다. [➕ Sub-Assy 만들기]로 서브어셈블리를 추가해주세요.");
            }

            return (true, createdCount, messages);
        }

        public (bool Success, int CopiedCount, string TargetAuto3DDir, List<string> Messages) ExportOrganizedAuto3DFiles(IEnumerable<BOMItem> allItems, string? baseDirectory = null)
        {
            var messages = new List<string>();
            int copiedCount = 0;
            string baseDir = !string.IsNullOrEmpty(baseDirectory) ? baseDirectory! : @"C:\CAD_Projects\PumpUnit";
            string auto3DDir = Path.Combine(baseDir, "Auto_3D");

            try
            {
                if (!Directory.Exists(auto3DDir))
                {
                    Directory.CreateDirectory(auto3DDir);
                }
            }
            catch { }

            var itemsList = allItems?.ToList() ?? new List<BOMItem>();
            var parentStack = new List<(int Level, string FolderPath)>
            {
                (0, auto3DDir)
            };

            // Root Assembly Mock File
            string rootFile = Path.Combine(auto3DDir, "PumpUnit_Root.sldasm");
            try
            {
                File.WriteAllText(rootFile, "MOCK ROOT ASSEMBLY");
                copiedCount++;
                messages.Add($"[Root Assy] -> Auto_3D\\PumpUnit_Root.sldasm");
            }
            catch { }

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
                    try { Directory.CreateDirectory(subFolder); } catch { }

                    string fn = safeSubName.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase) ? safeSubName : safeSubName + ".sldasm";
                    string subDest = Path.Combine(subFolder, fn);
                    try
                    {
                        File.WriteAllText(subDest, $"MOCK SUBASSEMBLY: {item.PartName}");
                        copiedCount++;
                        string rel = subDest.Replace(baseDir, "").TrimStart('\\', '/');
                        messages.Add($"[Sub{item.Level} Assy] '{item.PartName}' -> {rel}");
                    }
                    catch { }

                    parentStack.Add((itemLevel, subFolder));
                }
                else
                {
                    string safePartName = string.Concat(item.PartName.Split(Path.GetInvalidFileNameChars())).Trim();
                    string fn = safePartName.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase) ? safePartName : safePartName + ".sldprt";
                    string partDest = Path.Combine(currentParentDir, fn);
                    try
                    {
                        Directory.CreateDirectory(currentParentDir);
                        File.WriteAllText(partDest, $"MOCK PART: {item.PartName}");
                        copiedCount++;
                        string rel = partDest.Replace(baseDir, "").TrimStart('\\', '/');
                        messages.Add($"[Part] '{item.PartName}' -> {rel}");
                    }
                    catch { }
                }
            }

            return (true, copiedCount, auto3DDir, messages);
        }
    }
}
