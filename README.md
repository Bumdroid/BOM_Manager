# ⚡ SolidWorks 2021 - BOM Manager V1.0 (100% Native C# .NET Standalone)

SolidWorks 2021의 활성 어셈블리(`.sldasm`) 또는 독립 실행형(Standalone) 어셈블리 직접 열기를 통해 실시간 연동하여 부품의 계층 트리 구조(서브어셈블리 `▶`/`▼` 토글 및 3칸 인덴트), 부품 정보(No., 화면표시, Name of Part, Q'TY, Drawing No. 컬러 세그먼트, Rev., 설명충 등)를 확인/편집하고 SolidWorks 모델 속성에 저장 및 Excel/CSV로 내보내는 100% 순수 C# (.NET Framework 4.8 / WPF) 윈도우 스탠드얼론 솔루션입니다.

---

## 📌 주요 특징 및 기능

1. **100% C# (.NET Framework 4.8 + WPF) 순수 윈도우 스탠드얼론**:
   - Python 및 Add-in COM 등록 의존성 완전 제거 (용량 80KB 초경량, 0.1초 즉시 실행)
   - Visual Studio 솔루션(`BOMManager.sln`) 통합 구조
   - **단일 인스턴스 보장(Single Instance Mutex)**: 중복 실행 시 기존 창 자동 포커스(Foreground Window)
   - **[📂 어셈블리 열기] 지원**: 독립 실행(Standalone) 상태에서 직접 `.sldasm`/`.sldprt` 파일을 선택하여 SolidWorks에 열고 즉시 동기화

2. **계층 구조 및 솔리드웍스 스타일 삼각형 트리 & 인덴트 규칙**:
   - **서브어셈블리 인덴트**: 2차 서브어셈블리 이상(`Level >= 2`)은 스페이스 3개 후 삼각형(`▶`/`▼`) 표시, `ㄴ` 모양 생략
   - **파트 인덴트**: `Level * 3`개의 공백 후 `└  ` 기호 적용
   - **[📂 전체 펼치기]** / **[📁 전체 접기]** 원클릭 지원

3. **SolidWorks 화면 표시 제어 (Isolate & Transparency)**:
   - **화면 표시 버튼 (🟢 불투명 강조 / 🔴 투명)**: 클릭 시 해당 부품만 SolidWorks 화면에 불투명 강조하고 나머지는 투명화 (좌우 잘림 방지 캡슐 UI)
   - **[🌐 전체 표시]**: 모든 부품을 불투명 상태로 즉시 복원

4. **도면번호(Drawing No.) & 설명충 컬럼 및 모드 전환**:
   - **[📐 도면번호 입력] 모드**: No. / 화면표시 / Name of Part / Q'TY / Drawing No. / Rev. / 설명충 표시
   - **[📋 Summary] 모드**: 첫 화면으로 복귀하며 전체 속성 표시 및 Rev. 수정 방지(Read-Only)
   - **Drawing No. 자동 하이픈 제외 & 5색 컬러 포맷**:
     - 사용자 입력 시 `-`는 자동 제거
     - 입력 완료 시 `OOO-PPPPPGBBBXXXX` 형식으로 5색 분할 표시 (O: 빨간색, P: 진노랑, G: 초록색, B: 파란색, X: 보라색)
     - 수정 더블클릭 시에는 원본 텍스트로 편리하게 편집

5. **완성형 보고서 내보내기 & SolidWorks 동기화**:
   - **[💾 SW에 적용]**: 수정한 부품명, 수량, 도면번호, 설명충, 리비전 등을 SolidWorks 파일 사용자 정의 속성(Custom Properties)에 즉시 저장
   - **📊 Excel 내보내기 (.xlsx)**: 자체 OpenXML 엔진으로 Drawing No. 및 설명충 포함 네이비 테마 스타일링 내보내기
   - **📄 CSV 내보내기 (.csv)**: UTF-8 with BOM 인코딩으로 엑셀 한글 깨짐 방지

---

## 🚀 빌드 및 실행 방법

### 1. 원클릭 빌드 & 테스트 & 패키징
[`build_all.bat`](file:///c:/Temp/BOM_Manager/build_all.bat)을 실행하면 다음 작업이 자동으로 완료됩니다:
```bash
# Visual Studio 솔루션 전체 빌드
dotnet build BOMManager.sln -c Release

# 단위 테스트 6종 전체 자동 검증
dotnet run --project tests/BOMManagerTests.csproj -c Release

# 독립 배포 패키지 구성 (dist_standalone/)
```

### 2. 스탠드얼론 실행
- **실행**: `BOM_Manager.exe` 더블 클릭 (또는 `run_BOM_Manager.bat` 실행)
- **데모/가상 데이터 테스트**:
  ```bash
  BOM_Manager.exe --mock
  ```

---

## 📁 100% C# 솔루션 파일 구조

```
c:\Temp\BOM_Manager/
├── BOMManager.sln             # Visual Studio 솔루션 파일
├── BOMManager.csproj          # 메인 WPF 애플리케이션 (.NET 4.8)
├── BOM_Manager.exe            # 컴파일된 초경량 네이티브 실행 파일
├── App.xaml / App.xaml.cs     # WPF 애플리케이션 진입점 & 단일 인스턴스 Mutex
├── build_all.bat              # 솔루션 빌드, 테스트, 배포 패키징 스크립트
├── run_BOM_Manager.bat        # 런처 배치 파일
├── lib/                       # SolidWorks COM Interop 참조 라이브러리
│   ├── SolidWorks.Interop.sldworks.dll
│   ├── SolidWorks.Interop.swconst.dll
│   └── SolidWorks.Interop.swpublished.dll
├── dist_standalone/           # 독립 실행 배포 폴더 (실행 파일 및 필수 DLL)
│
├── Models/                    # 데이터 모델
│   ├── BOMItem.cs             # BOM 모델, 변경 감지, Drawing No. 컬러 파싱, 설명충
│   └── AssemblyInfo.cs        # SolidWorks 활성 어셈블리 메타데이터
│
├── Core/                      # SolidWorks 통신 코어
│   ├── ISolidWorksService.cs  # SolidWorks 서비스 인터페이스 (OpenDocument 포함)
│   ├── SwConnector.cs         # SolidWorks COM API 연동, Isolate, 속성 I/O
│   └── MockSwConnector.cs     # 가상 데이터 테스트 커넥터
│
├── UI/                        # WPF 사용자 인터페이스
│   ├── MainWindow.xaml / .cs  # 메인 UI, 도면번호/Summary 뷰 모드, DataGrid 이벤트
│   ├── Converters/            # 바인딩 컨버터 (Drawing No. 컬러 파싱, 앰버 하이라이트)
│   └── Dialogs/               # 일괄 변경 모달 다이얼로그 (재질, 비고, 설명충)
│
├── Utils/                     # 유틸리티
│   └── BomExporter.cs         # 순수 OpenXML Excel(.xlsx) & UTF-8 BOM CSV 내보내기
│
└── Tests/                     # 단위 및 기능 테스트 (6종)
    ├── BOMManagerTests.csproj # 테스트 프로젝트 파일
    └── TestRunner.cs          # 인덴트, 도면번호/설명충, OpenXML, Standalone 열기 검증
```
