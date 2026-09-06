# ⚡ SolidWorks 2021 - BOM Manager V0.0 (100% Native C# .NET)

SolidWorks 2021의 활성 어셈블리(Active Assembly, `.sldasm`) 문서와 실시간 연동하여 각 파트의 계층 트리 구조(Subassembly `▼`/`▶` 토글 및 `3 * Level` 공백 인덴트), 정보(Name of Part, Material, Q'TY, Rev., REMARK, Common Part)를 확인, 인라인 편집하고 SolidWorks 모델 속성에 저장 및 Excel/CSV로 내보내는 100% 네이티브 C# (.NET Framework 4.8 / WPF) 자동화 솔루션입니다.

---

## 📌 주요 특징 및 기능

1. **100% C# (.NET Framework 4.8 + WPF) 순수 네이티브 구축**:
   - Python / PySide6 / PyInstaller 의존성 완전 제거 (용량 70MB -> 80KB 초경량화, 0.1초 즉시 실행)
   - SolidWorks 2021 COM Interop API 직접 바인딩

2. **계층 구조 및 솔리드웍스 스타일 삼각형 트리 토글**:
   - **서브어셈블리 삼각형 토글 (`▼` / `▶`)**: 펼침 시 아래 방향(`▼`), 접힘 시 오른쪽 방향(`▶`)
   - **계층 인덴트 규칙**: `Level * 3`개의 공백 후 `└  ` 기호 적용 (Level 0: 인덴트 없음, Level 1: 3개 `"   └  "`, Level 2: 6개 `"      └  "`)
   - **[📂 전체 펼치기]** / **[📁 전체 접기]** 원클릭 지원

3. **SolidWorks 화면 표시 제어 (Isolate & Transparency)**:
   - **화면 표시 버튼 (🟢 불투명 / 🔴 투명)**: 특정 부품 클릭 시 해당 부품만 화면에 불투명 강조하고 나머지는 투명화
   - **[🌐 전체 표시]**: 모든 부품을 불투명 상태로 즉시 복원

4. **BOM 정보 인라인 편집 & 실시간 변경 추적 (Amber Highlight)**:
   - **No.** (순번, 수정 시 `*` 표시 및 배경 강조)
   - **Common Part** (공용품 체크박스 - 셀 정중앙 정렬)
   - **Name of Part** (부품명 - 더블 클릭 편집 시 순수 파트명만 표시)
   - **Material** (재질 - 기계재료 콤보박스 선택 및 직접 입력)
   - **Q'TY** (수량 - 정수 편집 >= 1)
   - **Rev.** (리비전 관리)
   - **REMARK** (비고 - 표면처리, 가공사양, 구매처 등 메모)
   - 수정된 셀은 따뜻한 앰버 옐로우 (`#FEF3C7`) 배경과 딥 앰버 (`#B45309`) 볼드 텍스트로 실시간 강조

5. **편의 도구 및 일괄 변경 (Batch Tools)**:
   - **실시간 검색 필터**: 부품명, 재질, 리비전, 비고 즉시 검색
   - **[☑️ 공용품 일괄 전환]**: 선택 행 공용품 일괄 토글
   - **[🏷️ 재질 일괄 지정]**: 다중 선택된 파트의 재질 일괄 변경
   - **[📝 비고 일괄 지정]**: 다중 선택된 파트의 비고 일괄 변경
   - **마우스 우클릭 메뉴**: Isolate, 트리 토글, 셀 복사, 탐색기에서 파일 열기, 원래 값 되돌리기(Reset)

6. **SolidWorks 속성 저장 (Apply to SW)**:
   - 수정한 정보를 각 파트 파일의 **Custom Properties(사용자 정의 속성)**에 직접 저장/동기화

7. **완성형 보고서 내보내기**:
   - **📊 Excel 내보내기 (.xlsx)**: 자체 구현된 OpenXML 엔진으로 외부 라이브러리 없이 네이비 블루 테마 스타일링 적용
   - **📄 CSV 내보내기 (.csv)**: UTF-8 with BOM 인코딩으로 한글 깨짐 없는 CSV 생성

---

## 🚀 실행 및 빌드 방법

### 1. 원클릭 빌드 & 테스트
[`build_all.bat`](file:///c:/Temp/BOM_Manager/build_all.bat)을 더블 클릭하면 다음 작업이 자동으로 완료됩니다:
```bash
# 1. WPF 메인 애플리케이션 컴파일 (BOM_Manager.exe)
dotnet build BOMManager.csproj -c Release

# 2. SolidWorks COM Add-in DLL 컴파일 (SolidWorksMLAddin.dll)
dotnet build addin/BOMManagerAddin.csproj -c Release

# 3. 단위 테스트 검증
dotnet run --project tests/BOMManagerTests.csproj
```

### 2. SolidWorks 상단 탭 등록 (권장 ⭐)
1. [`register_addin.bat`](file:///c:/Temp/BOM_Manager/register_addin.bat) 실행
2. SolidWorks 2021 실행 시 상단에 **[BOM Manager]** 탭 자동 상주 및 클릭 즉시 실행

### 3. 독립 실행
- [`run_BOM_Manager.bat`](file:///c:/Temp/BOM_Manager/run_BOM_Manager.bat) 더블 클릭
- 데모/가상 데이터 테스트:
  ```bash
  BOM_Manager.exe --mock
  ```

---

## 📁 100% C# 솔루션 파일 구조

```
c:\Temp\BOM_Manager/
├── BOMManager.csproj          # 메인 WPF 애플리케이션 프로젝트 파일 (.NET 4.8)
├── BOM_Manager.exe            # 컴파일된 초경량 네이티브 실행 파일
├── App.xaml / App.xaml.cs     # WPF 애플리케이션 진입점 및 글로벌 스타일
├── build_all.bat              # 원클릭 전체 빌드 & 단위 테스트 스크립트
├── register_addin.bat         # 솔리드웍스 상단 탭 Add-in 등록
├── unregister_addin.bat       # 솔리드웍스 상단 탭 Add-in 등록 해제
├── run_BOM_Manager.bat        # 런처 배치 파일
├── README.md                  # 설명서
│
├── Models/                    # 데이터 모델
│   ├── BOMItem.cs             # INotifyPropertyChanged 기반 BOM 모델 & 변경 감지
│   └── AssemblyInfo.cs        # SolidWorks 활성 어셈블리 메타데이터
│
├── Core/                      # SolidWorks 통신 코어
│   ├── ISolidWorksService.cs  # SolidWorks 서비스 인터페이스
│   ├── SwConnector.cs         # SolidWorks 2021 COM API 연동 및 속성 입출력
│   └── MockSwConnector.cs     # 가상 데이터 테스트 커넥터
│
├── UI/                        # WPF 사용자 인터페이스
│   ├── MainWindow.xaml / .cs  # 메인 윈도우 UI 및 DataGrid 이벤트 처리
│   ├── Converters/            # 바인딩 컨버터 (트리 토글, 앰버 하이라이트 등)
│   └── Dialogs/               # 일괄 변경 모달 다이얼로그 (재질, 비고)
│
├── Utils/                     # 유틸리티
│   └── BomExporter.cs         # 순수 .NET OpenXML Excel(.xlsx) & CSV 내보내기
│
├── Addin/                     # SolidWorks COM Add-in
│   ├── BOMManagerAddin.csproj # Add-in DLL 프로젝트 파일
│   ├── SwAddin.cs             # ISwAddin COM 구현체
│   └── SolidWorksMLAddin.dll  # 컴파일된 Add-in COM DLL
│
└── Tests/                     # 단위 테스트
    ├── BOMManagerTests.csproj # 테스트 프로젝트 파일
    └── TestRunner.cs          # 인덴트 규칙, 변경 감지, Export 테스트 러너
```
