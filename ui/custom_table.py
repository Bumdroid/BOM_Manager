import os
import re
from typing import List, Optional
from PySide6.QtWidgets import (
    QTableWidget, QTableWidgetItem, QStyledItemDelegate,
    QComboBox, QSpinBox, QLineEdit, QHeaderView, QMenu,
    QApplication, QStyle, QStyleOptionButton, QPushButton
)
from PySide6.QtCore import Qt, Signal, QModelIndex, QEvent, QRect, QRectF, QSize
from PySide6.QtGui import QColor, QBrush, QFont, QAction, QIcon, QPainter, QPixmap, QLinearGradient, QPen

from core.bom_model import BOMItem
from ui.styles import COMMON_MATERIALS

COL_NO = 0
COL_ISOLATE = 1
COL_COMMON = 2
COL_PART_NAME = 3
COL_MATERIAL = 4
COL_QTY = 5
COL_REV = 6
COL_REMARK = 7

COLUMN_HEADERS = [
    "No.",
    "화면\n표시",
    "Common\nPart",
    "Name of Part",
    "Material",
    "Q'TY",
    "Rev.",
    "REMARK"
]


def create_assy_badge_icon() -> QIcon:
    """간결하고 세련된 'Sub.' 배지 이미지(QIcon, 42x20) 동적 생성"""
    pixmap = QPixmap(42, 20)
    pixmap.fill(Qt.transparent)

    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.Antialiasing)
    painter.setRenderHint(QPainter.TextAntialiasing)

    # 둥근 모서리 배지 배경 (SolidWorks Modern Blue 계열)
    rect = QRectF(1.0, 1.0, 40.0, 18.0)
    grad = QLinearGradient(0, 0, 42, 20)
    grad.setColorAt(0.0, QColor("#2563EB")) # Modern Blue
    grad.setColorAt(1.0, QColor("#1D4ED8")) # Deep SolidWorks Blue

    painter.setBrush(QBrush(grad))
    painter.setPen(QPen(QColor("#93C5FD"), 1.0)) # Crisp Light Blue Border
    painter.drawRoundedRect(rect, 3.5, 3.5)

    # 'Sub.' 텍스트 (화이트 볼드, 8.5pt)
    font = QFont("Segoe UI", 8.5, QFont.Bold)
    painter.setFont(font)
    painter.setPen(QColor("#FFFFFF"))
    painter.drawText(rect, Qt.AlignCenter, "Sub.")

    painter.end()
    return QIcon(pixmap)


class CenteredCheckBoxDelegate(QStyledItemDelegate):
    """체크박스를 셀 정중앙에 배치하고 클릭을 처리하는 델리게이트"""
    toggled = Signal(int, bool) # row, is_checked

    def __init__(self, parent=None):
        super().__init__(parent)

    def paint(self, painter: QPainter, option, index: QModelIndex):
        # 배경 그리기 (선택 상태 등)
        self.initStyleOption(option, index)
        QApplication.style().drawPrimitive(QStyle.PE_PanelItemViewItem, option, painter)

        # 체크 상태 확인
        checked = index.data(Qt.UserRole)
        is_checked = bool(checked)

        btn_opt = QStyleOptionButton()
        btn_opt.state = QStyle.State_Enabled | (QStyle.State_On if is_checked else QStyle.State_Off)
        
        # 체크박스 크기 및 중앙 위치 계산
        style = QApplication.style()
        check_rect = style.subElementRect(QStyle.SE_CheckBoxIndicator, btn_opt, None)
        
        x = option.rect.x() + (option.rect.width() - check_rect.width()) // 2
        y = option.rect.y() + (option.rect.height() - check_rect.height()) // 2
        btn_opt.rect = QRect(x, y, check_rect.width(), check_rect.height())

        style.drawControl(QStyle.CE_CheckBox, btn_opt, painter)

    def editorEvent(self, event: QEvent, model, option, index: QModelIndex):
        if event.type() in (QEvent.MouseButtonRelease, QEvent.MouseButtonDblClick):
            if event.button() == Qt.LeftButton:
                curr_val = bool(index.data(Qt.UserRole))
                new_val = not curr_val
                model.setData(index, new_val, Qt.UserRole)
                self.toggled.emit(index.row(), new_val)
                return True
        return False


class MaterialComboBoxDelegate(QStyledItemDelegate):
    """재질(Material) 컬럼용 드롭다운 + 직접 입력 콤보박스 델리게이트"""

    def __init__(self, parent=None):
        super().__init__(parent)

    def createEditor(self, parent, option, index):
        combo = QComboBox(parent)
        combo.setEditable(True)
        combo.addItem("") # 빈칸 허용
        combo.addItems(COMMON_MATERIALS)
        return combo

    def setEditorData(self, editor: QComboBox, index: QModelIndex):
        current_val = index.model().data(index, Qt.DisplayRole) or ""
        # 현재 값이 목록에 없으면 추가
        if current_val and editor.findText(current_val) == -1:
            editor.insertItem(1, current_val)
        editor.setCurrentText(current_val)

    def setModelData(self, editor: QComboBox, model, index: QModelIndex):
        val = editor.currentText().strip()
        model.setData(index, val, Qt.EditRole)


class QtySpinBoxDelegate(QStyledItemDelegate):
    """수량(Q'TY) 컬럼용 정수 스핀박스 델리게이트"""

    def __init__(self, parent=None):
        super().__init__(parent)

    def createEditor(self, parent, option, index):
        spin = QSpinBox(parent)
        spin.setRange(1, 99999)
        spin.setAlignment(Qt.AlignCenter)
        return spin

    def setEditorData(self, editor: QSpinBox, index: QModelIndex):
        val_str = index.model().data(index, Qt.DisplayRole)
        try:
            val = int(val_str)
        except (ValueError, TypeError):
            val = 1
        editor.setValue(val)

    def setModelData(self, editor: QSpinBox, model, index: QModelIndex):
        model.setData(index, str(editor.value()), Qt.EditRole)


class BOMTableWidget(QTableWidget):
    """BOM 데이터를 표시하고 실시간 편집 및 화면 표시(Isolate)를 지원하는 메인 테이블 위젯"""

    itemDataChanged = Signal(int, BOMItem) # row, BOMItem
    statsUpdated = Signal(int, int, int, int) # total_items, total_qty, modified_count, common_count
    isolateRequested = Signal(list) # List[BOMItem]

    def __init__(self, parent=None):
        super().__init__(parent)
        self.bom_items: List[BOMItem] = []
        self._is_populating = False
        self.assy_icon = create_assy_badge_icon()

        self._init_ui()

    def _init_ui(self):
        self.setColumnCount(len(COLUMN_HEADERS))
        self.setHorizontalHeaderLabels(COLUMN_HEADERS)
        self.setAlternatingRowColors(True)
        self.setSelectionBehavior(QTableWidget.SelectRows)
        self.setSelectionMode(QTableWidget.ExtendedSelection)
        self.verticalHeader().setVisible(False)
        self.verticalHeader().setDefaultSectionSize(32)
        self.setIconSize(QSize(42, 20)) # 폭을 줄인 42x20 'Sub.' 배지 아이콘
        self.setShowGrid(True)

        # 델리게이트 설정
        self.common_delegate = CenteredCheckBoxDelegate(self)
        self.common_delegate.toggled.connect(self._on_common_toggled)
        self.setItemDelegateForColumn(COL_COMMON, self.common_delegate)

        self.material_delegate = MaterialComboBoxDelegate(self)
        self.qty_delegate = QtySpinBoxDelegate(self)
        self.setItemDelegateForColumn(COL_MATERIAL, self.material_delegate)
        self.setItemDelegateForColumn(COL_QTY, self.qty_delegate)

        # 열 너비 및 모드 설정
        header = self.horizontalHeader()
        header.setDefaultAlignment(Qt.AlignCenter)
        header.setSectionResizeMode(COL_NO, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(COL_ISOLATE, QHeaderView.Fixed)
        header.setSectionResizeMode(COL_COMMON, QHeaderView.Fixed)
        header.setSectionResizeMode(COL_PART_NAME, QHeaderView.Interactive)
        header.setSectionResizeMode(COL_MATERIAL, QHeaderView.Interactive)
        header.setSectionResizeMode(COL_QTY, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(COL_REV, QHeaderView.Interactive)
        header.setSectionResizeMode(COL_REMARK, QHeaderView.Stretch)

        self.setColumnWidth(COL_ISOLATE, 55)
        self.setColumnWidth(COL_COMMON, 65)
        self.setColumnWidth(COL_PART_NAME, 260)
        self.setColumnWidth(COL_MATERIAL, 140)
        self.setColumnWidth(COL_REV, 70)

        self.cellChanged.connect(self._on_cell_changed)
        self.cellClicked.connect(self._on_cell_clicked)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._show_context_menu)

    def load_items(self, items: List[BOMItem]):
        """BOMItem 리스트를 테이블에 로드 (서브어셈블리 Sub. 배지 및 └ 계층 구조 반영)"""
        self._is_populating = True
        self.bom_items = items
        self.setRowCount(len(items))

        for row, item in enumerate(items):
            # No.
            item_no = QTableWidgetItem(str(item.item_no))
            item_no.setTextAlignment(Qt.AlignCenter)
            item_no.setFlags(item_no.flags() & ~Qt.ItemIsEditable)
            self.setItem(row, COL_NO, item_no)

            # 화면표시 버튼 (🟢 불투명 / 🔴 투명)
            is_op = getattr(item, "is_opaque", True)
            item_iso = QTableWidgetItem("🟢" if is_op else "🔴")
            item_iso.setTextAlignment(Qt.AlignCenter)
            if is_op:
                item_iso.setToolTip(f"'{item.part_name}': 현재 불투명(강조) 상태입니다.\n클릭 시 이 파트만 불투명하게 보고 나머지는 투명화합니다.")
            else:
                item_iso.setToolTip(f"'{item.part_name}': 현재 투명 상태입니다.\n클릭 시 이 파트만 불투명하게 보고 나머지는 투명화합니다.")
            item_iso.setFlags(item_iso.flags() & ~Qt.ItemIsEditable)
            item_iso.setFont(QFont("Segoe UI Emoji", 11))
            self.setItem(row, COL_ISOLATE, item_iso)

            # Common Part (중앙 정렬 체크박스 델리게이트)
            item_common = QTableWidgetItem()
            item_common.setTextAlignment(Qt.AlignCenter)
            item_common.setData(Qt.UserRole, bool(item.is_common_part))
            item_common.setFlags(item_common.flags() & ~Qt.ItemIsEditable)
            self.setItem(row, COL_COMMON, item_common)

            # Name of Part (서브어셈블리 Sub. 배지 아이콘 + 스페이스 4개 └  계층 인덴트)
            level = getattr(item, "level", 0)
            if level > 0:
                indent = "    " * level
                display_name = f"{indent}└  {item.part_name}"
            else:
                display_name = item.part_name

            item_part = QTableWidgetItem(display_name)
            if getattr(item, "is_subassembly", False):
                item_part.setIcon(self.assy_icon)
                item_part.setFont(QFont("Segoe UI", 10, QFont.Bold))
                item_part.setToolTip(f"서브어셈블리 (.sldasm): {item.part_name}")
            else:
                item_part.setFont(QFont("Segoe UI", 10, QFont.DemiBold))

            self.setItem(row, COL_PART_NAME, item_part)

            # Material
            item_mat = QTableWidgetItem(item.material)
            self.setItem(row, COL_MATERIAL, item_mat)

            # Q'TY
            item_qty = QTableWidgetItem(str(item.qty))
            item_qty.setTextAlignment(Qt.AlignCenter)
            self.setItem(row, COL_QTY, item_qty)

            # Rev.
            item_rev = QTableWidgetItem(item.rev)
            item_rev.setTextAlignment(Qt.AlignCenter)
            self.setItem(row, COL_REV, item_rev)

            # REMARK
            item_rem = QTableWidgetItem(item.remark)
            self.setItem(row, COL_REMARK, item_rem)

            self._update_row_style(row, item)

        self._is_populating = False
        self._update_statistics()

    def update_transparency_icons(self):
        """모든 행의 화면표시 아이콘(🟢 불투명 / 🔴 투명)을 갱신"""
        self._is_populating = True
        for row, item in enumerate(self.bom_items):
            iso_item = self.item(row, COL_ISOLATE)
            if iso_item:
                is_op = getattr(item, "is_opaque", True)
                if is_op:
                    iso_item.setText("🟢")
                    iso_item.setToolTip(f"'{item.part_name}': 현재 불투명(강조) 상태입니다.\n클릭 시 이 파트만 불투명하게 보고 나머지는 투명화합니다.")
                else:
                    iso_item.setText("🔴")
                    iso_item.setToolTip(f"'{item.part_name}': 현재 투명 상태입니다.\n클릭 시 이 파트만 불투명하게 보고 나머지는 투명화합니다.")
        self._is_populating = False
        self.viewport().update()

    def _on_common_toggled(self, row: int, is_checked: bool):
        """Common Part 체크박스 상태 변경 처리"""
        if row < len(self.bom_items):
            item = self.bom_items[row]
            item.is_common_part = is_checked
            item.check_modified()
            self._update_row_style(row, item)
            self.itemDataChanged.emit(row, item)
            self._update_statistics()

    def _on_cell_clicked(self, row: int, col: int):
        """화면 표시(🟢/🔴) 클릭 시 이전 다중 선택 해제 후 해당 부품만 단독 표시 실행"""
        if row < len(self.bom_items):
            if col == COL_ISOLATE:
                self.clearSelection()
                self.selectRow(row)
                item = self.bom_items[row]
                self.isolateRequested.emit([item])

    def _on_cell_changed(self, row: int, col: int):
        if self._is_populating or row >= len(self.bom_items):
            return

        item = self.bom_items[row]
        cell_item = self.item(row, col)
        val = cell_item.text().strip() if cell_item else ""

        if col == COL_PART_NAME:
            # 'ㄴ', '└', '─', '├', '│', '┕', '╰', '[Sub.]', '[Assy.]', '🏷️' 등 트리 기호 제거하여 순수 파트명 추출
            clean_val = re.sub(r'^[└ㄴ├│┕╰┌\s─\-]+', '', val).strip()
            if clean_val.startswith("[Sub.]"):
                clean_val = clean_val[6:].strip()
            if clean_val.startswith("[Assy.]"):
                clean_val = clean_val[7:].strip()
            if clean_val.startswith("🏷️"):
                clean_val = clean_val[2:].strip()
            item.part_name = clean_val

            # 트리 계층에 맞게 셀 텍스트 서식 재적용 (스페이스 4개 └  계층 인덴트)
            level = getattr(item, "level", 0)
            if level > 0:
                formatted = f"{'    ' * level}└  {clean_val}"
            else:
                formatted = clean_val

            if cell_item and cell_item.text() != formatted:
                self._is_populating = True
                cell_item.setText(formatted)
                self._is_populating = False

        elif col == COL_MATERIAL:
            item.material = val
        elif col == COL_QTY:
            try:
                item.qty = max(1, int(val))
            except ValueError:
                item.qty = 1
                self._is_populating = True
                self.item(row, col).setText(str(item.qty))
                self._is_populating = False
        elif col == COL_REV:
            item.rev = val
        elif col == COL_REMARK:
            item.remark = val

        item.check_modified()
        self._update_row_style(row, item)
        self.itemDataChanged.emit(row, item)
        self._update_statistics()

    def _update_row_style(self, row: int, item: BOMItem):
        """수정된 행에 시각적 하이라이트 부여 (SolidWorks Light 테마)"""
        is_mod = item.check_modified()
        no_item = self.item(row, COL_NO)
        if no_item:
            if is_mod:
                no_item.setText(f"*{item.item_no}")
                no_item.setForeground(QBrush(QColor("#D97706"))) # Amber indicator
                no_item.setFont(QFont("Segoe UI", 10, QFont.Bold))
            else:
                no_item.setText(str(item.item_no))
                no_item.setForeground(QBrush(QColor("#64748B")))
                no_item.setFont(QFont("Segoe UI", 9))

        for c in [COL_PART_NAME, COL_MATERIAL, COL_QTY, COL_REV, COL_REMARK]:
            cell = self.item(row, c)
            if cell:
                if is_mod:
                    cell.setForeground(QBrush(QColor("#0284C7"))) # SolidWorks Blue for modified
                    cell.setFont(QFont("Segoe UI", 10, QFont.Bold))
                else:
                    if c == COL_PART_NAME and getattr(item, "is_subassembly", False):
                        cell.setForeground(QBrush(QColor("#1E3A8A"))) # Deep Navy for Sub-Assembly
                        cell.setFont(QFont("Segoe UI", 10, QFont.Bold))
                    else:
                        cell.setForeground(QBrush(QColor("#1E293B")))
                        cell.setFont(QFont("Segoe UI", 10, QFont.Normal if c != COL_PART_NAME else QFont.DemiBold))

    def _update_statistics(self):
        total_items = len(self.bom_items)
        total_qty = sum(item.qty for item in self.bom_items)
        modified_count = sum(1 for item in self.bom_items if item.is_modified)
        common_count = sum(1 for item in self.bom_items if item.is_common_part)
        self.statsUpdated.emit(total_items, total_qty, modified_count, common_count)

    def filter_items(self, query: str):
        """검색어에 따른 행 필터링 (대소문자 무시)"""
        query = query.strip().lower()
        for row in range(self.rowCount()):
            if not query:
                self.setRowHidden(row, False)
                continue

            match = False
            for col in [COL_PART_NAME, COL_MATERIAL, COL_REV, COL_REMARK]:
                cell = self.item(row, col)
                if cell and query in cell.text().lower():
                    match = True
                    break
            self.setRowHidden(row, not match)

    def toggle_batch_common_part(self):
        """선택된 행(또는 전체)의 공용품(Common Part) 체크박스를 토글/일괄 전환"""
        selected_rows = set(index.row() for index in self.selectedIndexes())
        target_rows = selected_rows if selected_rows else range(len(self.bom_items))
        if not target_rows:
            return

        any_unchecked = any(not self.bom_items[r].is_common_part for r in target_rows if r < len(self.bom_items))
        target_bool = any_unchecked

        self._is_populating = True
        for row in target_rows:
            if row < len(self.bom_items):
                item = self.bom_items[row]
                item.is_common_part = target_bool
                item.check_modified()
                cell = self.item(row, COL_COMMON)
                if cell:
                    cell.setData(Qt.UserRole, target_bool)
                self._update_row_style(row, item)
        self._is_populating = False
        self.viewport().update()
        self._update_statistics()

    def apply_batch_material(self, selected_material: str):
        """선택된 모든 행의 재질을 일괄 변경"""
        selected_rows = set(index.row() for index in self.selectedIndexes())
        if not selected_rows:
            return

        self._is_populating = True
        for row in selected_rows:
            if row < len(self.bom_items):
                item = self.bom_items[row]
                item.material = selected_material
                item.check_modified()
                self.item(row, COL_MATERIAL).setText(selected_material)
                self._update_row_style(row, item)
        self._is_populating = False
        self._update_statistics()

    def apply_batch_remark(self, remark_text: str):
        """선택된 모든 행의 비고를 일괄 변경"""
        selected_rows = set(index.row() for index in self.selectedIndexes())
        if not selected_rows:
            return

        self._is_populating = True
        for row in selected_rows:
            if row < len(self.bom_items):
                item = self.bom_items[row]
                item.remark = remark_text
                item.check_modified()
                self.item(row, COL_REMARK).setText(remark_text)
                self._update_row_style(row, item)
        self._is_populating = False
        self._update_statistics()

    def _show_context_menu(self, pos):
        index = self.indexAt(pos)
        if index.isValid():
            row = index.row()
            selected_rows = set(i.row() for i in self.selectedIndexes())
            # 우클릭한 행이 기존 다중 선택에 속하지 않으면 해당 행만 단독 선택
            if row not in selected_rows or len(selected_rows) <= 1:
                self.clearSelection()
                self.selectRow(row)
                self.setCurrentCell(row, index.column())

        menu = QMenu(self)
        
        # 선택된 파트 화면 단독 표시 (Isolate)
        isolate_action = QAction("👁️ 선택한 부품만 화면에 표시 (Isolate)", self)
        isolate_action.triggered.connect(self._isolate_selected_rows)
        menu.addAction(isolate_action)

        menu.addSeparator()

        toggle_common_action = QAction("☑️ 공용품(Common Part) 체크 토글", self)
        toggle_common_action.triggered.connect(self.toggle_batch_common_part)
        menu.addAction(toggle_common_action)

        copy_action = QAction("📋 셀 내용 복사 (Copy)", self)
        copy_action.triggered.connect(self._copy_selection)
        menu.addAction(copy_action)

        open_folder_action = QAction("📂 파트 폴더 열기 (Open Folder)", self)
        open_folder_action.triggered.connect(self._open_part_folder)
        menu.addAction(open_folder_action)

        menu.addSeparator()

        reset_action = QAction("↩️ 원래 값으로 되돌리기 (Reset)", self)
        reset_action.triggered.connect(self._reset_selected_rows)
        menu.addAction(reset_action)

        menu.exec(self.viewport().mapToGlobal(pos))

    def _isolate_selected_rows(self):
        selected_rows = sorted(list(set(index.row() for index in self.selectedIndexes())))
        if not selected_rows and self.currentRow() >= 0:
            selected_rows = [self.currentRow()]
        items = [self.bom_items[r] for r in selected_rows if 0 <= r < len(self.bom_items)]
        if items:
            self.isolateRequested.emit(items)

    def _copy_selection(self):
        selected = self.selectedItems()
        if selected:
            from PySide6.QtWidgets import QApplication
            QApplication.clipboard().setText(selected[0].text())

    def _open_part_folder(self):
        row = self.currentRow()
        if 0 <= row < len(self.bom_items):
            path = self.bom_items[row].file_path
            if path and os.path.exists(path):
                os.system(f'explorer /select,"{path}"')

    def _reset_selected_rows(self):
        selected_rows = set(index.row() for index in self.selectedIndexes())
        self._is_populating = True
        for row in selected_rows:
            if row < len(self.bom_items):
                item = self.bom_items[row]
                item.is_common_part = item.original_is_common_part
                item.part_name = item.original_part_name
                item.material = item.original_material
                item.qty = item.original_qty
                item.rev = item.original_rev
                item.remark = item.original_remark
                item.is_modified = False

                cell_common = self.item(row, COL_COMMON)
                if cell_common:
                    cell_common.setData(Qt.UserRole, item.is_common_part)

                # 파트명 서식 재적용 (스페이스 4개 └  계층 인덴트)
                part_cell = self.item(row, COL_PART_NAME)
                if part_cell:
                    level = getattr(item, "level", 0)
                    if level > 0:
                        formatted_name = f"{'    ' * level}└  {item.part_name}"
                    else:
                        formatted_name = item.part_name
                    part_cell.setText(formatted_name)
                    if getattr(item, "is_subassembly", False):
                        part_cell.setIcon(self.assy_icon)
                    else:
                        part_cell.setIcon(QIcon())

                self.item(row, COL_MATERIAL).setText(item.material)
                self.item(row, COL_QTY).setText(str(item.qty))
                self.item(row, COL_REV).setText(item.rev)
                self.item(row, COL_REMARK).setText(item.remark)
                self._update_row_style(row, item)
        self._is_populating = False
        self.viewport().update()
        self._update_statistics()
