# ⚡ Design Automation Portal & BOM Manager V1.0 (100% Native C# .NET Standalone)

SolidWorks 2021의 활성 어셈블리(`.sldasm`) 연동 및 Standalone 도면/스프링 설계 자동화(Spring Designer, AutoCAD 연동)를 통합 제공하는 **Design Automation Portal** 솔루션입니다.

---

## 📌 주요 특징 및 기능

1. **100% C# (.NET Framework 4.8 + WPF) 순수 윈도우 스탠드얼론**:
   - Python 및 번거로운 추가 런타임 의존성 완전 배제
   - Visual Studio 솔루션(`BOMManager.sln`) 통합 빌드
   - **단일 인스턴스 보장(Single Instance Mutex)**: 중복 실행 방지 및 기존 창 자동 활성화
   - **SolidWorks & AutoCAD 연동**: 어셈블리 계층 BOM 관리 및 AutoCAD 스프링 도면 자동 생성 애드인 탑재

2. **BOM 매니저 핵심 기능**:
   - **계층 트리 & 인덴트**: 서브어셈블리 접기/펼치기 토글, 깔끔한 3칸 인덴트
   - **SolidWorks 화면 표시 제어**: 원클릭 불투명 강조(Isolate) & 투명화 복원
   - **도면번호 5색 컬러 포맷팅** 및 일괄 속성 변경(재질, 설명, 리비전, 비고)
   - **OpenXML 엑셀(.xlsx) & CSV 내보내기** 및 SolidWorks 모델 속성 양방향 반영

3. **스프링 설계 자동화(Spring Designer)**:
   - 규격/비규격 스프링 설계 파라미터 계산 및 AutoCAD 자동 도면화 연동

---

## 🚀 빌드, 배포 및 설치 / 언인스톨

### 1. 원클릭 빌드 & 패키징
[`build_all.bat`](file:///c:/Temp/BOM_Manager/build_all.bat)을 실행하면 다음 과정이 한 번에 처리됩니다:
1. `BOMManager.sln` 솔루션 릴리즈 빌드
2. 단위 테스트(Unit Tests) 자동 실행 및 검증
3. 실행 파일, 리소스, DLL, 애드인 번들링 (`dist_standalone/`)
4. 설치 스크립트(`Install_DT_Design.bat`) 및 언인스톨 스크립트(`Uninstall_DT_Design.bat`) 통합
5. 배포용 압축 패키지 생성 (`Dist/Design_Automation_Portal_Alpha_V0.3.zip`)

```bash
build_all.bat
```

### 2. 원키 설치 (Installation)
[`Dist\Install_DT_Design.bat`](file:///c:/Temp/BOM_Manager/Dist/Install_DT_Design.bat) (또는 루트 [`Install_DT_Design.bat`](file:///c:/Temp/BOM_Manager/Install_DT_Design.bat))를 실행합니다.
- 설치 대상 폴더: `C:\ISC_DT_Automation`
- 파일 및 리소스 자동 압축 해제 및 복사
- 바탕화면(일반/공용/OneDrive)에 `Design Automation Portal` 바로가기 자동 생성
- 언인스톨러(`Uninstall_DT_Design.bat`) 자동 배치

### 3. 언인스톨 (Uninstallation)
[`Uninstall_DT_Design.bat`](file:///c:/Temp/BOM_Manager/Uninstall_DT_Design.bat) (또는 `C:\ISC_DT_Automation\Uninstall_DT_Design.bat`)를 실행합니다.
- 실행 중인 프로그램 프로세스 자동 확인 및 종료
- 바탕화면 및 시작 메뉴 바로가기 아이콘 완전 삭제
- `C:\ISC_DT_Automation` 프로그램 설치 디렉토리 완전 삭제 (설치 폴더 내 직접 실행 시에도 안전 삭제 보장)
- 사용자 설정/캐시 데이터(`%APPDATA%\BOMManager`, `Common_Draw`) 선택 삭제 지원

---

## 📁 파일 및 디렉토리 구조

```
c:\Temp\BOM_Manager/
├── BOMManager.sln                  # Visual Studio 솔루션 파일
├── BOMManager.csproj               # 메인 WPF 애플리케이션 (.NET 4.8)
├── Design_Automation_Portal.exe    # 컴파일된 메인 포털 실행 파일
├── Install_DT_Design.bat           # 원키 설치 배치 파일
├── Uninstall_DT_Design.bat         # 원키 언인스톨 배치 파일
├── build_all.bat                   # 솔루션 빌드, 테스트, 배포 패키징 스크립트
├── Dist/                           # 최종 배포 패키지 디렉토리
│   ├── Design_Automation_Portal_Alpha_V0.1.zip
│   ├── Install_DT_Design.bat
│   └── Uninstall_DT_Design.bat
├── lib/                            # SolidWorks COM Interop 참조 라이브러리
├── resources/                      # 앱 아이콘, 폰트, 이미지 리소스
├── addin/                          # AutoCAD 연동용 애드인 DLL 및 설정
├── dist_standalone/                # 독립 실행 배포 파일 모음
├── Models/                         # BOM 및 어셈블리 데이터 모델
├── Modules/                        # Spring Designer 등 기능 모듈
├── Core/                           # SolidWorks 통신 코어 및 설정 관리자
├── UI/                             # WPF 사용자 인터페이스 및 다이얼로그
├── Utils/                          # Excel / CSV 내보내기 유틸리티
└── Tests/                          # 단위 테스트
```
