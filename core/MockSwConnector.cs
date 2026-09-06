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
            var rawData = new (string Name, string Dwg, string Mat, int Qty, string Exp, string Rem, string Path, bool IsSub, int Lvl)[]
            {
                ("PUMP_UNIT_ASSY", "PUM-10000A0010001", "", 1, "메인 펌프 유닛 조립체", "메인 펌프 유닛", @"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM", true, 0),
                ("BASE_FRAME", "PUM-10100A0010002", "SS400", 1, "하부 베이스 프레임 구조물", "화약도장 (아이보리)", @"C:\CAD_Projects\PumpUnit\BASE_FRAME.SLDPRT", false, 1),
                ("MOTOR_MODULE_ASSY", "PUM-20000A0010003", "", 1, "구동 모터 모듈 서브조립체", "구동 모터 모듈 서브Assy", @"C:\CAD_Projects\PumpUnit\MOTOR_MODULE_ASSY.SLDASM", true, 1),
                ("MOTOR_BRACKET", "PUM-20100A0010004", "AL6061-T6", 2, "모터 고정용 브래킷 가공품", "아노다이징 (흑색)", @"C:\CAD_Projects\PumpUnit\MOTOR_BRACKET.SLDPRT", false, 2),
                ("MAIN_SHAFT_D25", "PUM-20200A0010005", "SCM440", 1, "메인 구동축 D25 연마품", "고주파 열처리 HRC55", @"C:\CAD_Projects\PumpUnit\MAIN_SHAFT_D25.SLDPRT", false, 2),
                ("IMPELLER_HOUSING", "PUM-30100A0010006", "SUS304", 1, "펌프 임펠러 밀폐 하우징", "내식 가공", @"C:\CAD_Projects\PumpUnit\IMPELLER_HOUSING.SLDPRT", false, 1),
                ("FLANGE_COUPLING", "PUM-40100A0010007", "S45C", 2, "동력 전달 플랜지 커플링", "무전해 니켈도금", @"C:\CAD_Projects\PumpUnit\FLANGE_COUPLING.SLDPRT", false, 0),
                ("SEAL_COVER", "PUM-50100A0010008", "POM", 4, "누유 방지 오일 씰 커버", "정밀 가공품", @"C:\CAD_Projects\PumpUnit\SEAL_COVER.SLDPRT", false, 0),
                ("HEX_BOLT_M8x25", "STD-00825B0010009", "SUS304", 12, "육각 볼트 M8 x 25L", "규격품 / 툴박스", @"C:\CAD_Projects\PumpUnit\HEX_BOLT_M8x25.SLDPRT", false, 0),
                ("SPRING_WASHER_M8", "STD-00008W0010010", "SPRING STEEL", 12, "스프링 와셔 M8", "규격품", @"C:\CAD_Projects\PumpUnit\SPRING_WASHER_M8.SLDPRT", false, 0),
            };

            var items = new List<BOMItem>();
            int no = 1;
            foreach (var d in rawData)
            {
                if (topLevelOnly && d.Lvl > 0) continue;
                items.Add(new BOMItem(no++, d.Name, d.Mat, d.Qty, d.Rem, d.Path, d.IsSub, d.Lvl, d.Dwg, d.Exp));
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
            foreach (var item in allItems)
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
    }
}
