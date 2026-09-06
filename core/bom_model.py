from dataclasses import dataclass, field
from typing import Dict, List, Optional
import os

@dataclass
class BOMItem:
    """Represents a single unique part/component item in the BOM."""
    item_no: int = 1
    is_common_part: bool = False
    part_name: str = ""
    material: str = ""
    qty: int = 1
    rev: str = ""
    remark: str = ""
    file_name: str = ""
    file_path: str = ""
    configuration: str = ""
    component_names: List[str] = field(default_factory=list)
    custom_properties: Dict[str, str] = field(default_factory=dict)
    is_modified: bool = False
    is_suppressed: bool = False
    is_virtual: bool = False
    is_opaque: bool = True # 화면 표시 상태: True(불투명, 초록색), False(투명, 빨간색)
    is_subassembly: bool = False # True인 경우 서브어셈블리(.sldasm)
    is_expanded: bool = True # 트리 펼침 상태: True(펼쳐짐 ▼), False(접힘 ▶)
    level: int = 0 # 0: 최상위, 1: 1단계 하위, 2: 2단계 하위 등
    original_is_common_part: Optional[bool] = None
    original_part_name: Optional[str] = None
    original_material: Optional[str] = None
    original_qty: Optional[int] = None
    original_rev: Optional[str] = None
    original_remark: Optional[str] = None

    def __post_init__(self):
        if not self.file_name and self.file_path:
            self.file_name = os.path.basename(self.file_path)
        if not self.part_name and self.file_name:
            # 기본 파트명은 파일명에서 확장자 제거
            name, _ = os.path.splitext(self.file_name)
            self.part_name = name
        
        # 원본 값 저장 (변경 추적용)
        if self.original_is_common_part is None:
            self.original_is_common_part = self.is_common_part
        if self.original_part_name is None:
            self.original_part_name = self.part_name
        if self.original_material is None:
            self.original_material = self.material
        if self.original_qty is None:
            self.original_qty = self.qty
        if self.original_rev is None:
            self.original_rev = self.rev
        if self.original_remark is None:
            self.original_remark = self.remark

    def check_modified(self) -> bool:
        """원본 값 대비 변경 여부 업데이트"""
        self.is_modified = (
            self.is_common_part != self.original_is_common_part or
            self.part_name != self.original_part_name or
            self.material != self.original_material or
            self.qty != self.original_qty or
            self.rev != self.original_rev or
            self.remark != self.original_remark
        )
        return self.is_modified

    def to_dict(self) -> dict:
        return {
            "No": self.item_no,
            "Common Part": "Y" if self.is_common_part else "N",
            "Name of Part": self.part_name,
            "Material": self.material,
            "Q'TY": self.qty,
            "Rev.": self.rev,
            "REMARK": self.remark,
        }


@dataclass
class AssemblyInfo:
    """Information about the active SolidWorks assembly."""
    title: str = "No Assembly"
    path: str = ""
    active_configuration: str = ""
    total_components_count: int = 0
    unique_parts_count: int = 0
    is_connected: bool = False
    error_message: Optional[str] = None
