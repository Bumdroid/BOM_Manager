using System;

namespace BOMManager.Modules.SpringDesigner.Models
{
    /// <summary>
    /// 설계 입력 파라미터 및 공학 계산 관계식 모델 (스프링 + 소켓 탭 파라미터)
    /// </summary>
    public class SpringDesignParameters
    {
        // 템플릿 선택 (기본값: 구형 제작도면 템플릿 NEW_Template_제작도면_R01.dwg)
        public TemplateOption SelectedTemplate { get; set; } = TemplateOption.NewManufacturingR01;

        // 공통 좌표
        public double BaseX { get; set; } = 0.0;
        public double BaseY { get; set; } = 0.0;

        // 1. 선경 (Wire Diameter, d) mm (기본값: 1.1)
        public double WireDiameter { get; set; } = 1.1;

        // 3. 외경 (Outer Diameter, d2) mm (기본값: 6.0)
        public double OuterDiameter { get; set; } = 6.0;

        // 2. 내경 (Inner Diameter, Di) mm - 계산 프로퍼티: Di = Do - 2*d
        public double InnerDiameter => OuterDiameter - (WireDiameter * 2.0);

        // 4. 자유장 (Free Length, hs) mm (기본값: 8.6)
        public double FreeLength { get; set; } = 8.6;

        // 5. Min Device (P2h, mm) (기본값: 7.5)
        public double P2h { get; set; } = 7.5;

        // 6. 총권수 (Total Coils, Nf) (기본값: 5.0)
        public double TotalCoils { get; set; } = 5.0;

        // 7. 유효권수 (Active Coils, Na) - 연마 유무 상관없이 항상: 총권수 - 2.0
        public double ActiveCoils => TotalCoils - 2.0;

        // 8. 재료 (Material) - 기본값: SUS
        public SpringMaterial Material { get; set; } = SpringMaterial.SUS;

        // 9. 양끝 연마가공 옵션 (기본값: true 체크됨)
        public bool IsGrindingEnds { get; set; } = true;

        // --- 공용화 플래그 (맞춤 공용화 클릭 시 true, 수정/Fix/최적/Undo 시 false) ---
        public bool IsStandardized { get; set; } = false;
        public string MatchedDrawingNo { get; set; } = "";
        public string MatchedPartNumber { get; set; } = "";

        // --- 최적화용 Fix 체크박스 프로퍼티 ---
        public bool FixSprNum { get; set; } = false;
        public bool FixWireDiameter { get; set; } = false;
        public bool FixOuterDiameter { get; set; } = false;
        public bool FixFreeLength { get; set; } = false;
        public bool FixTotalCoils { get; set; } = true;
        public bool FixActiveCoils { get; set; } = true;
        public bool FixP2h { get; set; } = true;
        public bool FixMaterial { get; set; } = false;

        // PKG 수치 안전 변환 프로퍼티 (수식 계산용)
        public double PKGValue => double.TryParse(PKG, out double val) && val > 0 ? val : 1109.0;

        // --- Socket 탭 입력 파라미터 ---
        // 2행 Elastomer Force: EF_min, EF_nor, EF_max (기본값: 20 / 25 / 30)
        public double EF_min { get; set; } = 20.0;
        public double EF_nor { get; set; } = 25.0;
        public double EF_max { get; set; } = 30.0;

        // 3행 Elastomer Thickness: ET_min, ET_nor, ET_max (기본값: 0.67 / 0.70 / 0.78)
        public double ET_min { get; set; } = 0.67;
        public double ET_nor { get; set; } = 0.70;
        public double ET_max { get; set; } = 0.78;

        // 4행 PMD Thickness: PT_min, PT_nor, PT_max (기본값: 0.829 / 0.904 / 0.979)
        public double PT_min { get; set; } = 0.829;
        public double PT_nor { get; set; } = 0.904;
        public double PT_max { get; set; } = 0.979;

        // 네번째 표: PKG, SPR_num, LID_gap (기본값: 1109 / 28 / 1.0)
        public string PKG { get; set; } = "1109";
        public int SPR_num { get; set; } = 28;
        public double LID_gap { get; set; } = 1.0;

        // --- Socket 탭 공학 실시간 계산 공식 ---
        // 5행 Elastomer + PMD gap (mm)
        public double EP_min => 0.0;
        public double EP_nor => (ET_nor - ET_min) + (PT_nor - PT_min);
        public double EP_max => (ET_max - ET_min) + (PT_max - PT_min);

        // P1h = P2h - EP_nor
        public double P1h => P2h - EP_nor;

        // P1 = K * (hs - P1h)
        public double P1 => SpringConstantK * (FreeLength - P1h);

        // P3h = P2h - EP_max
        public double P3h => P2h - EP_max;

        // P3 = K * (hs - P3h)
        public double P3 => SpringConstantK * (FreeLength - P3h);

        // 6행 Spring Force (g)
        public double SF_min => P2 * 1000.0;
        public double SF_nor => P1 * 1000.0;
        public double SF_max => P3 * 1000.0;

        // 7행 Total Force (g)
        public double TF_min => SF_min * SPR_num;
        public double TF_nor => SF_nor * SPR_num;
        public double TF_max => SF_max * SPR_num;

        // 횡탄성계수 matK
        public double MatK
        {
            get
            {
                if (Material == SpringMaterial.MusicWire)
                {
                    return WireDiameter <= 0.06 ? 8500.0 : 8000.0;
                }
                else // SUS 및 기타
                {
                    return WireDiameter <= 0.06 ? 7500.0 : 7000.0;
                }
            }
        }

        // 인장강도 sten
        public double Sten
        {
            get
            {
                if (Material == SpringMaterial.MusicWire)
                {
                    return 270.0;
                }
                else // SUS 및 기타
                {
                    return 200.0;
                }
            }
        }

        // 중심경 dc = d2 - d
        public double Dc => OuterDiameter - WireDiameter;

        // SP 상수 K = matK * d^4 / (8 * dc^3 * Na)
        public double SpringConstantK
        {
            get
            {
                double d = WireDiameter;
                double dc = Dc;
                double na = ActiveCoils <= 0 ? 1.0 : ActiveCoils;
                if (dc <= 0) return 0.0;

                return (MatK * Math.Pow(d, 4.0)) / (8.0 * Math.Pow(dc, 3.0) * na);
            }
        }

        // P2 하중 P2 = K * (hs - P2h)
        public double P2 => SpringConstantK * (FreeLength - P2h);

        // 밀착고 FullComp = 연마 체크시: (d * Nf) + d - (d / 2), 미체크시: (d * Nf) + d
        public double FullComp
        {
            get
            {
                double d = WireDiameter;
                double nf = TotalCoils;
                if (IsGrindingEnds)
                {
                    return (d * nf) + d - (d / 2.0);
                }
                else
                {
                    return (d * nf) + d;
                }
            }
        }

        // 스프링 지수 spidx = dc / d
        public double SpIdx => WireDiameter <= 0 ? 0.0 : Dc / WireDiameter;

        // 응력 수정 계수 smodi = (4 * spidx - 1) / (4 * spidx - 4) + 0.615 / spidx
        public double SModi
        {
            get
            {
                double c = SpIdx;
                if (c <= 1.0) return 1.0;
                return ((4.0 * c - 1.0) / (4.0 * c - 4.0)) + (0.615 / c);
            }
        }

        // 비틀림 응력 stor = (smodi * 8 * dc * P2) / (pi * d^3)
        public double STor
        {
            get
            {
                double d = WireDiameter;
                if (d <= 0) return 0.0;
                return (SModi * 8.0 * Dc * P2) / (Math.PI * Math.Pow(d, 3.0));
            }
        }

        // 허용 응력 salo = stor / sten
        public double SAlo => Sten <= 0 ? 0.0 : STor / Sten;

        // 재료 표시용 텍스트 변환
        public string MaterialDisplayName => Material switch
        {
            SpringMaterial.SUS => "SUS",
            SpringMaterial.MusicWire => "Music wire",
            SpringMaterial.Brass => "황동",
            SpringMaterial.NickelSilver => "양백",
            SpringMaterial.PhosphorBronze => "인청동",
            _ => "Music wire"
        };
    }
}
