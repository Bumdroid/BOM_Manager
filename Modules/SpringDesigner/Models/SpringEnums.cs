using System;

namespace BOMManager.Modules.SpringDesigner.Models
{
    /// <summary>
    /// 스프링 재료 옵션
    /// </summary>
    public enum SpringMaterial
    {
        SUS,
        MusicWire,
        Brass,          // 황동
        NickelSilver,   // 양백
        PhosphorBronze  // 인청동
    }

    /// <summary>
    /// 자동 생성할 기계/구조 요소의 종류
    /// </summary>
    public enum ElementType
    {
        Plate,      // 평판 (플레이트)
        Pin,        // 핀 / 샤프트
        Spring      // 스프링 (기본)
    }

    /// <summary>
    /// 사내 템플릿 DWG 선택 옵션
    /// </summary>
    public enum TemplateOption
    {
        StandardRev00,      // 2D Template_Rev00.dwg (신형 템플릿)
        NewManufacturingR01 // NEW_Template_제작도면_R01.dwg (구형 제작도면 템플릿)
    }
}
