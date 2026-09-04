import os
import sys
from typing import List, Tuple, Optional, Dict
from .bom_model import BOMItem, AssemblyInfo

# SolidWorks Document Type Constants
SW_DOC_NONE = 0
SW_DOC_PART = 1
SW_DOC_ASSEMBLY = 2
SW_DOC_DRAWING = 3

# SolidWorks Custom Property Type Constants (swCustomInfoType_e)
SW_CUSTOM_INFO_UNKNOWN = 0
SW_CUSTOM_INFO_TEXT = 30
SW_CUSTOM_INFO_DATE = 31
SW_CUSTOM_INFO_NUMBER = 3
SW_CUSTOM_INFO_YES_OR_NO = 11

# SolidWorks Custom Property Add Options (swCustomPropertyAddOption_e)
SW_CUSTOM_PROP_REPLACE_VALUE = 1
SW_CUSTOM_PROP_DELETE_AND_ADD = 2
SW_CUSTOM_PROP_ONLY_IF_NEW = 0

def _safe_call(obj, name: str, default=None, *args):
    """COM 객체의 속성(Property) 또는 메서드(Method)를 안전하게 호출/조회"""
    if obj is None:
        return default
    try:
        attr = getattr(obj, name, default)
        if callable(attr):
            return attr(*args)
        return attr
    except Exception:
        return default


def _extract_leaf_name(comp_name: str) -> str:
    """SolidWorks 컴포넌트 전체 인스턴스 경로에서 최종 말단(Leaf) 파트명만 순수하게 추출"""
    if not comp_name:
        return ""
    # 1. '/' 슬래시 경로 구분 (예: 'SubAssy-1/ChildAssy-1/Part-1' -> 'Part-1')
    if '/' in comp_name:
        comp_name = comp_name.split('/')[-1]
    # 2. '@' 골뱅이 상위 어셈블리 표기 (예: 'Part-1@SubAssy-1' -> 'Part-1')
    if '@' in comp_name:
        comp_name = comp_name.split('@')[0]
    # 3. '-' 인스턴스 번호 제거 (예: 'Part-1' -> 'Part')
    comp_name = comp_name.split('-')[0]
    return comp_name.strip().lower()


class SolidWorksConnector:
    """SolidWorks 2021 COM API 연동 및 데이터 추출/반영 클래스"""

    def __init__(self, mock_mode: bool = False):
        self.mock_mode = mock_mode
        self.sw_app = None
        self._connected = False
        self._cached_comp_records: List[dict] = []
        self._cached_doc_path: str = ""

    def connect(self) -> Tuple[bool, str]:
        """SolidWorks COM 객체에 연결 시도"""
        if self.mock_mode:
            self._connected = True
            return True, "Mock Mode 활성화됨 (가상 SolidWorks 2021 환경)"

        try:
            import win32com.client
            import subprocess

            # 1. 실행 중인 SldWorks COM 객체 탐색 (ProgID 후보 순회)
            progids = ["SldWorks.Application", "SldWorks.Application.29", "SldWorks.Application.28"]
            for progid in progids:
                try:
                    self.sw_app = win32com.client.GetActiveObject(progid)
                    if self.sw_app is not None:
                        self._connected = True
                        try:
                            version = _safe_call(self.sw_app, "RevisionNumber", "2021")
                        except Exception:
                            version = "2021"
                        return True, f"SolidWorks 연결 성공 (SW Version: {version})"
                except Exception:
                    continue

            # 2. GetActiveObject 실패 시 실행 중인 프로세스 검사
            try:
                tasklist = subprocess.check_output('tasklist /FI "IMAGENAME eq SLDWORKS.exe"', shell=True, text=True)
                if "SLDWORKS.exe" in tasklist:
                    return False, "SolidWorks가 실행 중이지만 아직 준비 중이거나 권한 격리(UAC) 상태입니다. 잠시 후 [새로고침]을 누르거나 문서를 열어주세요."
            except Exception:
                pass

            return False, "SolidWorks 2021이 실행되어 있지 않습니다. SolidWorks를 먼저 실행해 주세요."
        except ImportError:
            return False, "pywin32 패키지가 설치되지 않았습니다."
        except Exception as e:
            return False, f"SolidWorks 연결 실패: {str(e)}"

    def get_active_assembly_info(self) -> AssemblyInfo:
        """현재 활성화된 어셈블리 문서 정보 확인"""
        if self.mock_mode:
            return AssemblyInfo(
                title="PUMP_UNIT_ASSY.SLDASM",
                path=r"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM",
                active_configuration="Default",
                total_components_count=18,
                unique_parts_count=6,
                is_connected=True
            )

        if not self._connected or not self.sw_app:
            connected, msg = self.connect()
            if not connected:
                return AssemblyInfo(is_connected=False, error_message=msg)

        try:
            model = getattr(self.sw_app, "ActiveDoc", None)
            if model is None:
                return AssemblyInfo(is_connected=True, error_message="열려 있는 SolidWorks 문서가 없습니다.")

            doc_type = _safe_call(model, "GetType", SW_DOC_NONE)
            title = _safe_call(model, "GetTitle", "Untitled")
            path = _safe_call(model, "GetPathName", "")

            if doc_type != SW_DOC_ASSEMBLY:
                type_name = "파트(.SLDPRT)" if doc_type == SW_DOC_PART else ("도면(.SLDDRW)" if doc_type == SW_DOC_DRAWING else "기타")
                return AssemblyInfo(
                    title=title,
                    path=path,
                    is_connected=True,
                    error_message=f"현재 문서는 어셈블리가 아닌 '{type_name}' 문서입니다. 어셈블리(.sldasm)를 활성화해 주세요."
                )

            active_conf_name = "Default"
            try:
                conf = _safe_call(model, "GetActiveConfiguration")
                if conf:
                    active_conf_name = getattr(conf, "Name", "Default")
            except Exception:
                try:
                    cfg_mgr = getattr(model, "ConfigurationManager", None)
                    if cfg_mgr:
                        active_conf_name = cfg_mgr.ActiveConfiguration.Name
                except Exception:
                    pass

            return AssemblyInfo(
                title=title,
                path=path,
                active_configuration=active_conf_name,
                is_connected=True
            )
        except Exception as e:
            return AssemblyInfo(is_connected=False, error_message=f"문서 정보 확인 중 오류: {str(e)}")

    def load_bom(self, top_level_only: bool = False, include_suppressed: bool = False) -> Tuple[List[BOMItem], Optional[str]]:
        """활성 어셈블리에서 각 파트의 정보(Name of Part, Material, Q'TY, REMARK 등) 추출"""
        if self.mock_mode:
            return self._get_mock_bom_items(), None

        if not self._connected:
            connected, msg = self.connect()
            if not connected:
                return [], msg

        try:
            model = getattr(self.sw_app, "ActiveDoc", None)
            if model is None or _safe_call(model, "GetType", SW_DOC_NONE) != SW_DOC_ASSEMBLY:
                return [], "어셈블리 문서가 활성화되어 있지 않습니다."

            # SolidWorks AssemblyDoc 인터페이스
            sw_assy = model
            components = _safe_call(sw_assy, "GetComponents", None, top_level_only)

            if not components:
                self._cached_components = []
                return [], "어셈블리에 포함된 컴포넌트가 없습니다."

            # 첫 클릭 지연 제거를 위해 컴포넌트 리스트 및 메타데이터 캐싱
            self._cached_components = [c for c in components if c is not None]
            self._cached_doc_path = os.path.normpath(_safe_call(model, "GetPathName", "") or "").lower()
            self._cached_comp_records = []

            # 파트 파일 경로 + 컨피규레이션별 집계 딕셔너리
            # key: (normalized_file_path, config_name)
            # value: {"count": int, "comp": IComponent2, "model": ModelDoc2}
            part_map: Dict[Tuple[str, str], dict] = {}

            for comp in components:
                if comp is None:
                    continue

                is_suppressed = _safe_call(comp, "IsSuppressed", False)
                if not include_suppressed and is_suppressed:
                    continue

                # 봉투 부품(Envelope) 여부 체크 (BOM 제외)
                is_envelope = _safe_call(comp, "IsEnvelope", False)
                if is_envelope:
                    continue

                file_path = _safe_call(comp, "GetPathName", "")
                if not file_path:
                    continue

                norm_path = os.path.normpath(file_path).lower()
                is_sub = norm_path.endswith(".sldasm")
                comp_name = _safe_call(comp, "Name2", "") or ""
                fname = os.path.basename(norm_path).lower()
                clean_name = _extract_leaf_name(comp_name)

                # 서브어셈블리 계층 깊이(level) 및 상위 어셈블리 경로/명칭 정밀 추적
                # 1. SolidWorks Name2 인스턴스 경로 분석 (가장 신뢰성 높음: 'Part-1@SubAssy-1' -> @ 개수 또는 / 개수)
                level = 0
                if comp_name:
                    at_cnt = comp_name.count('@')
                    slash_cnt = comp_name.count('/')
                    level = max(at_cnt, slash_cnt)

                # 2. GetParent() COM 호출 및 상위 경로 수집
                parent_paths = []
                parent_names = []
                try:
                    p = _safe_call(comp, "GetParent")
                    p_depth = 0
                    while p is not None:
                        p_depth += 1
                        p_path = _safe_call(p, "GetPathName", "")
                        if p_path:
                            parent_paths.append(os.path.normpath(p_path).lower())
                        p_name = _safe_call(p, "Name2", "")
                        if p_name:
                            parent_names.append(p_name.lower().strip())
                        p = _safe_call(p, "GetParent")
                    level = max(level, p_depth)
                except Exception:
                    pass

                # Name2에서 상위 서브어셈블리 명칭 추출하여 parent_names에 추가
                if '@' in comp_name:
                    for seg in comp_name.split('@')[1:]:
                        s_clean = seg.split('/')[0].strip().lower()
                        if s_clean and s_clean not in parent_names:
                            parent_names.append(s_clean)
                            base_s = s_clean.split('-')[0]
                            if base_s and base_s not in parent_names:
                                parent_names.append(base_s)
                elif '/' in comp_name:
                    for seg in comp_name.split('/')[:-1]:
                        s_clean = seg.split('@')[0].strip().lower()
                        if s_clean and s_clean not in parent_names:
                            parent_names.append(s_clean)
                            base_s = s_clean.split('-')[0]
                            if base_s and base_s not in parent_names:
                                parent_names.append(base_s)

                # 화면표시(투명/불투명) 초고속 처리를 위한 메타데이터 사전 캐싱 (클릭 시 COM 통신 0회 달성)
                self._cached_comp_records.append({
                    "comp": comp,
                    "path": norm_path,
                    "is_sub": is_sub,
                    "name": comp_name.lower().strip(),
                    "fname": fname,
                    "clean_name": clean_name,
                    "level": level,
                    "parent_paths": parent_paths,
                    "parent_names": parent_names
                })

                # 파트 파일 경로 + 컨피규레이션별 집계
                config_name = _safe_call(comp, "ReferencedConfiguration", "Default") or "Default"
                key = (norm_path, str(config_name).lower())

                is_virtual = _safe_call(comp, "IsVirtual", False)

                if key not in part_map:
                    part_map[key] = {
                        "count": 1,
                        "comp": comp,
                        "file_path": file_path,
                        "config_name": str(config_name),
                        "component_names": [comp_name] if comp_name else [],
                        "is_virtual": bool(is_virtual),
                        "is_suppressed": bool(is_suppressed),
                        "is_subassembly": bool(is_sub),
                        "level": level
                    }
                else:
                    part_map[key]["count"] += 1
                    if comp_name:
                        part_map[key]["component_names"].append(comp_name)
                    # 하위 부품 계층 유지: 서브어셈블리 내부에 포함된 부품이면 level 반영
                    if level > part_map[key]["level"]:
                        part_map[key]["level"] = level

            bom_items: List[BOMItem] = []
            item_no = 1

            for key, data in part_map.items():
                comp = data["comp"]
                file_path = data["file_path"]
                config_name = data["config_name"]
                qty = data["count"]

                # 파트명 기본값: 파일명에서 확장자 제거
                base_file_name = os.path.basename(file_path)
                default_part_name, _ = os.path.splitext(base_file_name)

                part_name = default_part_name
                material = ""
                rev = ""
                remark = ""
                is_common = False
                custom_props = {}

                # 파트의 ModelDoc2 접근 시도
                part_model = _safe_call(comp, "GetModelDoc2")

                if part_model:
                    # 1. SolidWorks 내장 재질 확인
                    try:
                        part_doc = part_model
                        sw_part = part_doc
                        mat_name = sw_part.GetMaterialPropertyName2(config_name, "")
                        if mat_name:
                            material = mat_name
                    except Exception:
                        pass

                    # 2. Custom Properties (설정별 및 전역 속성 읽기)
                    try:
                        ext = part_model.Extension
                        cfg_prop_mgr = ext.CustomPropertyManager(config_name)
                        gen_prop_mgr = ext.CustomPropertyManager("")

                        def read_prop(name_candidates: List[str]) -> str:
                            for c in name_candidates:
                                if cfg_prop_mgr:
                                    res, val, eval_val = cfg_prop_mgr.Get5(c, False, "", "", False)
                                    if res == 1 or res == 2:
                                        if eval_val:
                                            return eval_val
                                        elif val:
                                            return val
                                if gen_prop_mgr:
                                    res, val, eval_val = gen_prop_mgr.Get5(c, False, "", "", False)
                                    if res == 1 or res == 2:
                                        if eval_val:
                                            return eval_val
                                        elif val:
                                            return val
                            return ""

                        p_name = read_prop(["Name of Part", "Part Name", "PartName", "품명", "부품명", "Description"])
                        if p_name:
                            part_name = p_name

                        if not material:
                            p_mat = read_prop(["Material", "재질", "MATERIAL", "Mat"])
                            if p_mat:
                                material = p_mat

                        p_rev = read_prop(["Rev.", "Rev", "Revision", "리비전", "도면버전", "REV"])
                        if p_rev:
                            rev = p_rev

                        p_rem = read_prop(["REMARK", "Remark", "비고", "Comment", "Note", "비고사항"])
                        if p_rem:
                            remark = p_rem

                        p_com = read_prop(["Common Part", "CommonPart", "공용품", "공용부품", "표준품", "구매품"])
                        is_common = p_com.lower() in ["yes", "true", "1", "y", "예", "공용", "o", "v"]

                    except Exception as e:
                        print(f"속성 읽기 중 예외 ({base_file_name}): {e}", file=sys.stderr)

                item = BOMItem(
                    item_no=item_no,
                    is_common_part=is_common,
                    part_name=part_name,
                    material=material,
                    qty=qty,
                    rev=rev,
                    remark=remark,
                    file_name=base_file_name,
                    file_path=file_path,
                    configuration=config_name,
                    component_names=data.get("component_names", []),
                    custom_properties=custom_props,
                    is_suppressed=data["is_suppressed"],
                    is_virtual=data["is_virtual"],
                    is_subassembly=data.get("is_subassembly", False),
                    level=data.get("level", 0)
                )
                bom_items.append(item)
                item_no += 1

            return bom_items, None

        except Exception as e:
            return [], f"BOM 추출 중 오류 발생: {str(e)}"

    def apply_properties_to_solidworks(self, bom_items: List[BOMItem]) -> Tuple[int, int, List[str]]:
        """
        사용자가 수정한 속성(Name of Part, Material, Q'TY, Rev., REMARK, Common Part 등)을
        SolidWorks 각 파트의 Custom Property에 일괄 반영
        Returns: (성공 개수, 실패 개수, 에러 메시지 목록)
        """
        if self.mock_mode:
            # Mock mode 시뮬레이션
            success_count = len(bom_items)
            for item in bom_items:
                item.original_is_common_part = item.is_common_part
                item.original_part_name = item.part_name
                item.original_material = item.material
                item.original_qty = item.qty
                item.original_rev = item.rev
                item.original_remark = item.remark
                item.is_modified = False
            return success_count, 0, []

        if not self._connected or not self.sw_app:
            return 0, len(bom_items), ["SolidWorks에 연결되어 있지 않습니다."]

        success_count = 0
        fail_count = 0
        errors = []

        try:
            active_doc = self.sw_app.ActiveDoc

            for item in bom_items:
                try:
                    file_path = item.file_path
                    if not file_path or not os.path.exists(file_path):
                        # 가상 부품이거나 경로가 없을 경우
                        errors.append(f"[{item.part_name}] 파일 경로를 찾을 수 없습니다: {file_path}")
                        fail_count += 1
                        continue

                    # 파트 문서 열기 / 가져오기
                    doc_spec = self.sw_app.GetOpenDocument(file_path)
                    part_model = doc_spec

                    if part_model is None:
                        # 메모리에 열려있지 않으면 백그라운드로 열기 (1=Silent)
                        part_model = self.sw_app.OpenDoc6(
                            file_path,
                            SW_DOC_PART if file_path.lower().endswith(".sldprt") else SW_DOC_ASSEMBLY,
                            1, # swOpenDocOptions_Silent
                            item.configuration,
                            0, 0
                        )

                    if part_model is None:
                        errors.append(f"[{item.part_name}] 모델 문서를 열 수 없습니다.")
                        fail_count += 1
                        continue

                    ext = part_model.Extension
                    # 설정별 속성 매니저 및 전역 속성 매니저
                    prop_mgr_cfg = ext.CustomPropertyManager(item.configuration)
                    prop_mgr_gen = ext.CustomPropertyManager("")

                    # 속성 쓰기 함수 (기존 값이 있으면 Set2, 없으면 Add3)
                    def set_or_add_prop(prop_name: str, prop_val: str):
                        # 1. 설정별 속성 매니저에 쓰기
                        if prop_mgr_cfg:
                            res = prop_mgr_cfg.Set2(prop_name, prop_val)
                            if res != 0:
                                prop_mgr_cfg.Add3(prop_name, SW_CUSTOM_INFO_TEXT, prop_val, SW_CUSTOM_PROP_REPLACE_VALUE)
                        # 2. 전역 속성 매니저에도 동기화
                        if prop_mgr_gen:
                            res = prop_mgr_gen.Set2(prop_name, prop_val)
                            if res != 0:
                                prop_mgr_gen.Add3(prop_name, SW_CUSTOM_INFO_TEXT, prop_val, SW_CUSTOM_PROP_REPLACE_VALUE)

                    # 속성 일괄 반영
                    set_or_add_prop("Name of Part", item.part_name)
                    set_or_add_prop("Material", item.material)
                    set_or_add_prop("Q'TY", str(item.qty))
                    set_or_add_prop("Rev.", item.rev)
                    set_or_add_prop("REMARK", item.remark)
                    set_or_add_prop("Common Part", "Yes" if item.is_common_part else "No")

                    # SolidWorks 내장 재질 설정 시도
                    if item.material and file_path.lower().endswith(".sldprt"):
                        try:
                            sw_part = part_model
                            sw_part.SetMaterialPropertyName2(item.configuration, "", item.material)
                        except Exception:
                            pass

                    # 변경 상태 플래그 갱신
                    item.original_is_common_part = item.is_common_part
                    item.original_part_name = item.part_name
                    item.original_material = item.material
                    item.original_qty = item.qty
                    item.original_rev = item.rev
                    item.original_remark = item.remark
                    item.is_modified = False

                    # 모델 저장 (Silent Save)
                    try:
                        part_model.Save3(1, 0, 0)
                    except Exception:
                        pass

                    success_count += 1

                except Exception as ex_item:
                    fail_count += 1
                    errors.append(f"[{item.part_name}] 속성 저장 중 예외: {str(ex_item)}")

            # 작업 완료 후 활성 어셈블리 뷰 리빌드
            if active_doc:
                try:
                    active_doc.ForceRebuild3(False)
                except Exception:
                    pass

            return success_count, fail_count, errors

        except Exception as e:
            return success_count, fail_count, [f"전체 속성 적용 중 치명적 오류: {str(e)}"]

    def _ensure_cached_comp_records(self, model) -> List[dict]:
        """어셈블리 내 모든 컴포넌트와 식별 정보를 메모리에 캐싱하여 반환 (첫 클릭 및 연속 클릭 시 COM 통신 오버헤드 0ms)"""
        doc_path = os.path.normpath(_safe_call(model, "GetPathName", "") or "").lower()
        if self._cached_comp_records and self._cached_doc_path == doc_path:
            return self._cached_comp_records

        components = _safe_call(model, "GetComponents", None, False)
        if not components:
            self._cached_comp_records = []
            self._cached_doc_path = doc_path
            return []

        self._cached_doc_path = doc_path
        self._cached_components = [c for c in components if c is not None]
        self._cached_comp_records = []

        for comp in self._cached_components:
            if comp is None:
                continue
            file_path = _safe_call(comp, "GetPathName", "") or ""
            if not file_path:
                continue
            norm_path = os.path.normpath(file_path).lower()
            is_sub = norm_path.endswith(".sldasm")
            comp_name = (_safe_call(comp, "Name2", "") or "").strip()
            fname = os.path.basename(norm_path).lower()
            clean_name = _extract_leaf_name(comp_name)

            # 서브어셈블리 계층 깊이(level) 및 상위 어셈블리 경로/명칭 정밀 추적
            level = 0
            if comp_name:
                at_cnt = comp_name.count('@')
                slash_cnt = comp_name.count('/')
                level = max(at_cnt, slash_cnt)

            parent_paths = []
            parent_names = []
            try:
                p = _safe_call(comp, "GetParent")
                p_depth = 0
                while p is not None:
                    p_depth += 1
                    p_path = _safe_call(p, "GetPathName", "")
                    if p_path:
                        parent_paths.append(os.path.normpath(p_path).lower())
                    p_name = _safe_call(p, "Name2", "")
                    if p_name:
                        parent_names.append(p_name.lower().strip())
                    p = _safe_call(p, "GetParent")
                level = max(level, p_depth)
            except Exception:
                pass

            if '@' in comp_name:
                for seg in comp_name.split('@')[1:]:
                    s_clean = seg.split('/')[0].strip().lower()
                    if s_clean and s_clean not in parent_names:
                        parent_names.append(s_clean)
                        base_s = s_clean.split('-')[0]
                        if base_s and base_s not in parent_names:
                            parent_names.append(base_s)
            elif '/' in comp_name:
                for seg in comp_name.split('/')[:-1]:
                    s_clean = seg.split('@')[0].strip().lower()
                    if s_clean and s_clean not in parent_names:
                        parent_names.append(s_clean)
                        base_s = s_clean.split('-')[0]
                        if base_s and base_s not in parent_names:
                            parent_names.append(base_s)

            self._cached_comp_records.append({
                "comp": comp,
                "path": norm_path,
                "is_sub": is_sub,
                "name": comp_name.lower(),
                "fname": fname,
                "clean_name": clean_name,
                "level": level,
                "parent_paths": parent_paths,
                "parent_names": parent_names
            })
        return self._cached_comp_records

    def _is_subassembly(self, comp) -> bool:
        """컴포넌트가 서브어셈블리(.sldasm)인지 판별"""
        if comp is None:
            return False
        comp_path = (_safe_call(comp, "GetPathName", "") or "").lower()
        return comp_path.endswith(".sldasm")

    def _is_comp_target(self, comp, target_items: List[BOMItem]) -> bool:
        """어셈블리 컴포넌트가 대상 BOMItem에 해당하는지 엄격/정밀 판별 (서브어셈블리 내부 파트 포함)"""
        if comp is None:
            return False

        comp_path = os.path.normpath(_safe_call(comp, "GetPathName", "") or "").lower()
        if not comp_path or comp_path.endswith(".sldasm"):
            return False

        comp_name = (_safe_call(comp, "Name2", "") or "").lower().strip()
        comp_fname = os.path.basename(comp_path).lower()
        comp_clean = comp_name.split('@')[0].split('-')[0].split('/')[0].strip()

        for t in target_items:
            if t.file_path:
                t_path = os.path.normpath(t.file_path).lower()
                if t_path == comp_path:
                    return True

            if t.file_name:
                t_fname = t.file_name.lower().strip()
                if t_fname == comp_fname:
                    return True
                t_base, _ = os.path.splitext(t_fname)
                if t_base == comp_clean:
                    return True

            for cn in t.component_names:
                cn_lower = cn.lower().strip()
                if cn_lower and (cn_lower == comp_name or comp_name in cn_lower or cn_lower in comp_name):
                    return True

            if t.part_name:
                t_pname = t.part_name.lower().strip()
                if t_pname and t_pname == comp_clean:
                    return True

        return False

    def _select_comp(self, model, comp) -> bool:
        """컴포넌트를 안전하게 선택 목록에 추가 (Select4 및 SelectByID2)"""
        if comp is None:
            return False
        selected = False
        try:
            res = comp.Select4(True, None, False)
            if res:
                selected = True
        except Exception:
            pass

        if not selected:
            try:
                comp_name = _safe_call(comp, "Name2", "")
                if comp_name and model:
                    res = model.Extension.SelectByID2(comp_name, "COMPONENT", 0, 0, 0, True, 0, None, 0)
                    if res:
                        selected = True
            except Exception:
                pass
        return selected

    def set_components_transparency(self, target_items: List[BOMItem], all_items: List[BOMItem]) -> bool:
        """
        초고속 단일 패스(Single-pass)로 서브어셈블리 내/외부 모든 파트 중
        대상 파트 또는 대상 서브어셈블리 소속 부품(🟢)만 100% 완전 불투명(Opaque), 나머지 모든 파트(🔴)는 무색 투명 유리(Transparent)로 즉시 렌더링
        """
        target_paths = set()
        target_fnames = set()
        target_names = set()
        target_pnames = set()
        target_subassembly_paths = set()
        target_subassembly_names = set()
        target_subassembly_fnames = set()

        for item in target_items:
            is_sub = getattr(item, "is_subassembly", False) or (item.file_path and item.file_path.lower().endswith(".sldasm"))
            if item.file_path:
                t_path = os.path.normpath(item.file_path).lower()
                target_paths.add(t_path)
                t_fname = os.path.basename(t_path)
                target_fnames.add(t_fname)
                if is_sub:
                    target_subassembly_paths.add(t_path)
                    target_subassembly_fnames.add(t_fname)
            if item.file_name:
                t_fname = item.file_name.lower().strip()
                target_fnames.add(t_fname)
                t_base, _ = os.path.splitext(t_fname)
                target_names.add(t_base)
                if is_sub or t_fname.endswith(".sldasm"):
                    target_subassembly_fnames.add(t_fname)
                    target_subassembly_names.add(t_base)
            if item.part_name:
                p_clean = item.part_name.lower().strip()
                target_pnames.add(p_clean)
                if is_sub:
                    target_subassembly_names.add(p_clean)
            for cn in item.component_names:
                cn_lower = cn.lower().strip()
                target_names.add(cn_lower)
                clean_cn = _extract_leaf_name(cn_lower)
                if clean_cn:
                    target_names.add(clean_cn)
                if is_sub:
                    target_subassembly_names.add(clean_cn)
                    target_subassembly_names.add(cn_lower)

        # 1. BOMItem 상태 업데이트 (불투명: True, 투명: False)
        # 1단계: 직접 선택된 서브어셈블리 식별
        target_sub_indices = set()
        for idx, item in enumerate(all_items):
            item_path = os.path.normpath(item.file_path).lower() if item.file_path else ""
            item_part = item.part_name.lower().strip() if item.part_name else ""
            item_fname = os.path.basename(item_path) if item_path else ""
            item_is_sub = getattr(item, "is_subassembly", False) or item_path.endswith(".sldasm")

            is_direct = (
                item_path in target_paths or
                item_fname in target_fnames or
                item_part in target_pnames or
                item_part in target_names or
                any(n.lower().strip() in target_names or _extract_leaf_name(n) in target_names for n in item.component_names)
            )

            if is_direct and item_is_sub:
                target_sub_indices.add(idx)

        # 2단계: 대상 서브어셈블리의 모든 하위 자식 아이템(descendants)을 불투명(🟢) 대상에 포함
        matched_indices = set()
        for sub_idx in target_sub_indices:
            matched_indices.add(sub_idx)
            sub_level = getattr(all_items[sub_idx], "level", 0)
            for j in range(sub_idx + 1, len(all_items)):
                child_level = getattr(all_items[j], "level", 0)
                if child_level > sub_level:
                    matched_indices.add(j)
                    # 하위 부품의 식별자도 CAD 매칭용 세트에 추가
                    c_item = all_items[j]
                    if c_item.file_path:
                        c_p = os.path.normpath(c_item.file_path).lower()
                        target_paths.add(c_p)
                        target_fnames.add(os.path.basename(c_p))
                    if c_item.part_name:
                        target_pnames.add(c_item.part_name.lower().strip())
                else:
                    break

        # 3단계: 각 BOMItem is_opaque 플래그 결정
        for idx, item in enumerate(all_items):
            item_path = os.path.normpath(item.file_path).lower() if item.file_path else ""
            item_part = item.part_name.lower().strip() if item.part_name else ""
            item_fname = os.path.basename(item_path) if item_path else ""
            item_is_sub = getattr(item, "is_subassembly", False) or item_path.endswith(".sldasm")

            if idx in matched_indices:
                item.is_opaque = True
            else:
                is_direct = (
                    item_path in target_paths or
                    item_fname in target_fnames or
                    item_part in target_pnames or
                    item_part in target_names or
                    any(n.lower().strip() in target_names or _extract_leaf_name(n) in target_names for n in item.component_names)
                )
                if item_is_sub and idx not in target_sub_indices:
                    is_direct = False
                item.is_opaque = is_direct

        if self.mock_mode or not self.sw_app:
            return True

        try:
            model = getattr(self.sw_app, "ActiveDoc", None)
            if not model or _safe_call(model, "GetType", SW_DOC_NONE) != SW_DOC_ASSEMBLY:
                return False

            comp_records = self._ensure_cached_comp_records(model)
            if not comp_records:
                return False

            target_comps = []
            non_target_comps = []

            # 무색 투명 유리 머티리얼 (초록색 틴트 없는 순수 화이트 유리 85% 투명도)
            # [R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission]
            glass_mat = [1.0, 1.0, 1.0, 0.5, 0.5, 0.5, 0.3, 0.85, 0.0]

            def is_rec_under_target_sub(rec) -> bool:
                if not target_subassembly_paths and not target_subassembly_names and not target_subassembly_fnames:
                    return False
                for pp in rec.get("parent_paths", []):
                    if pp in target_subassembly_paths or os.path.basename(pp) in target_subassembly_fnames:
                        return True
                for pn in rec.get("parent_names", []):
                    if pn in target_subassembly_names or any(sn == pn or sn in pn for sn in target_subassembly_names if len(sn) >= 3):
                        return True
                c_name = rec.get("name", "")
                if c_name:
                    for sn in target_subassembly_names:
                        if sn and len(sn) >= 3:
                            if f"/{sn}" in c_name or f"{sn}-" in c_name or f"@{sn}" in c_name or sn in c_name:
                                return True
                return False

            for rec in comp_records:
                comp = rec["comp"]
                if comp is None:
                    continue

                # 1. 서브어셈블리(.sldasm) 컨테이너 자체
                if rec["is_sub"]:
                    # 서브어셈블리 컨테이너의 머티리얼 오버라이드를 제거하여 하위 자식 파트 각각의 독립 투명/불투명 렌더링 보장
                    _safe_call(comp, "RemoveMaterialProperty2", None, 1, None)
                    _safe_call(comp, "RemoveMaterialProperty2", None, 2, None)
                    continue

                # 2. 개별 파트(.sldprt) 판별: 직접 대상이거나, 대상 서브어셈블리의 하위 부품인 경우
                is_target = (
                    rec["path"] in target_paths or
                    rec["fname"] in target_fnames or
                    rec["clean_name"] in target_names or
                    rec["clean_name"] in target_pnames or
                    rec["name"] in target_names or
                    is_rec_under_target_sub(rec)
                )

                if is_target:
                    target_comps.append(comp)
                    # 100% 완전 불투명 및 원본 CAD 색상 복원
                    _safe_call(comp, "RemoveMaterialProperty2", None, 1, None)
                    _safe_call(comp, "RemoveMaterialProperty2", None, 2, None)
                else:
                    non_target_comps.append(comp)
                    # 무색 85% 투명 유리 적용
                    try:
                        comp.MaterialPropertyValues = glass_mat
                    except Exception:
                        pass

            # 3. SolidWorks CAD 뷰포트 내 모든 선택 상태 확실하게 해제
            _safe_call(model, "ClearSelection2", None, True)
            try:
                sel_mgr = _safe_call(model, "SelectionManager")
                if sel_mgr:
                    _safe_call(sel_mgr, "DeSelect2", None, -1, -1)
            except Exception:
                pass

            # 4. 뷰포트 즉각 리프레시
            _safe_call(model, "GraphicsRedraw2")
            try:
                active_view = getattr(model, "ActiveView", None)
                if active_view:
                    active_view.UpdateView()
            except Exception:
                pass

            return True
        except Exception as e:
            print(f"set_components_transparency 오류: {e}", file=sys.stderr)
            return False

    def show_all_opaque(self, all_items: List[BOMItem]) -> bool:
        """모든 부품을 화면에 표시하고 완전 불투명(Opaque) 상태로 초고속 복원"""
        for item in all_items:
            item.is_opaque = True

        if self.mock_mode or not self.sw_app:
            return True

        try:
            model = getattr(self.sw_app, "ActiveDoc", None)
            if not model or _safe_call(model, "GetType", SW_DOC_NONE) != SW_DOC_ASSEMBLY:
                return False

            comp_records = self._ensure_cached_comp_records(model)
            all_comps = [rec["comp"] for rec in comp_records if rec.get("comp") is not None]

            # 1. 모든 컴포넌트의 머티리얼 오버라이드 제거 -> 100% 원본 불투명 색상 복원
            for comp in all_comps:
                _safe_call(comp, "RemoveMaterialProperty2", None, 1, None)
                _safe_call(comp, "RemoveMaterialProperty2", None, 2, None)

            _safe_call(model, "ClearSelection2", None, True)
            _safe_call(model, "ViewZoomtofit2")
            _safe_call(model, "GraphicsRedraw2")
            try:
                active_view = getattr(model, "ActiveView", None)
                if active_view:
                    active_view.UpdateView()
            except Exception:
                pass
            return True
        except Exception as e:
            print(f"show_all_opaque 오류: {e}", file=sys.stderr)
            return False

    def isolate_and_zoom_parts(self, target_items: List[BOMItem], isolate_mode: bool = True) -> bool:
        """기존 호환용 메서드 -> set_components_transparency 호출"""
        return self.set_components_transparency(target_items, target_items)

    def show_all_components(self) -> bool:
        """기존 호환용 메서드 -> show_all_opaque 호출"""
        return self.show_all_opaque([])

    def _get_mock_bom_items(self) -> List[BOMItem]:
        """SolidWorks 연결이 없을 때 테스트/시연용 가상 데이터 생성 (서브어셈블리 계층 구조 포함)"""
        mock_data = [
            ("PUMP_UNIT_ASSY", "", 1, "메인 펌프 유닛", r"C:\CAD_Projects\PumpUnit\PUMP_UNIT_ASSY.SLDASM", True, 0),
            ("BASE_FRAME", "SS400", 1, "화약도장 (아이보리)", r"C:\CAD_Projects\PumpUnit\BASE_FRAME.SLDPRT", False, 1),
            ("MOTOR_MODULE_ASSY", "", 1, "구동 모터 모듈 서브Assy", r"C:\CAD_Projects\PumpUnit\MOTOR_MODULE_ASSY.SLDASM", True, 1),
            ("MOTOR_BRACKET", "AL6061-T6", 2, "아노다이징 (흑색)", r"C:\CAD_Projects\PumpUnit\MOTOR_BRACKET.SLDPRT", False, 2),
            ("MAIN_SHAFT_D25", "SCM440", 1, "고주파 열처리 HRC55", r"C:\CAD_Projects\PumpUnit\MAIN_SHAFT_D25.SLDPRT", False, 2),
            ("IMPELLER_HOUSING", "SUS304", 1, "내식 가공", r"C:\CAD_Projects\PumpUnit\IMPELLER_HOUSING.SLDPRT", False, 1),
            ("FLANGE_COUPLING", "S45C", 2, "무전해 니켈도금", r"C:\CAD_Projects\PumpUnit\FLANGE_COUPLING.SLDPRT", False, 0),
            ("SEAL_COVER", "POM", 4, "정밀 가공품", r"C:\CAD_Projects\PumpUnit\SEAL_COVER.SLDPRT", False, 0),
            ("HEX_BOLT_M8x25", "SUS304", 12, "규격품 / 툴박스", r"C:\CAD_Projects\PumpUnit\HEX_BOLT_M8x25.SLDPRT", False, 0),
            ("SPRING_WASHER_M8", "SPRING STEEL", 12, "규격품", r"C:\CAD_Projects\PumpUnit\SPRING_WASHER_M8.SLDPRT", False, 0),
        ]
        items = []
        for idx, (name, mat, qty, rem, path, is_sub, lvl) in enumerate(mock_data, start=1):
            item = BOMItem(
                item_no=idx,
                part_name=name,
                material=mat,
                qty=qty,
                remark=rem,
                file_name=os.path.basename(path),
                file_path=path,
                configuration="Default",
                is_subassembly=is_sub,
                level=lvl
            )
            items.append(item)
        return items
