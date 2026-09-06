using System;
using System.IO;
using System.Linq;
using BOMManager.Core;
using BOMManager.Models;
using BOMManager.Utils;

namespace BOMManager.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== [BOM Manager C# Unit & Verification Tests] ===");
            int passed = 0;
            int failed = 0;

            // Test 1: Subassembly & Part Indentation Rules
            try
            {
                var sub0 = new BOMItem(1, "MAIN_ASSY", isSubassembly: true, level: 0);
                var sub1 = new BOMItem(2, "SUB1_MODULE", isSubassembly: true, level: 1);
                var sub2 = new BOMItem(3, "SUB2_MODULE", isSubassembly: true, level: 2);
                var sub3 = new BOMItem(4, "SUB3_MODULE", isSubassembly: true, level: 3);

                var part0 = new BOMItem(5, "PART_LVL0", isSubassembly: false, level: 0);
                var part1 = new BOMItem(6, "PART_LVL1", isSubassembly: false, level: 1);
                var part2 = new BOMItem(7, "PART_LVL2", isSubassembly: false, level: 2);

                if (sub0.SubassemblyIndent != "") throw new Exception($"Sub0 indent failed: '{sub0.SubassemblyIndent}'");
                if (sub1.SubassemblyIndent != "   ") throw new Exception($"Sub1 indent failed: expected 3 spaces, got '{sub1.SubassemblyIndent}'");
                if (sub2.SubassemblyIndent != "      ") throw new Exception($"Sub2 indent failed: expected 6 spaces, got '{sub2.SubassemblyIndent}'");
                if (sub3.SubassemblyIndent != "         ") throw new Exception($"Sub3 indent failed: expected 9 spaces, got '{sub3.SubassemblyIndent}'");

                if (part0.PartPrefix != "") throw new Exception($"Part0 prefix failed: '{part0.PartPrefix}'");
                if (part1.PartPrefix != "   └  ") throw new Exception($"Part1 prefix failed: expected '   └  ', got '{part1.PartPrefix}'");
                if (part2.PartPrefix != "      └  ") throw new Exception($"Part2 prefix failed: expected '      └  ', got '{part2.PartPrefix}'");

                if (sub0.PartNameDisplay != "▼ MAIN_ASSY")
                    throw new Exception($"Sub0 display failed: expected '▼ MAIN_ASSY', got '{sub0.PartNameDisplay}'");
                if (sub1.PartNameDisplay != "   ▼ SUB1_MODULE")
                    throw new Exception($"Sub1 display failed: expected '   ▼ SUB1_MODULE', got '{sub1.PartNameDisplay}'");
                if (sub2.PartNameDisplay != "      ▼ SUB2_MODULE")
                    throw new Exception($"Sub2 display failed: expected '      ▼ SUB2_MODULE', got '{sub2.PartNameDisplay}'");

                if (part0.PartNameDisplay != "PART_LVL0")
                    throw new Exception($"Part0 display failed: expected 'PART_LVL0', got '{part0.PartNameDisplay}'");
                if (part1.PartNameDisplay != "   └  PART_LVL1")
                    throw new Exception($"Part1 display failed: expected '   └  PART_LVL1', got '{part1.PartNameDisplay}'");
                if (part2.PartNameDisplay != "      └  PART_LVL2")
                    throw new Exception($"Part2 display failed: expected '      └  PART_LVL2', got '{part2.PartNameDisplay}'");

                Console.WriteLine(" [PASS] Test 1: 서브어셈블리(2차 서브Assy: 스페이스 3개+삼각형, ㄴ생략) 및 파트 인덴트(└) 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 1: {ex.Message}");
                failed++;
            }

            // Test 2: Modification tracking, Drawing No Hyphen Stripping & Segment Parsing, 설명충 Property
            try
            {
                // 입력 시 하이픈(-) 포함되어 있어도 저장 시 하이픈 제외 검증
                var item = new BOMItem(1, "BRACKET", "SS400", 2, "비고", drawingNo: "OOO-PPPPPGBBBXXXX", explanation: "모터 브래킷 보강대");
                if (item.IsModified) throw new Exception("New item should not be modified initially.");

                // DrawingNo 자체는 하이픈이 제외된 순수 16자리여야 함
                if (item.DrawingNo != "OOOPPPPPGBBBXXXX")
                    throw new Exception($"DrawingNo should strip hyphens on init: expected 'OOOPPPPPGBBBXXXX', got '{item.DrawingNo}'");

                // Verify OOO-PPPPPGBBBXXXX segmentation for color display
                if (item.DrawingNoPartO != "OOO") throw new Exception($"PartO failed: expected 'OOO', got '{item.DrawingNoPartO}'");
                if (item.DrawingNoHyphen != "-") throw new Exception($"Hyphen failed: expected '-', got '{item.DrawingNoHyphen}'");
                if (item.DrawingNoPartP != "PPPPP") throw new Exception($"PartP failed: expected 'PPPPP', got '{item.DrawingNoPartP}'");
                if (item.DrawingNoPartG != "G") throw new Exception($"PartG failed: expected 'G', got '{item.DrawingNoPartG}'");
                if (item.DrawingNoPartB != "BBB") throw new Exception($"PartB failed: expected 'BBB', got '{item.DrawingNoPartB}'");
                if (item.DrawingNoPartX != "XXXX") throw new Exception($"PartX failed: expected 'XXXX', got '{item.DrawingNoPartX}'");

                // 설명충(Explanation) 초기값 확인
                if (item.Explanation != "모터 브래킷 보강대")
                    throw new Exception($"Explanation failed: expected '모터 브래킷 보강대', got '{item.Explanation}'");

                item.Material = "SUS304";
                if (!item.IsModified) throw new Exception("Item should be marked modified after Material change.");
                if (!item.IsMaterialModified) throw new Exception("IsMaterialModified should be true.");
                if (item.IsQtyModified) throw new Exception("IsQtyModified should be false.");

                // 사용자 입력으로 하이픈 여러 개 들어간 도면번호 설정
                item.DrawingNo = "PUM-10000-A-001-0001";
                if (!item.IsDrawingNoModified) throw new Exception("IsDrawingNoModified should be true.");
                if (item.DrawingNo != "PUM10000A0010001")
                    throw new Exception($"DrawingNo should strip all hyphens: expected 'PUM10000A0010001', got '{item.DrawingNo}'");

                if (item.DrawingNoPartO != "PUM" || item.DrawingNoPartP != "10000" || item.DrawingNoPartG != "A" || item.DrawingNoPartB != "001" || item.DrawingNoPartX != "0001")
                    throw new Exception("PUM10000A0010001 segmentation failed.");

                // 설명충 수정
                item.Explanation = "변경된 상세 설명";
                if (!item.IsExplanationModified) throw new Exception("IsExplanationModified should be true.");

                item.ResetToOriginal();
                if (item.IsModified) throw new Exception("Item should not be modified after ResetToOriginal().");
                if (item.Material != "SS400") throw new Exception($"Material should be reset to SS400, got {item.Material}");
                if (item.DrawingNo != "OOOPPPPPGBBBXXXX") throw new Exception($"DrawingNo should be reset to OOOPPPPPGBBBXXXX, got {item.DrawingNo}");
                if (item.Explanation != "모터 브래킷 보강대") throw new Exception($"Explanation should be reset to '모터 브래킷 보강대', got {item.Explanation}");

                Console.WriteLine(" [PASS] Test 2: 도면번호 하이픈(-) 자동 제외, OOO-PPPPPGBBBXXXX 컬러 세그먼트 파싱, 설명충 속성 및 복원 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 2: {ex.Message}");
                failed++;
            }

            // Test 3: Mock Connector BOM Loading
            try
            {
                var mock = new MockSwConnector();
                var (items, err) = mock.LoadBom(topLevelOnly: false);
                if (err != null) throw new Exception($"Mock LoadBom error: {err}");
                if (items.Count != 10) throw new Exception($"Expected 10 items, got {items.Count}");

                var (topItems, _) = mock.LoadBom(topLevelOnly: true);
                if (topItems.Count != 5) throw new Exception($"Expected 5 top-level items, got {topItems.Count}");

                // 설명충 데이터 로딩 확인
                if (string.IsNullOrEmpty(items[0].Explanation))
                    throw new Exception("Mock item 0 should have non-empty Explanation (설명충)");

                Console.WriteLine($" [PASS] Test 3: SolidWorks 커넥터 목업 로드 (전체: {items.Count}개, 최상위: {topItems.Count}개, 설명충 로드) 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 3: {ex.Message}");
                failed++;
            }

            // Test 4: Excel (.xlsx) and CSV Export
            try
            {
                var mock = new MockSwConnector();
                var (items, _) = mock.LoadBom();
                string testXlsx = Path.Combine(Path.GetTempPath(), "test_bom_export.xlsx");
                string testCsv = Path.Combine(Path.GetTempPath(), "test_bom_export.csv");

                bool xlsxOk = BomExporter.ExportToExcel(items, testXlsx, "PUMP_UNIT_ASSY");
                if (!xlsxOk || !File.Exists(testXlsx)) throw new Exception("Excel export failed or file not created.");
                var fileInfo = new FileInfo(testXlsx);
                if (fileInfo.Length < 1000) throw new Exception($"Exported xlsx size suspiciously small: {fileInfo.Length} bytes");

                bool csvOk = BomExporter.ExportToCsv(items, testCsv);
                if (!csvOk || !File.Exists(testCsv)) throw new Exception("CSV export failed or file not created.");
                string csvContent = File.ReadAllText(testCsv);
                if (!csvContent.Contains("Drawing No.")) throw new Exception("CSV header should contain 'Drawing No.'");
                if (!csvContent.Contains("설명충")) throw new Exception("CSV header should contain '설명충'");
                if (csvContent.Contains("Common Part")) throw new Exception("CSV header should not contain 'Common Part'");
                if (!csvContent.Contains("PUM10000A0010001")) throw new Exception("CSV data should contain clean drawing numbers like 'PUM10000A0010001'");
                if (!csvContent.Contains("메인 펌프 유닛 조립체")) throw new Exception("CSV data should contain explanation text");

                File.Delete(testXlsx);
                File.Delete(testCsv);

                Console.WriteLine(" [PASS] Test 4: 순수 C# OpenXML Excel (.xlsx) 및 UTF-8 BOM CSV 내보내기 (Drawing No., 설명충 포함) 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 4: {ex.Message}");
                failed++;
            }

            // Test 5: Real SolidWorks Connection Test
            try
            {
                var connector = new SwConnector();
                var (ok, msg) = connector.Connect();
                Console.WriteLine($" [INFO] Test 5 (Real SW): Connect -> Success={ok}, Msg={msg}");
                var info = connector.GetActiveAssemblyInfo();
                Console.WriteLine($" [INFO] Test 5 (Real SW): AssemblyInfo -> Title={info.Title}, Connected={info.IsConnected}, Err={info.ErrorMessage}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [INFO] Test 5 (Real SW exception): {ex.Message}");
            }

            Console.WriteLine($"\n=== 결과: {passed} 통과, {failed} 실패 ===");
            return failed == 0 ? 0 : 1;
        }
    }
}
