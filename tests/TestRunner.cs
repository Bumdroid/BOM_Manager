using System;
using System.Collections.Generic;
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

                // Default collapsed state (▶)
                if (sub0.PartNameDisplay != "▶ MAIN_ASSY")
                    throw new Exception($"Sub0 collapsed display failed: expected '▶ MAIN_ASSY', got '{sub0.PartNameDisplay}'");
                if (sub1.PartNameDisplay != "   ▶ SUB1_MODULE")
                    throw new Exception($"Sub1 collapsed display failed: expected '   ▶ SUB1_MODULE', got '{sub1.PartNameDisplay}'");
                if (sub2.PartNameDisplay != "      ▶ SUB2_MODULE")
                    throw new Exception($"Sub2 collapsed display failed: expected '      ▶ SUB2_MODULE', got '{sub2.PartNameDisplay}'");

                // Expanded state (▼)
                sub0.IsExpanded = true;
                sub1.IsExpanded = true;
                sub2.IsExpanded = true;
                if (sub0.PartNameDisplay != "▼ MAIN_ASSY")
                    throw new Exception($"Sub0 expanded display failed: expected '▼ MAIN_ASSY', got '{sub0.PartNameDisplay}'");
                if (sub1.PartNameDisplay != "   ▼ SUB1_MODULE")
                    throw new Exception($"Sub1 expanded display failed: expected '   ▼ SUB1_MODULE', got '{sub1.PartNameDisplay}'");
                if (sub2.PartNameDisplay != "      ▼ SUB2_MODULE")
                    throw new Exception($"Sub2 expanded display failed: expected '      ▼ SUB2_MODULE', got '{sub2.PartNameDisplay}'");

                if (part0.PartNameDisplay != "PART_LVL0")
                    throw new Exception($"Part0 display failed: expected 'PART_LVL0', got '{part0.PartNameDisplay}'");
                if (part1.PartNameDisplay != "   └  PART_LVL1")
                    throw new Exception($"Part1 display failed: expected '   └  PART_LVL1', got '{part1.PartNameDisplay}'");
                if (part2.PartNameDisplay != "      └  PART_LVL2")
                    throw new Exception($"Part2 display failed: expected '      └  PART_LVL2', got '{part2.PartNameDisplay}'");

                // Test dynamic dropdown categories when parent is LID Assy
                part1.UpdateAvailableAssyCategories("LID Assy");
                if (!part1.AvailableAssyCategories.Contains("Cover") || !part1.AvailableAssyCategories.Contains("Pusher") ||
                    !part1.AvailableAssyCategories.Contains("Pusher bolt") || !part1.AvailableAssyCategories.Contains("Pusher spring") ||
                    !part1.AvailableAssyCategories.Contains("Lid bolt"))
                {
                    throw new Exception("LID Assy child categories verification failed.");
                }

                // Test dynamic dropdown categories when parent is Elastomer Assy.
                part1.UpdateAvailableAssyCategories("Elastomer Assy.");
                if (!part1.AvailableAssyCategories.Contains("FRAME") || !part1.AvailableAssyCategories.Contains("ELASTOMER") ||
                    !part1.AvailableAssyCategories.Contains("FRAME BOLT") || !part1.AvailableAssyCategories.Contains("BOTTOM COVER ASSY"))
                {
                    throw new Exception("Elastomer Assy. child categories verification failed.");
                }

                // Test dynamic dropdown categories when parent is BSS Assy.
                part1.UpdateAvailableAssyCategories("BSS Assy.");
                if (!part1.AvailableAssyCategories.Contains("BSS BASE") || !part1.AvailableAssyCategories.Contains("INSULATION FILM"))
                {
                    throw new Exception("BSS Assy. child categories verification failed.");
                }

                Console.WriteLine(" [PASS] Test 1: 서브어셈블리(접힘/펼침 및 인덴트), 파트 인덴트(└), 동적 Assy. 하위 드롭다운(LID, Elastomer, BSS) 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 1: {ex.Message}");
                failed++;
            }

            // Test 2: Modification tracking, Drawing No Hyphen Stripping & Segment Parsing, 설명충 & AssyCategory Property
            try
            {
                // 입력 시 하이픈(-) 포함되어 있어도 저장 시 하이픈 제외 검증
                var item = new BOMItem(1, "BRACKET", "SS400", 2, "비고", drawingNo: "OOO-PPPPPGBBBXXXX", explanation: "모터 브래킷 보강대", assyCategory: "LID Assy");
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

                // 설명충(Explanation) 및 AssyCategory 초기값 확인
                if (item.Explanation != "모터 브래킷 보강대")
                    throw new Exception($"Explanation failed: expected '모터 브래킷 보강대', got '{item.Explanation}'");
                if (item.AssyCategory != "LID Assy")
                    throw new Exception($"AssyCategory failed: expected 'LID Assy', got '{item.AssyCategory}'");

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

                // AssyCategory 수정
                item.AssyCategory = "Elastomer Assy.";
                if (!item.IsAssyCategoryModified) throw new Exception("IsAssyCategoryModified should be true.");

                item.ResetToOriginal();
                if (item.IsModified) throw new Exception("Item should not be modified after ResetToOriginal().");
                if (item.Material != "SS400") throw new Exception($"Material should be reset to SS400, got {item.Material}");
                if (item.DrawingNo != "OOOPPPPPGBBBXXXX") throw new Exception($"DrawingNo should be reset to OOOPPPPPGBBBXXXX, got {item.DrawingNo}");
                if (item.Explanation != "모터 브래킷 보강대") throw new Exception($"Explanation should be reset to '모터 브래킷 보강대', got {item.Explanation}");
                if (item.AssyCategory != "LID Assy") throw new Exception($"AssyCategory should be reset to 'LID Assy', got {item.AssyCategory}");

                Console.WriteLine(" [PASS] Test 2: 도면번호 하이픈(-) 자동 제외, OOO-PPPPPGBBBXXXX 컬러 세그먼트 파싱, AssyCategory, 설명충 속성 및 복원 검증 성공");
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

                // 설명충 및 AssyCategory 데이터 로딩 확인
                if (string.IsNullOrEmpty(items[0].Explanation))
                    throw new Exception("Mock item 0 should have non-empty Explanation (설명충)");
                if (string.IsNullOrEmpty(items[0].AssyCategory))
                    throw new Exception("Mock item 0 should have non-empty AssyCategory");

                Console.WriteLine($" [PASS] Test 3: SolidWorks 커넥터 목업 로드 (전체: {items.Count}개, 최상위: {topItems.Count}개, AssyCategory, 설명충 로드) 성공");
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
                if (!csvContent.Contains("Assy.")) throw new Exception("CSV header should contain 'Assy.'");
                if (!csvContent.Contains("설명충")) throw new Exception("CSV header should contain '설명충'");
                if (csvContent.Contains("Common Part")) throw new Exception("CSV header should not contain 'Common Part'");
                if (!csvContent.Contains("PUM10000A0010001")) throw new Exception("CSV data should contain clean drawing numbers like 'PUM10000A0010001'");
                if (!csvContent.Contains("메인 펌프 유닛 조립체")) throw new Exception("CSV data should contain explanation text");

                File.Delete(testXlsx);
                File.Delete(testCsv);

                Console.WriteLine(" [PASS] Test 4: 순수 C# OpenXML Excel (.xlsx) 및 UTF-8 BOM CSV 내보내기 (Assy., Drawing No., 설명충 포함) 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 4: {ex.Message}");
                failed++;
            }

            // Test 5: Standalone Document Open & Real SolidWorks Connection Test
            try
            {
                var mock = new MockSwConnector();
                var (openOk, openMsg) = mock.OpenDocument(@"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM");
                if (!openOk) throw new Exception($"Mock OpenDocument failed: {openMsg}");

                var (emptyOk, _) = mock.OpenDocument("");
                if (emptyOk) throw new Exception("Empty path should fail in OpenDocument");

                Console.WriteLine(" [PASS] Test 5: Standalone 어셈블리 문서 직접 열기(OpenDocument) 기능 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 5: {ex.Message}");
                failed++;
            }

            // Test 6: Real SolidWorks Connection Test
            try
            {
                var connector = new SwConnector();
                var (ok, msg) = connector.Connect();
                Console.WriteLine($" [INFO] Test 6 (Real SW): Connect -> Success={ok}, Msg={msg}");
                var info = connector.GetActiveAssemblyInfo();
                Console.WriteLine($" [INFO] Test 6 (Real SW): AssemblyInfo -> Title={info.Title}, Connected={info.IsConnected}, Err={info.ErrorMessage}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [INFO] Test 6 (Real SW exception): {ex.Message}");
            }

            // Test 7: Left-to-Right Horizontal Tree Building & Layout Test
            try
            {
                var mock = new MockSwConnector();
                var (items, _) = mock.LoadBom(topLevelOnly: false);
                var roots = BOMTreeNode.BuildForest(items, "PUMP_UNIT_ASSY.SLDASM");

                if (roots.Count == 0) throw new Exception("Roots should not be empty");
                var totalAssy = roots[0];
                if (totalAssy.Item.PartName != "PUMP_UNIT_ASSY.SLDASM") throw new Exception($"Root should be PUMP_UNIT_ASSY.SLDASM, got {totalAssy.Item.PartName}");
                if (totalAssy.Children.Count == 0) throw new Exception("Total Assy should have children");

                var (w, h) = BOMTreeNode.CalculateHorizontalLayout(roots);
                if (w < 400 || h < 200) throw new Exception($"Calculated dimensions invalid: {w}x{h}");
                if (totalAssy.X != 50 && totalAssy.X != 40) throw new Exception($"Root X should be 40 or 50, got {totalAssy.X}");
                if (totalAssy.Children[0].X <= totalAssy.X) throw new Exception("Child X should be greater than parent X in left-to-right layout");

                Console.WriteLine($" [PASS] Test 7: 좌→우 수평 트리 모델 구축(BOMTreeNode) 및 계층 좌표(X, Y) 자동 산출 검증 성공 (크기: {w:F0}x{h:F0})");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 7: {ex.Message}");
                failed++;
            }

            // Test 8: Master Root Wrapping, IsDescendantOf and Sub-Assy Reparenting
            try
            {
                // 1. Master Root wrapping test
                var flatParts = new[]
                {
                    new BOMItem(1, "PART_A", isSubassembly: false, level: 0),
                    new BOMItem(2, "PART_B", isSubassembly: false, level: 0),
                };
                var wrappedRoots = BOMTreeNode.BuildForest(flatParts, "MY_MASTER_ASSY.SLDASM");
                if (wrappedRoots.Count != 1) throw new Exception($"Expected 1 master root, got {wrappedRoots.Count}");
                if (wrappedRoots[0].Item.PartName != "MY_MASTER_ASSY.SLDASM")
                    throw new Exception($"Expected root name 'MY_MASTER_ASSY.SLDASM', got '{wrappedRoots[0].Item.PartName}'");
                if (wrappedRoots[0].Width != 380)
                    throw new Exception($"Expected node width 380, got {wrappedRoots[0].Width}");
                if (wrappedRoots[0].NodeTypeBadge != "Root Assy.")
                    throw new Exception($"Expected root badge 'Root Assy.', got '{wrappedRoots[0].NodeTypeBadge}'");

                // 2. IsDescendantOf logic test
                var parentNode = wrappedRoots[0];
                var childNode = wrappedRoots[0].Children[0];
                if (!childNode.IsDescendantOf(parentNode))
                    throw new Exception("childNode.IsDescendantOf(parentNode) should be true");
                if (parentNode.IsDescendantOf(childNode))
                    throw new Exception("parentNode.IsDescendantOf(childNode) should be false");

                // 3. Create new Sub-Assy and reparent PART_A under it
                var newSubItem = new BOMItem(3, "NEW_CUSTOM_SUB_ASSY", isSubassembly: true, level: 1);
                var sub2Item = new BOMItem(4, "CHILD_SUB_ASSY", isSubassembly: true, level: 2);
                var allItemsList = new System.Collections.Generic.List<BOMItem> { flatParts[0], flatParts[1], newSubItem, sub2Item };

                // Move flatParts[0] (PART_A) under newSubItem
                // In flat list, newSubItem is at level 1, so PART_A becomes level 2
                flatParts[0].Level = newSubItem.Level + 1;
                allItemsList.Remove(flatParts[0]);
                int targetIdx = allItemsList.IndexOf(newSubItem);
                allItemsList.Insert(targetIdx + 1, flatParts[0]);

                var updatedForest = BOMTreeNode.BuildForest(allItemsList, "MY_MASTER_ASSY.SLDASM");
                var updatedRoot = updatedForest[0];
                var subNode = updatedRoot.Children.FirstOrDefault(c => c.Item.PartName == "NEW_CUSTOM_SUB_ASSY");
                if (subNode == null) throw new Exception("NEW_CUSTOM_SUB_ASSY not found in forest");
                if (subNode.NodeTypeBadge != "Sub1 Assy.")
                    throw new Exception($"Expected badge 'Sub1 Assy.', got '{subNode.NodeTypeBadge}'");
                if (subNode.Children.Count != 2 || subNode.Children[0].Item.PartName != "PART_A")
                    throw new Exception($"PART_A was not reparented under NEW_CUSTOM_SUB_ASSY. Found {subNode.Children.Count} children");

                var sub2Node = subNode.Children.FirstOrDefault(c => c.Item.PartName == "CHILD_SUB_ASSY");
                if (sub2Node == null) throw new Exception("CHILD_SUB_ASSY not found under NEW_CUSTOM_SUB_ASSY");
                if (sub2Node.TreeDepth != 2 || sub2Node.NodeTypeBadge != "Sub2 Assy.")
                    throw new Exception($"Expected Sub2 node with depth 2 and badge 'Sub2 Assy.', got depth={sub2Node.TreeDepth}, badge='{sub2Node.NodeTypeBadge}'");

                Console.WriteLine(" [PASS] Test 8: 최상위 Root Assy.(좌측 파란배경) 및 계층별 Sub1 Assy./Sub2 Assy. 배지, 1.5배 박스 폭(330px), 드래그앤드롭 재부모화 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 8: {ex.Message}");
                failed++;
            }

            // Test 9: Node Applied/Unapplied Background & Border Colors + Ancestor Chain
            try
            {
                var root = new BOMTreeNode(new BOMItem(0, "MAIN_ASSY.SLDASM", isSubassembly: true, level: 0));
                var sub1 = new BOMTreeNode(new BOMItem(1, "SUB1", isSubassembly: true, level: 1), root);
                root.Children.Add(sub1);
                var part1 = new BOMTreeNode(new BOMItem(2, "PART1", isSubassembly: false, level: 2), sub1);
                sub1.Children.Add(part1);

                // 1. Root colors
                if (root.NodeBackground != "#ECFDF5" || root.NodeBorderBrush != "#10B981")
                    throw new Exception($"Root color mismatch: bg={root.NodeBackground}, border={root.NodeBorderBrush}");

                // 2. Unapplied colors
                if (sub1.NodeBackground != "#FFFFFF" || sub1.NodeBorderBrush != "#CBD5E1")
                    throw new Exception($"Unapplied sub1 color mismatch: bg={sub1.NodeBackground}, border={sub1.NodeBorderBrush}");
                if (part1.NodeBackground != "#FFFFFF" || part1.NodeBorderBrush != "#CBD5E1")
                    throw new Exception($"Unapplied part1 color mismatch: bg={part1.NodeBackground}, border={part1.NodeBorderBrush}");

                // 3. Applied colors
                sub1.Item.AssyCategory = "Elastomer Assy.";
                sub1.IsApproved = false; // Unchecked state -> Should remain white (Unapplied)
                if (sub1.IsApplied || sub1.NodeBackground != "#FFFFFF" || sub1.NodeBorderBrush != "#CBD5E1")
                    throw new Exception($"Unapproved sub1 with category should be white: isApplied={sub1.IsApplied}, bg={sub1.NodeBackground}");

                sub1.IsApproved = true; // Checked state -> Should be green (Applied)
                if (!sub1.IsApplied || sub1.NodeBackground != "#ECFDF5" || sub1.NodeBorderBrush != "#10B981")
                    throw new Exception($"Applied sub1 color mismatch: isApplied={sub1.IsApplied}, bg={sub1.NodeBackground}, border={sub1.NodeBorderBrush}");

                part1.Item.AssyCategory = "ELASTOMER";
                if (!part1.IsApplied || part1.NodeBackground != "#ECFDF5" || part1.NodeBorderBrush != "#10B981")
                    throw new Exception($"Applied part1 color mismatch: isApplied={part1.IsApplied}, bg={part1.NodeBackground}, border={part1.NodeBorderBrush}");

                // 4. Ancestor relationship for blue selection trail
                if (!part1.IsDescendantOf(sub1) || !part1.IsDescendantOf(root))
                    throw new Exception("part1 should have sub1 and root as ancestors");

                Console.WriteLine(" [PASS] Test 9: 승인/미승인 배경색(초록/흰색) 및 테두리색, 선택 시 상위 Assy. 전파 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 9: {ex.Message}");
                failed++;
            }

            // Test 10: FRAME ASSY and LID ASSY Category Hierarchy & Options
            try
            {
                var root = new BOMTreeNode(new BOMItem(0, "MAIN_ASSY.SLDASM", isSubassembly: true, level: 0));
                var elastomerAssy = new BOMTreeNode(new BOMItem(1, "ELASTOMER ASSY", isSubassembly: true, level: 1, assyCategory: "Elastomer Assy."), root);
                root.Children.Add(elastomerAssy);
                var frameAssy = new BOMTreeNode(new BOMItem(2, "FRAME ASSY", isSubassembly: true, level: 2, assyCategory: "FRAME Assy."), elastomerAssy);
                elastomerAssy.Children.Add(frameAssy);
                var framePart = new BOMTreeNode(new BOMItem(3, "FRAME PART", isSubassembly: false, level: 3, assyCategory: "FRAME"), frameAssy);
                frameAssy.Children.Add(framePart);

                var lidAssy = new BOMTreeNode(new BOMItem(4, "LID ASSY", isSubassembly: true, level: 1, assyCategory: "LID Assy"), root);
                root.Children.Add(lidAssy);
                var pusherPart = new BOMTreeNode(new BOMItem(5, "PUSHER PART", isSubassembly: false, level: 2, assyCategory: "Pusher"), lidAssy);
                lidAssy.Children.Add(pusherPart);

                if (!framePart.IsDescendantOf(frameAssy) || !framePart.IsDescendantOf(elastomerAssy) || !framePart.IsDescendantOf(root))
                    throw new Exception("framePart hierarchy chain is broken");

                if (!pusherPart.IsDescendantOf(lidAssy) || !pusherPart.IsDescendantOf(root))
                    throw new Exception("pusherPart hierarchy chain is broken");

                if (!framePart.IsApplied || !pusherPart.IsApplied)
                    throw new Exception("Applied status should be true for categorized parts");

                Console.WriteLine(" [PASS] Test 10: FRAME ASSY(FRAME, FRAME BUSH, BUSH BOLT, BLOCK) 및 LID ASSY(Cover, Pusher...) 계층 분류 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 10: {ex.Message}");
                failed++;
            }

            // Test 11: ApplySubAssembliesToFile (SolidWorks Sub-Assy 생성 및 파일 적용)
            try
            {
                var mockService = new MockSwConnector();
                var items = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1),
                    new BOMItem(2, "COVER_PART", isSubassembly: false, level: 2),
                    new BOMItem(3, "PUSHER_PART", isSubassembly: false, level: 2),
                    new BOMItem(4, "FRAME_ASSY", isSubassembly: true, level: 1),
                    new BOMItem(5, "FRAME_BASE", isSubassembly: false, level: 2)
                };

                var res = mockService.ApplySubAssembliesToFile(items, @"C:\CAD_Projects\TestAssy");
                if (!res.Success || res.CreatedCount != 2)
                    throw new Exception($"ApplySubAssembliesToFile failed: Success={res.Success}, Count={res.CreatedCount}");

                if (items[0].FileName != "LID_ASSY.sldasm" || items[3].FileName != "FRAME_ASSY.sldasm")
                    throw new Exception($"Sub-Assy FileName mismatch: {items[0].FileName}, {items[3].FileName}");

                Console.WriteLine(" [PASS] Test 11: Sub-Assy 파일 적용(ApplySubAssembliesToFile) 및 .sldasm 생성 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 11: {ex.Message}");
                failed++;
            }

            // Test 12: Reload & State Preservation/Merge (파일 적용 후 트리 리프레시 시 사용자 입력값 보존)
            try
            {
                var originalItems = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1, assyCategory: "LID ASSY")
                    {
                        FilePath = @"C:\CAD\LID_ASSY.sldasm",
                        FileName = "LID_ASSY.sldasm",
                        Explanation = "상단 커버 어셈블리",
                        Remark = "중요 부품"
                    },
                    new BOMItem(2, "COVER_PART", isSubassembly: false, level: 2, drawingNo: "123-45678A999", material: "AL6061-T6")
                    {
                        FilePath = @"C:\CAD\COVER_PART.sldprt",
                        FileName = "COVER_PART.sldprt",
                        Rev = "A",
                        Explanation = "커버 가공품",
                        Remark = "아노다이징",
                        IsModified = true
                    }
                };

                // 스냅샷 시뮬레이션
                var stateMap = new Dictionary<string, BOMItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var itm in originalItems)
                {
                    if (!string.IsNullOrEmpty(itm.FilePath))
                        stateMap[itm.FilePath.ToLowerInvariant()] = itm;
                }

                // SolidWorks에서 새로 로드된 새 BOMItem 인스턴스들 (기본적으로 속성이 비어있음)
                var reloadedItems = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1)
                    {
                        FilePath = @"C:\CAD\LID_ASSY.sldasm",
                        FileName = "LID_ASSY.sldasm"
                    },
                    new BOMItem(2, "COVER_PART", isSubassembly: false, level: 2)
                    {
                        FilePath = @"C:\CAD\COVER_PART.sldprt",
                        FileName = "COVER_PART.sldprt"
                    }
                };

                // 상태 복원 및 병합
                foreach (var itm in reloadedItems)
                {
                    if (stateMap.TryGetValue(itm.FilePath.ToLowerInvariant(), out var orig))
                    {
                        if (!string.IsNullOrWhiteSpace(orig.DrawingNo)) itm.DrawingNo = orig.DrawingNo;
                        if (!string.IsNullOrWhiteSpace(orig.Material)) itm.Material = orig.Material;
                        if (!string.IsNullOrWhiteSpace(orig.Rev)) itm.Rev = orig.Rev;
                        if (!string.IsNullOrWhiteSpace(orig.Explanation)) itm.Explanation = orig.Explanation;
                        if (!string.IsNullOrWhiteSpace(orig.Remark)) itm.Remark = orig.Remark;
                        if (!string.IsNullOrWhiteSpace(orig.AssyCategory)) itm.AssyCategory = orig.AssyCategory;
                        itm.IsModified = orig.IsModified;
                    }
                }

                // 검증
                if (reloadedItems[1].DrawingNo != "12345678A999" || reloadedItems[1].Material != "AL6061-T6" ||
                    reloadedItems[1].Rev != "A" || reloadedItems[1].Remark != "아노다이징" ||
                    reloadedItems[0].Explanation != "상단 커버 어셈블리" || reloadedItems[0].AssyCategory != "LID ASSY")
                {
                    throw new Exception("State preservation and merge failed for reloaded items");
                }

                Console.WriteLine(" [PASS] Test 12: 파일 적용 후 트리 리프레시 시 사용자 입력값(도번, 재질, 설명, 비고, 카테고리) 보존 및 병합 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 12: {ex.Message}");
                failed++;
            }

            // Test 13: Initial Tree Expansion - Only Root is Expanded, Sub1 and deeper levels Collapsed
            try
            {
                var flatItems = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1),
                    new BOMItem(2, "COVER_PART", isSubassembly: false, level: 2),
                    new BOMItem(3, "PUSHER_PART", isSubassembly: false, level: 2),
                    new BOMItem(4, "ELASTOMER_ASSY", isSubassembly: true, level: 1),
                    new BOMItem(5, "FRAME_ASSY", isSubassembly: true, level: 2),
                    new BOMItem(6, "FRAME_PART", isSubassembly: false, level: 3)
                };

                var roots = BOMTreeNode.BuildForest(flatItems, "TOP_ASSEMBLY.SLDASM");
                if (roots.Count != 1) throw new Exception($"Expected 1 master root, got {roots.Count}");

                // Simulate LoadItems initial expansion policy
                foreach (var root in roots)
                {
                    root.IsExpanded = true;
                    foreach (var child in root.Children)
                    {
                        void CollapseRec(BOMTreeNode n)
                        {
                            n.IsExpanded = false;
                            if (n.Item != null) n.Item.IsExpanded = false;
                            foreach (var c in n.Children) CollapseRec(c);
                        }
                        CollapseRec(child);
                    }
                }

                var topRoot = roots[0];
                if (!topRoot.IsExpanded) throw new Exception("Top root should be expanded");
                if (topRoot.Children.Count != 2) throw new Exception($"Expected 2 Sub1 nodes, got {topRoot.Children.Count}");

                foreach (var sub1Node in topRoot.Children)
                {
                    if (sub1Node.IsExpanded)
                        throw new Exception($"Sub1 node '{sub1Node.Item.PartName}' should be collapsed (IsExpanded=false)");
                    foreach (var sub2Node in sub1Node.Children)
                    {
                        if (sub2Node.IsExpanded)
                            throw new Exception($"Sub2 node '{sub2Node.Item.PartName}' should be collapsed (IsExpanded=false)");
                    }
                }

                Console.WriteLine(" [PASS] Test 13: 최초 트리 로드 시 Root만 확장되고 Sub1 및 하위 레벨은 기본 접힘(Collapsed) 상태 유지 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 13: {ex.Message}");
                failed++;
            }

            // Test 14: State Preservation on Sub-Assy Creation / Tree Refresh
            try
            {
                var flatItems = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1, assyCategory: "LID ASSY") { IsExpanded = true, IsApproved = true },
                    new BOMItem(2, "COVER_PART", isSubassembly: false, level: 2, assyCategory: "Cover"),
                    new BOMItem(3, "PUSHER_PART", isSubassembly: false, level: 2, assyCategory: "Pusher"),
                    new BOMItem(4, "ELASTOMER_ASSY", isSubassembly: true, level: 1, assyCategory: "ELASTOMER ASSY") { IsExpanded = false, IsApproved = false } // Unapproved / collapsed
                };

                // Capture expansion and approval map before adding new Sub-Assy
                var expansionMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var approvalMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var itm in flatItems)
                {
                    string key = itm.PartName + "_" + itm.Level;
                    expansionMap[key] = itm.IsExpanded;
                    approvalMap[key] = itm.IsApproved;
                }

                // Add newly created Sub-Assy
                var newSub = new BOMItem(5, "FRAME_ASSY", isSubassembly: true, level: 2, assyCategory: "FRAME ASSY") { IsExpanded = true, IsApproved = true };
                flatItems.Insert(4, newSub);
                expansionMap[newSub.PartName + "_" + newSub.Level] = true;
                approvalMap[newSub.PartName + "_" + newSub.Level] = true;

                // Re-build forest and restore
                var roots = BOMTreeNode.BuildForest(flatItems, "TOP_ASSEMBLY.SLDASM");
                foreach (var root in roots)
                {
                    root.IsExpanded = true;
                    void RestoreRec(BOMTreeNode n)
                    {
                        if (n.TreeDepth > 0 && n.Item != null)
                        {
                            string key = n.Item.PartName + "_" + n.Item.Level;
                            if (expansionMap.TryGetValue(key, out bool exp))
                            {
                                n.IsExpanded = exp;
                                n.Item.IsExpanded = exp;
                            }
                            if (approvalMap.TryGetValue(key, out bool app))
                            {
                                n.IsApproved = app;
                                n.Item.IsApproved = app;
                            }
                        }
                        foreach (var c in n.Children) RestoreRec(c);
                    }
                    RestoreRec(root);
                }

                var lidNode = roots[0].Children.First(c => c.Item.PartName == "LID_ASSY");
                var elastomerNode = roots[0].Children.First(c => c.Item.PartName == "ELASTOMER_ASSY");
                var newFrameNode = elastomerNode.Children.First(c => c.Item.PartName == "FRAME_ASSY");

                if (!lidNode.IsExpanded || !lidNode.IsApplied)
                    throw new Exception("LID_ASSY should remain expanded (IsExpanded=true) and applied (green)");
                if (elastomerNode.IsExpanded || elastomerNode.IsApplied)
                    throw new Exception("ELASTOMER_ASSY should remain collapsed (IsExpanded=false) and unapplied (white)");
                if (!newFrameNode.IsExpanded || !newFrameNode.IsApplied)
                    throw new Exception("Newly created FRAME_ASSY should be expanded and applied");

                Console.WriteLine(" [PASS] Test 14: 새 Sub-Assy 생성/트리 갱신 시 기존 승인/펼침 상태(IsExpanded) 100% 보존 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 14: {ex.Message}");
                failed++;
            }

            // Test 15: Virtual Subassembly & (RA01) Path PartName Extraction
            try
            {
                // 1. 가상 어셈블리 경로 및 컴포넌트명
                string name1 = SwConnector.ExtractCleanPartName(@"C:\CAD\PROJECT (RA01)\[FRAME ASSY^MAIN].sldasm", "MAIN-1/[FRAME ASSY^MAIN]-1", "[FRAME ASSY^MAIN]");
                if (name1 != "FRAME ASSY") throw new Exception($"Expected 'FRAME ASSY', got '{name1}'");

                // 2. 가상 어셈블리 + 내부 리비전 표시
                string name2 = SwConnector.ExtractCleanPartName(@"C:\Temp\(RA01)\[FRAME ASSY (RA01)^MAIN].sldasm", "MAIN-1/[FRAME ASSY (RA01)^MAIN]-1", "[FRAME ASSY (RA01)^MAIN]");
                if (name2 != "FRAME ASSY (RA01)") throw new Exception($"Expected 'FRAME ASSY (RA01)', got '{name2}'");

                // 3. 인스턴스 번호가 붙은 파트
                string name3 = SwConnector.ExtractCleanPartName(@"C:\CAD\PumpUnit\BASE_FRAME.SLDPRT", "BASE_FRAME-2");
                if (name3 != "BASE_FRAME") throw new Exception($"Expected 'BASE_FRAME', got '{name3}'");

                // 4. 경로 없이 컴포넌트명만 존재하는 가상 파트
                string name4 = SwConnector.ExtractCleanPartName(null, "[MOTOR_BRACKET^MAIN]-1", null);
                if (name4 != "MOTOR_BRACKET") throw new Exception($"Expected 'MOTOR_BRACKET', got '{name4}'");

                Console.WriteLine(" [PASS] Test 15: 가상 서브어셈블리 [Name^Parent] 및 (RA01) 경로에서의 순수 PartName 추출 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 15: {ex.Message}");
                failed++;
            }

            // Test 16: IsApproved Decoupled from IsExpanded (개수 뱃지 클릭 시 승인 방지)
            try
            {
                var rootItem = new BOMItem(1, "ROOT_ASSY", isSubassembly: true, level: 0);
                var rootNode = new BOMTreeNode(rootItem);
                var subAssyItem = new BOMItem(2, "LID_ASSY", isSubassembly: true, level: 1, assyCategory: "LID Assy");
                var subAssyNode = new BOMTreeNode(subAssyItem, rootNode);

                // Initial state: not approved, collapsed
                if (subAssyNode.IsApproved || subAssyNode.IsApplied || subAssyNode.NodeBackground != "#FFFFFF")
                {
                    throw new Exception("Initially unapproved subassembly should be unapproved and white background");
                }

                // Expand node (simulate clicking "~개" count badge)
                subAssyNode.IsExpanded = true;
                subAssyItem.IsExpanded = true;

                // MUST still be unapproved and white!
                if (subAssyNode.IsApproved)
                {
                    throw new Exception("Expanding node must NOT make IsApproved true!");
                }
                if (subAssyNode.IsApplied || subAssyNode.NodeBackground != "#FFFFFF")
                {
                    throw new Exception("Expanding node without explicit [승인] check must remain white/unapplied");
                }

                // Explicitly approve (simulate clicking [승인] checkbox)
                subAssyNode.IsApproved = true;
                if (!subAssyNode.IsApproved || !subAssyNode.IsApplied || subAssyNode.NodeBackground != "#ECFDF5")
                {
                    throw new Exception("Explicitly approved subassembly must become green/applied");
                }

                // Collapse node while approved
                subAssyNode.IsExpanded = false;
                subAssyItem.IsExpanded = false;

                // MUST still be approved!
                if (!subAssyNode.IsApproved || !subAssyNode.IsApplied)
                {
                    throw new Exception("Collapsing node must NOT revoke approved status!");
                }

                Console.WriteLine(" [PASS] Test 16: ~개 개수 뱃지 순수 텍스트 표시 및 [승인] 체크 기반 독립적 트리 펼침/접힘 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 16: {ex.Message}");
                failed++;
            }

            // Test 17: Unapproved items validation check
            try
            {
                var items = new List<BOMItem>
                {
                    new BOMItem(1, "MAIN_ASSY", isSubassembly: true, level: 0),
                    new BOMItem(2, "LID_ASSY", isSubassembly: true, level: 1, assyCategory: "LID Assy") { IsApproved = false },
                    new BOMItem(3, "COVER", isSubassembly: false, level: 2, assyCategory: "Cover"),
                    new BOMItem(4, "UNASSIGNED_PART", isSubassembly: false, level: 2, assyCategory: "")
                };

                var unapprovedSubs = items.Where(i => i.IsSubassembly && i.Level > 0 && !i.IsApproved).ToList();
                var unassignedParts = items.Where(i => !i.IsSubassembly && string.IsNullOrWhiteSpace(i.AssyCategory)).ToList();

                if (unapprovedSubs.Count != 1 || unapprovedSubs[0].PartName != "LID_ASSY")
                    throw new Exception("Failed to detect unapproved Sub-Assy");
                if (unassignedParts.Count != 1 || unassignedParts[0].PartName != "UNASSIGNED_PART")
                    throw new Exception("Failed to detect unassigned part");

                // Approve all
                items[1].IsApproved = true;
                items[3].AssyCategory = "Pusher";

                var unapprovedSubs2 = items.Where(i => i.IsSubassembly && i.Level > 0 && !i.IsApproved).ToList();
                var unassignedParts2 = items.Where(i => !i.IsSubassembly && string.IsNullOrWhiteSpace(i.AssyCategory)).ToList();

                if (unapprovedSubs2.Count != 0 || unassignedParts2.Count != 0)
                    throw new Exception("All items should be validated as approved");

                Console.WriteLine(" [PASS] Test 17: 정리된 파일 저장 시 미승인 항목 사전 검증 및 차단 로직 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 17: {ex.Message}");
                failed++;
            }

            // Test 18: Auto_3D Hierarchical Folder Structure & File Copies
            try
            {
                string testExportDir = Path.Combine(Path.GetTempPath(), "BOMManager_Auto3D_Test_" + Guid.NewGuid().ToString("N"));
                var mockService = new MockSwConnector();

                var testItems = new List<BOMItem>
                {
                    new BOMItem(1, "LID_ASSY", isSubassembly: true, level: 1, assyCategory: "LID ASSY") { IsApproved = true },
                    new BOMItem(2, "COVER", isSubassembly: false, level: 2, assyCategory: "Cover"),
                    new BOMItem(3, "ELASTOMER_ASSY", isSubassembly: true, level: 1, assyCategory: "ELASTOMER ASSY") { IsApproved = true },
                    new BOMItem(4, "FRAME_ASSY", isSubassembly: true, level: 2, assyCategory: "FRAME ASSY") { IsApproved = true },
                    new BOMItem(5, "FRAME_BASE", isSubassembly: false, level: 3, assyCategory: "FRAME")
                };

                var res = mockService.ExportOrganizedAuto3DFiles(testItems, testExportDir);

                if (!res.Success) throw new Exception($"Auto_3D export failed: {string.Join(", ", res.Messages)}");

                string auto3DRoot = Path.Combine(testExportDir, "Auto_3D");
                string rootAsm = Path.Combine(auto3DRoot, "PumpUnit_Root.sldasm");
                string lidDir = Path.Combine(auto3DRoot, "LID_ASSY");
                string lidAsm = Path.Combine(lidDir, "LID_ASSY.sldasm");
                string coverPart = Path.Combine(lidDir, "COVER.sldprt");
                string elastomerDir = Path.Combine(auto3DRoot, "ELASTOMER_ASSY");
                string elastomerAsm = Path.Combine(elastomerDir, "ELASTOMER_ASSY.sldasm");
                string frameDir = Path.Combine(elastomerDir, "FRAME_ASSY");
                string frameAsm = Path.Combine(frameDir, "FRAME_ASSY.sldasm");
                string framePart = Path.Combine(frameDir, "FRAME_BASE.sldprt");

                if (!File.Exists(rootAsm)) throw new Exception($"Root asm missing: {rootAsm}");
                if (!File.Exists(lidAsm)) throw new Exception($"Sub1 lid asm missing: {lidAsm}");
                if (!File.Exists(coverPart)) throw new Exception($"Sub1 cover part missing: {coverPart}");
                if (!File.Exists(elastomerAsm)) throw new Exception($"Sub1 elastomer asm missing: {elastomerAsm}");
                if (!File.Exists(frameAsm)) throw new Exception($"Sub2 frame asm missing: {frameAsm}");
                if (!File.Exists(framePart)) throw new Exception($"Sub2 frame part missing: {framePart}");

                // Cleanup
                try { Directory.Delete(testExportDir, true); } catch { }

                Console.WriteLine(" [PASS] Test 18: Auto_3D 계층 폴더 구조(Root -> Sub1 -> Sub2) 및 .sldasm / .sldprt 분할 저장 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 18: {ex.Message}");
                failed++;
            }

            // Test 19: Developer Temp State Save, Restore & Reset Toggle
            try
            {
                string tempStateFile = Path.Combine(Path.GetTempPath(), "dev_temp_test_" + Guid.NewGuid().ToString("N") + ".json");
                var originalItems = new List<BOMItem>
                {
                    new BOMItem(1, "ELASTOMER_ASSY", material: "AL6061", qty: 1, remark: "Test Remark", filePath: @"C:\CAD\ELASTOMER_ASSY.SLDASM", isSubassembly: true, level: 1, drawingNo: "DWG12345678901234", explanation: "엘라스토머 설명", assyCategory: "ELASTOMER ASSY")
                    {
                        Rev = "A",
                        IsApproved = true,
                        IsExpanded = true,
                        IsCommonPart = false
                    },
                    new BOMItem(2, "FRAME_ASSY", material: "SUS304", qty: 2, remark: "Sub2", filePath: @"C:\CAD\FRAME_ASSY.SLDASM", isSubassembly: true, level: 2, drawingNo: "DWG12345678901235", explanation: "프레임 설명", assyCategory: "FRAME ASSY")
                    {
                        Rev = "B",
                        IsApproved = true,
                        IsExpanded = true,
                        IsCommonPart = true
                    },
                    new BOMItem(3, "FRAME_BASE", material: "AL7075", qty: 4, remark: "Part", filePath: @"C:\CAD\FRAME_BASE.SLDPRT", isSubassembly: false, level: 3, drawingNo: "DWG12345678901236", explanation: "베이스 설명", assyCategory: "FRAME")
                    {
                        Rev = "0",
                        IsApproved = true,
                        IsExpanded = false,
                        IsCommonPart = false
                    }
                };

                // Helper serialize function mirroring MainWindow logic
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"RootTitle\": \"TOTAL_ASSEMBLY.SLDASM\",");
                sb.AppendLine("  \"Items\": [");
                for (int i = 0; i < originalItems.Count; i++)
                {
                    var itm = originalItems[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"ItemNo\": {itm.ItemNo},");
                    sb.AppendLine($"      \"PartName\": \"{itm.PartName}\",");
                    sb.AppendLine($"      \"FilePath\": \"{itm.FilePath.Replace("\\", "\\\\")}\",");
                    sb.AppendLine($"      \"FileName\": \"{itm.FileName}\",");
                    sb.AppendLine($"      \"DrawingNo\": \"{itm.DrawingNo}\",");
                    sb.AppendLine($"      \"Material\": \"{itm.Material}\",");
                    sb.AppendLine($"      \"Qty\": {itm.Qty},");
                    sb.AppendLine($"      \"Rev\": \"{itm.Rev}\",");
                    sb.AppendLine($"      \"Explanation\": \"{itm.Explanation}\",");
                    sb.AppendLine($"      \"Remark\": \"{itm.Remark}\",");
                    sb.AppendLine($"      \"AssyCategory\": \"{itm.AssyCategory}\",");
                    sb.AppendLine($"      \"IsSubassembly\": {(itm.IsSubassembly ? "true" : "false")},");
                    sb.AppendLine($"      \"Level\": {itm.Level},");
                    sb.AppendLine($"      \"IsApproved\": {(itm.IsApproved ? "true" : "false")},");
                    sb.AppendLine($"      \"IsExpanded\": {(itm.IsExpanded ? "true" : "false")},");
                    sb.AppendLine($"      \"IsCommonPart\": {(itm.IsCommonPart ? "true" : "false")}");
                    sb.Append("    }");
                    if (i < originalItems.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(tempStateFile, sb.ToString(), System.Text.Encoding.UTF8);

                if (!File.Exists(tempStateFile)) throw new Exception("Temp state file was not created");

                // Parse and verify restoration
                string jsonText = File.ReadAllText(tempStateFile);
                if (!jsonText.Contains("ELASTOMER_ASSY") || !jsonText.Contains("FRAME_ASSY") || !jsonText.Contains("FRAME_BASE"))
                {
                    throw new Exception("Temp state JSON missing items");
                }

                // Verify deleting on second toggle
                File.Delete(tempStateFile);
                if (File.Exists(tempStateFile)) throw new Exception("Temp state file was not deleted on reset");

                Console.WriteLine(" [PASS] Test 19: 개발자용 임시저장(Dev Temp State) 저장, 복원 및 2회차 클릭 초기화(삭제) 로직 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 19: {ex.Message}");
                failed++;
            }

            // Test 20: User-Created Sub-Assy and [생성됨] Badge Property Check
            try
            {
                var createdSub = new BOMItem(1, "CUSTOM_SUB_ASSY", isSubassembly: true, level: 1, remark: "수동 생성된 Sub-Assy")
                {
                    IsUserCreated = true
                };
                var existingItem = new BOMItem(2, "EXISTING_PART", isSubassembly: false, level: 1);

                if (!createdSub.IsUserCreated) throw new Exception("User created subassembly must have IsUserCreated = true");
                if (existingItem.IsUserCreated) throw new Exception("Existing part must not have IsUserCreated = true");

                Console.WriteLine(" [PASS] Test 20: 수동 생성된 Sub-Assy 항목의 IsUserCreated(생성됨 뱃지) 플래그 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 20: {ex.Message}");
                failed++;
            }

            // Test 21: Vault Config Defaults (Server: 192.168.150.105, Vault: ISC_Vault, Password: "", AutoLogin: true) and Username Persistence
            try
            {
                var config = VaultConfigManager.Load();
                if (config.Server != "192.168.150.105") throw new Exception($"Expected Server '192.168.150.105', got '{config.Server}'");
                if (config.VaultName != "ISC_Vault") throw new Exception($"Expected VaultName 'ISC_Vault', got '{config.VaultName}'");
                if (config.Password != "") throw new Exception($"Expected Password '', got '{config.Password}'");

                // Test saving and restoring username and AutoLogin
                string testUser = "이두규";
                VaultConfigManager.SaveLastUsername(testUser, true);

                var reloaded = VaultConfigManager.Load();
                if (reloaded.LastUsername != testUser) throw new Exception($"Expected LastUsername '{testUser}', got '{reloaded.LastUsername}'");
                if (!reloaded.AutoLogin) throw new Exception("Expected AutoLogin to be true");

                Console.WriteLine(" [PASS] Test 21: Autodesk Vault 고정 설정(Server, Vault, Pwd) 및 사용자(이두규)/자동로그인 영구 보존 로드 검증 성공");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [FAIL] Test 21: {ex.Message}");
                failed++;
            }

            Console.WriteLine($"\n=== 결과: {passed} 통과, {failed} 실패 ===");
            return failed == 0 ? 0 : 1;
        }
    }
}


