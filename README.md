# ⚡ SolidWorks 2021 - BOM Manager V0.0

SolidWorks 2021의 활성 어셈블리(Active Assembly, `.sldasm`) 문서와 실시간 연동하여 각 파트의 정보(Name of Part, Material, Q'TY, REMARK)를 확인, 편집하고 SolidWorks 모델 속성에 저장 및 Excel/CSV로 내보내는 자동화 소프트웨어입니다.

---

## 📌 주요 기능

1. **SolidWorks 2021 자동 연동**:
   - 실행 중인 SolidWorks 2021 인스턴스 자동 감지 및 연결
   - 현재 활성화된 어셈블리(`.sldasm`) 부품 목록 자동 추출
   - 최상위(Top-Level) 부품만 / 전체 하위 부품 포함 선택 가능
   - 동일 파트 수량(Q'TY) 자동 집계

2. **BOM 정보 인라인 편집**:
   - **No.** (순번, 수정 시 `*` 표시)
   - **Name of Part** (부품명 / 파트명)
   - **Material** (재질 - 주요 기계재료 드롭다운 콤보박스 선택 및 직접 입력)
   - **Q'TY** (수량 - 스핀박스 편집)
   - **REMARK** (비고 - 표면처리, 가공사양, 구매처 등 메모)
   - **File Name / Path** (파트 파일명 및 전체 경로 확인)

3. **작업 편의 도구**:
   - **검색 필터**: 부품명, 재질, 비고, 파일명 실시간 필터링
   - **재질 일괄 지정 (Batch Material)**: 다중 선택된 파트의 재질을 한 번에 변경
   - **비고 일괄 지정 (Batch Remark)**: 다중 선택된 파트의 비고를 한 번에 변경
   - **마우스 우클릭 메뉴**: 셀 복사, 해당 파트 폴더 열기, 원래 값으로 되돌리기

4. **SolidWorks 속성 저장 (Apply to SW)**:
   - 사용자가 수정한 `Name of Part`, `Material`, `Q'TY`, `REMARK`를 각 파트 파일의 **Custom Properties(사용자 정의 속성)**에 일괄 저장/동기화
   - SolidWorks 내장 재질 속성 동시 업데이트

5. **보고서 내보내기**:
   - **📊 Excel 내보내기 (.xlsx)**: 서식 및 헤더 스타일링이 적용된 완성형 엑셀 파일 생성
   - **📄 CSV 내보내기 (.csv)**: UTF-8 with BOM 인코딩으로 한글 깨짐 없는 CSV 생성

---

## 🚀 실행 및 상단 탭 메뉴 등록 방법

### 방법 1. SolidWorks 상단 탭 메뉴 자동 등록 (권장 ⭐)
1. [`register_addin.bat`](file:///c:/Temp/BOM_Manager/register_addin.bat) 파일을 한 번 더블 클릭하여 실행합니다. (이미 등록 완료됨)
2. **SolidWorks 2021을 실행**하면 상단 CommandManager에 **[BOM Manager]** 탭이 자동으로 생성되어 상주합니다.
3. 솔리드웍스 상단 탭의 **[BOM Manager V0.0]** 버튼을 클릭하면 팝업창이 즉시 뜹니다.
4. 상단 메뉴 바 **도구(Tools) -> BOM Manager V0.0** 메뉴를 통해서도 언제든 실행 가능합니다.
*(※ 등록을 해제하고 싶을 때는 [`unregister_addin.bat`](file:///c:/Temp/BOM_Manager/unregister_addin.bat)을 실행하시면 됩니다.)*

### 방법 2. 독립 실행 (배치 파일 / 커맨드라인)
- [`run_BOM_Manager.bat`](file:///c:/Temp/BOM_Manager/run_BOM_Manager.bat) 더블 클릭
- 또는 명령 프롬프트에서:
  ```bash
  python run.py
  python run.py --mock   # 데모/가상 데이터 테스트
  ```

---

## 📁 프로젝트 파일 구조

```
c:\Temp\BOM_Manager/
├── register_addin.bat         # ⭐ 솔리드웍스 상단 탭 메뉴 자동 등록 스크립트
├── unregister_addin.bat       # 솔리드웍스 상단 탭 메뉴 등록 해제 스크립트
├── run_BOM_Manager.bat        # 독립 실행형 런처
├── run.py                     # Python 프로그램 진입점
├── requirements.txt           # 의존 패키지 목록 (PySide6, pywin32, openpyxl, pandas)
├── README.md                  # 사용 설명서
├── addin/
│   ├── BOMManagerAddin.dll    # 솔리드웍스 상단 탭 연동 COM Add-in DLL
│   ├── SwAddin.cs             # SolidWorks Add-in C# 소스 코드
│   ├── build_addin.bat        # Add-in DLL 재컴파일 스크립트
│   └── Launch_BOM_Manager.vba # SolidWorks 매크로 런처 스크립트
├── core/
│   ├── bom_model.py           # BOM 데이터 모델 & 변경 추적
│   └── sw_connector.py        # SolidWorks 2021 COM API 연동 및 속성 입출력
├── ui/
│   ├── styles.py              # 모던 다크 테마 QSS 스타일시트 & 재질 목록
│   ├── custom_table.py        # BOM 데이터 테이블 뷰 & 인라인 편집 델리게이트
│   └── main_window.py         # 메인 팝업 윈도우 UI & 이벤트 처리
└── utils/
    └── exporter.py            # Excel / CSV 내보내기 유틸리티
```
