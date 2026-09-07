using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BOMManager.Models;

namespace BOMManager.Utils
{
    public static class BomExporter
    {
        public static bool ExportToCsv(IEnumerable<BOMItem> items, string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("No.,Name of Part,Q'TY,Assy.,Material,Drawing No.,Rev.,설명충,REMARK,File Name");

                foreach (var item in items)
                {
                    string name = EscapeCsv(item.PartName);
                    string assy = EscapeCsv(item.AssyCategory);
                    string dwg = EscapeCsv(item.DrawingNo);
                    string mat = EscapeCsv(item.Material);
                    string rev = EscapeCsv(item.Rev);
                    string exp = EscapeCsv(item.Explanation);
                    string rem = EscapeCsv(item.Remark);
                    string file = EscapeCsv(item.FileName);

                    sb.AppendLine($"{item.ItemNo},{name},{item.Qty},{assy},{mat},{dwg},{rev},{exp},{rem},{file}");
                }

                // UTF-8 with BOM for Korean Excel compatibility
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ExportToExcel(IEnumerable<BOMItem> items, string filePath, string assemblyTitle = "BOM")
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                using (var zip = ZipFile.Open(filePath, ZipArchiveMode.Create))
                {
                    // 1. [Content_Types].xml
                    AddZipEntry(zip, "[Content_Types].xml", GetContentTypesXml());

                    // 2. _rels/.rels
                    AddZipEntry(zip, "_rels/.rels", GetRootRelsXml());

                    // 3. xl/_rels/workbook.xml.rels
                    AddZipEntry(zip, "xl/_rels/workbook.xml.rels", GetWorkbookRelsXml());

                    // 4. xl/styles.xml (Professional Navy Blue Theme & Borders)
                    AddZipEntry(zip, "xl/styles.xml", GetStylesXml());

                    // 5. xl/workbook.xml
                    AddZipEntry(zip, "xl/workbook.xml", GetWorkbookXml());

                    // 6. xl/worksheets/sheet1.xml
                    AddZipEntry(zip, "xl/worksheets/sheet1.xml", GetSheetXml(items, assemblyTitle));
                }

                return true;
            }
            catch
            {
                // Fallback to CSV if zip export fails
                return ExportToCsv(items, Path.ChangeExtension(filePath, ".csv"));
            }
        }

        private static void AddZipEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
            {
                return $"\"{val.Replace("\"", "\"\"")}\"";
            }
            return val;
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&apos;");
        }

        private static string GetContentTypesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
            "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n" +
            "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n" +
            "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n" +
            "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n" +
            "  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n" +
            "</Types>";

        private static string GetRootRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
            "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n" +
            "</Relationships>";

        private static string GetWorkbookRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
            "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n" +
            "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n" +
            "</Relationships>";

        private static string GetWorkbookXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n" +
            "  <sheets>\n" +
            "    <sheet name=\"BOM_List\" sheetId=\"1\" r:id=\"rId1\"/>\n" +
            "  </sheets>\n" +
            "</workbook>";

        private static string GetStylesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n" +
            "  <fonts count=\"4\">\n" +
            "    <font><sz val=\"10\"/><color rgb=\"FF1F2937\"/><name val=\"Segoe UI\"/></font>\n" +
            "    <font><b/><sz val=\"16\"/><color rgb=\"FF1E3A8A\"/><name val=\"Segoe UI\"/></font>\n" +
            "    <font><i/><sz val=\"9\"/><color rgb=\"FF4B5563\"/><name val=\"Segoe UI\"/></font>\n" +
            "    <font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font>\n" +
            "  </fonts>\n" +
            "  <fills count=\"4\">\n" +
            "    <fill><patternFill patternType=\"none\"/></fill>\n" +
            "    <fill><patternFill patternType=\"gray125\"/></fill>\n" +
            "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1E3A8A\"/></patternFill></fill>\n" +
            "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF9FAFB\"/></patternFill></fill>\n" +
            "  </fills>\n" +
            "  <borders count=\"2\">\n" +
            "    <border><left/><right/><top/><bottom/></border>\n" +
            "    <border>\n" +
            "      <left style=\"thin\"><color rgb=\"FFD1D5DB\"/></left>\n" +
            "      <right style=\"thin\"><color rgb=\"FFD1D5DB\"/></right>\n" +
            "      <top style=\"thin\"><color rgb=\"FFD1D5DB\"/></top>\n" +
            "      <bottom style=\"thin\"><color rgb=\"FFD1D5DB\"/></bottom>\n" +
            "    </border>\n" +
            "  </borders>\n" +
            "  <cellStyleXfs count=\"1\">\n" +
            "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>\n" +
            "  </cellStyleXfs>\n" +
            "  <cellXfs count=\"6\">\n" +
            "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>\n" + // 0: default
            "    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\n" + // 1: Title
            "    <xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>\n" + // 2: Subtitle
            "    <xf numFmtId=\"0\" fontId=\"3\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>\n" + // 3: Header
            "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>\n" + // 4: Data Normal
            "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>\n" + // 5: Data Alt
            "  </cellXfs>\n" +
            "</styleSheet>";

        private static string GetSheetXml(IEnumerable<BOMItem> items, string assemblyTitle)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.AppendLine("  <cols>");
            sb.AppendLine("    <col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"2\" max=\"2\" width=\"35\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"3\" max=\"3\" width=\"10\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"4\" max=\"4\" width=\"18\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"5\" max=\"5\" width=\"18\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"6\" max=\"6\" width=\"25\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"7\" max=\"7\" width=\"10\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"8\" max=\"8\" width=\"25\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"9\" max=\"9\" width=\"30\" customWidth=\"1\"/>");
            sb.AppendLine("    <col min=\"10\" max=\"10\" width=\"25\" customWidth=\"1\"/>");
            sb.AppendLine("  </cols>");
            sb.AppendLine("  <sheetData>");

            // Row 1: Title
            sb.AppendLine("    <row r=\"1\" ht=\"38\" customHeight=\"1\">");
            sb.AppendLine($"      <c r=\"A1\" s=\"1\" t=\"inlineStr\"><is><t>BILL OF MATERIALS (BOM) - {EscapeXml(assemblyTitle)}</t></is></c>");
            sb.AppendLine("    </row>");

            // Row 2: Subtitle
            sb.AppendLine("    <row r=\"2\" ht=\"20\" customHeight=\"1\">");
            sb.AppendLine($"      <c r=\"A2\" s=\"2\" t=\"inlineStr\"><is><t>Generated by SolidWorks BOM Manager (.NET C#) | Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</t></is></c>");
            sb.AppendLine("    </row>");

            // Row 3: Empty
            sb.AppendLine("    <row r=\"3\" ht=\"10\" customHeight=\"1\"/>");

            // Row 4: Header
            sb.AppendLine("    <row r=\"4\" ht=\"26\" customHeight=\"1\">");
            string[] headers = { "No.", "Name of Part", "Q'TY", "Assy.", "Material", "Drawing No.", "Rev.", "설명충", "REMARK", "File Name" };
            string[] colLetters = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };
            for (int i = 0; i < headers.Length; i++)
            {
                sb.AppendLine($"      <c r=\"{colLetters[i]}4\" s=\"3\" t=\"inlineStr\"><is><t>{EscapeXml(headers[i])}</t></is></c>");
            }
            sb.AppendLine("    </row>");

            // Rows 5+: Data
            int rowIndex = 5;
            foreach (var item in items)
            {
                int styleId = (rowIndex % 2 == 1) ? 4 : 5;
                sb.AppendLine($"    <row r=\"{rowIndex}\" ht=\"22\" customHeight=\"1\">");
                sb.AppendLine($"      <c r=\"A{rowIndex}\" s=\"{styleId}\"><v>{item.ItemNo}</v></c>");
                sb.AppendLine($"      <c r=\"B{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.PartNameDisplay)}</t></is></c>");
                sb.AppendLine($"      <c r=\"C{rowIndex}\" s=\"{styleId}\"><v>{item.Qty}</v></c>");
                sb.AppendLine($"      <c r=\"D{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.AssyCategory)}</t></is></c>");
                sb.AppendLine($"      <c r=\"E{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.Material)}</t></is></c>");
                sb.AppendLine($"      <c r=\"F{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.DrawingNo)}</t></is></c>");
                sb.AppendLine($"      <c r=\"G{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.Rev)}</t></is></c>");
                sb.AppendLine($"      <c r=\"H{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.Explanation)}</t></is></c>");
                sb.AppendLine($"      <c r=\"I{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.Remark)}</t></is></c>");
                sb.AppendLine($"      <c r=\"J{rowIndex}\" s=\"{styleId}\" t=\"inlineStr\"><is><t>{EscapeXml(item.FileName)}</t></is></c>");
                sb.AppendLine("    </row>");
                rowIndex++;
            }

            sb.AppendLine("  </sheetData>");
            sb.AppendLine("  <mergeCells count=\"2\">");
            sb.AppendLine("    <mergeCell ref=\"A1:J1\"/>");
            sb.AppendLine("    <mergeCell ref=\"A2:J2\"/>");
            sb.AppendLine("  </mergeCells>");
            sb.AppendLine("</worksheet>");

            return sb.ToString();
        }
    }
}
