using System;

namespace BOMManager.Modules.SpringDesigner.Models
{
    /// <summary>
    /// Vault 공용화 CSV 파일에서 파싱된 스프링 단품 레코드
    /// </summary>
    public class SpringCsvRecord
    {
        public string PartNumber { get; set; } = "";
        public string DrawingNo { get; set; } = "";
        public double WireDiameter { get; set; } = 0.0;
        public double OuterDiameter { get; set; } = 0.0;
        public double InnerDiameter { get; set; } = 0.0;
        public double FreeLength { get; set; } = 0.0;
        public double P2h { get; set; } = 0.0;
        public double TotalCoils { get; set; } = 0.0;
        public double ActiveCoils { get; set; } = 0.0;
        public SpringMaterial Material { get; set; } = SpringMaterial.MusicWire;
        public string[] RawRow { get; set; } = new string[0];
    }

    /// <summary>
    /// 공용화 후보 비교 팝업창 바인딩 모델
    /// </summary>
    public class StandardizationCandidateModel
    {
        public string PartNumber { get; set; } = "";
        public string DrawingNo { get; set; } = "";
        public double WireDiameter { get; set; }
        public double OuterDiameter { get; set; }
        public double InnerDiameter => OuterDiameter - (WireDiameter * 2.0);
        public double FreeLength { get; set; }
        public double P2h { get; set; }
        public double TotalCoils { get; set; }
        public double ActiveCoils { get; set; }
        public SpringMaterial Material { get; set; }
        public string MaterialName { get; set; } = "";
        public int RecommendedSprNum { get; set; }
        public bool IsRecommended { get; set; }
        public string RankLabel { get; set; } = "";
        public SpringCsvRecord OriginalRecord { get; set; } = new SpringCsvRecord();

        public string DisplaySummary => string.Format("{0} ({1}, d={2:F2}, Do={3:F2}, Hs={4:F2}, Nf={5:F1})", DrawingNo, MaterialName, WireDiameter, OuterDiameter, FreeLength, TotalCoils);
    }
}
