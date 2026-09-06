import os
import sys
from typing import List, Optional

from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel,
    QPushButton, QLineEdit, QCheckBox, QFileDialog, QMessageBox,
    QDialog, QComboBox, QFormLayout, QDialogButtonBox, QStatusBar,
    QFrame, QSizePolicy
)
from PySide6.QtCore import Qt, QTimer, QByteArray
from PySide6.QtGui import QFont, QIcon, QPixmap

from core.bom_model import BOMItem, AssemblyInfo
from core.sw_connector import SolidWorksConnector
from ui.custom_table import BOMTableWidget
from ui.styles import MODERN_STYLE, COMMON_MATERIALS
from utils.exporter import BOMExporter


class BatchMaterialDialog(QDialog):
    """선택된 파트 재질 일괄 변경 다이얼로그"""
    def __init__(self, parent=None, count=1):
        super().__init__(parent)
        self.setWindowTitle("재질 일괄 지정 (Batch Material)")
        self.setFixedWidth(380)
        self.setStyleSheet(MODERN_STYLE)

        layout = QVBoxLayout(self)

        info_label = QLabel(f"선택한 {count}개 파트에 적용할 재질을 선택하거나 입력하세요:")
        info_label.setWordWrap(True)
        layout.addWidget(info_label)

        self.combo = QComboBox()
        self.combo.setEditable(True)
        self.combo.addItems(COMMON_MATERIALS)
        layout.addWidget(self.combo)

        btn_box = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btn_box.accepted.connect(self.accept)
        btn_box.rejected.connect(self.reject)
        layout.addWidget(btn_box)

    def get_selected_material(self) -> str:
        return self.combo.currentText().strip()


class BatchRemarkDialog(QDialog):
    """선택된 파트 비고 일괄 변경 다이얼로그"""
    def __init__(self, parent=None, count=1):
        super().__init__(parent)
        self.setWindowTitle("비고 일괄 지정 (Batch Remark)")
        self.setFixedWidth(400)
        self.setStyleSheet(MODERN_STYLE)

        layout = QVBoxLayout(self)

        info_label = QLabel(f"선택한 {count}개 파트에 적용할 비고(REMARK)를 입력하세요:")
        info_label.setWordWrap(True)
        layout.addWidget(info_label)

        self.line_edit = QLineEdit()
        self.line_edit.setPlaceholderText("예: 아노다이징 (흑색), 후가공 필요, 구매품 등")
        layout.addWidget(self.line_edit)

        btn_box = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btn_box.accepted.connect(self.accept)
        btn_box.rejected.connect(self.reject)
        layout.addWidget(btn_box)

    def get_remark_text(self) -> str:
        return self.line_edit.text().strip()


class MainWindow(QMainWindow):
    """BOM Manager V0.0 메인 윈도우"""

    def __init__(self, mock_mode: bool = False):
        super().__init__()
        self.mock_mode = mock_mode
        self.sw_connector = SolidWorksConnector(mock_mode=self.mock_mode)
        self.current_assembly_info = AssemblyInfo()

        self._init_window()
        self._init_ui()
        self._check_sw_connection_and_load(initial=True)

    def _init_window(self):
        self.setWindowTitle("BOM Manager V0.0 - SolidWorks 2021")
        self.resize(1150, 750)
        self.setMinimumSize(850, 550)
        self.setStyleSheet(MODERN_STYLE)

    def _init_ui(self):
        central_widget = QWidget(self)
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(16, 16, 16, 16)
        main_layout.setSpacing(12)

        # 1. 상단 헤더 패널 (타이틀, SolidWorks 상태, 레벨 옵션, 검색창)
        header_panel = QFrame()
        header_panel.setObjectName("headerPanel")
        header_layout = QHBoxLayout(header_panel)
        header_layout.setContentsMargins(8, 8, 8, 8)

        # 로고 & 타이틀 영역 (페페 아이콘 + 타이틀)
        title_container = QHBoxLayout()
        title_container.setSpacing(10)

        # 페페 캐릭터 로고 이미지
        logo_pixmap = self._get_logo_pixmap(36, 36)
        if not logo_pixmap.isNull():
            self.setWindowIcon(QIcon(logo_pixmap))
            logo_label = QLabel()
            logo_label.setObjectName("logoLabel")
            logo_label.setPixmap(logo_pixmap)
            logo_label.setFixedSize(36, 36)
            title_container.addWidget(logo_label)

        title_box = QVBoxLayout()
        title_box.setSpacing(2)
        title_label = QLabel("BOM Manager V0.0")
        title_label.setObjectName("titleLabel")
        subtitle_label = QLabel("SolidWorks 2021 Active Assembly BOM Automation")
        subtitle_label.setObjectName("subtitleLabel")
        title_box.addWidget(title_label)
        title_box.addWidget(subtitle_label)
        title_container.addLayout(title_box)

        header_layout.addLayout(title_container)

        header_layout.addSpacing(20)

        # 상태 인디케이터 뱃지
        self.status_badge = QLabel("연결 확인 중...")
        self.status_badge.setObjectName("badgeLabel")
        header_layout.addWidget(self.status_badge)

        header_layout.addStretch()

        # 어셈블리 탐색 옵션 (최상위 레벨만 / 전체 하위 포함)
        self.chk_top_level = QCheckBox("최상위(Top-Level) 부품만")
        self.chk_top_level.setToolTip("체크 시 하위 서브어셈블리 내부 부품을 제외하고 최상위 부품만 추출합니다.")
        self.chk_top_level.toggled.connect(self._on_reload_clicked)
        header_layout.addWidget(self.chk_top_level)

        header_layout.addSpacing(10)

        # 검색 필터
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("🔍 부품명, 재질, 비고 검색...")
        self.search_input.setFixedWidth(220)
        self.search_input.textChanged.connect(self._on_search_text_changed)
        header_layout.addWidget(self.search_input)

        main_layout.addWidget(header_panel)

        # 2. 통계 요약 카드 패널
        stats_panel = QFrame()
        stats_panel.setObjectName("cardPanel")
        stats_layout = QHBoxLayout(stats_panel)
        stats_layout.setContentsMargins(12, 6, 12, 6)

        # 고유 파트 수
        self.lbl_unique_parts = QLabel("0")
        self.lbl_unique_parts.setObjectName("statValue")
        lbl_unique_title = QLabel("고유 부품 수 (Unique Parts):")
        lbl_unique_title.setObjectName("statLabel")
        stats_layout.addWidget(lbl_unique_title)
        stats_layout.addWidget(self.lbl_unique_parts)
        stats_layout.addSpacing(20)

        # 총 수량
        self.lbl_total_qty = QLabel("0")
        self.lbl_total_qty.setObjectName("statValue")
        lbl_qty_title = QLabel("총 수량 (Total Q'TY):")
        lbl_qty_title.setObjectName("statLabel")
        stats_layout.addWidget(lbl_qty_title)
        stats_layout.addWidget(self.lbl_total_qty)
        stats_layout.addSpacing(20)

        # 공용품 수
        self.lbl_common_count = QLabel("0")
        self.lbl_common_count.setObjectName("statValue")
        self.lbl_common_count.setStyleSheet("color: #0284C7; font-weight: bold;")
        lbl_com_title = QLabel("공용품 (Common):")
        lbl_com_title.setObjectName("statLabel")
        stats_layout.addWidget(lbl_com_title)
        stats_layout.addWidget(self.lbl_common_count)
        stats_layout.addSpacing(20)

        # 수정된 항목 수
        self.lbl_modified_count = QLabel("0")
        self.lbl_modified_count.setObjectName("statValue")
        self.lbl_modified_count.setStyleSheet("color: #D97706; font-weight: bold;") # Amber
        lbl_mod_title = QLabel("수정됨 (Modified):")
        lbl_mod_title.setObjectName("statLabel")
        stats_layout.addWidget(lbl_mod_title)
        stats_layout.addWidget(self.lbl_modified_count)

        stats_layout.addStretch()

        # 트리 전체 펼치기 & 접기 버튼
        self.btn_expand_all = QPushButton("📂 전체 펼치기")
        self.btn_expand_all.setToolTip("모든 서브어셈블리 하위 부품 트리를 펼칩니다.")
        self.btn_expand_all.clicked.connect(lambda: self.table_widget.expand_all())
        stats_layout.addWidget(self.btn_expand_all)

        self.btn_collapse_all = QPushButton("📁 전체 접기")
        self.btn_collapse_all.setToolTip("모든 서브어셈블리 하위 부품 트리를 접습니다.")
        self.btn_collapse_all.clicked.connect(lambda: self.table_widget.collapse_all())
        stats_layout.addWidget(self.btn_collapse_all)

        # 빠른 새로고침 & 전체 표시 버튼
        self.btn_show_all = QPushButton("🌐 전체 표시 (Show All)")
        self.btn_show_all.setToolTip("숨겨진 모든 부품을 다시 SolidWorks 화면에 표시합니다.")
        self.btn_show_all.clicked.connect(self._on_show_all_clicked)
        stats_layout.addWidget(self.btn_show_all)

        self.btn_refresh = QPushButton("🔄 새로고침 (Reload)")
        self.btn_refresh.setToolTip("현재 활성 SolidWorks 어셈블리에서 데이터를 다시 읽어옵니다.")
        self.btn_refresh.clicked.connect(self._on_reload_clicked)
        stats_layout.addWidget(self.btn_refresh)

        main_layout.addWidget(stats_panel)

        # 3. 중앙 BOM 테이블
        self.table_widget = BOMTableWidget(self)
        self.table_widget.statsUpdated.connect(self._on_stats_updated)
        self.table_widget.isolateRequested.connect(self._on_isolate_requested)
        main_layout.addWidget(self.table_widget)

        # 4. 하단 액션 버튼 바
        action_panel = QFrame()
        action_layout = QHBoxLayout(action_panel)
        action_layout.setContentsMargins(0, 4, 0, 0)

        # 왼쪽: 일괄 편집 도구
        self.btn_batch_common = QPushButton("☑️ 공용품 일괄 전환")
        self.btn_batch_common.setObjectName("accentButton")
        self.btn_batch_common.setToolTip("선택한 행의 공용품(Common Part) 체크박스를 일괄 토글합니다.")
        self.btn_batch_common.clicked.connect(self.table_widget.toggle_batch_common_part)
        action_layout.addWidget(self.btn_batch_common)

        self.btn_batch_mat = QPushButton("🏷️ 재질 일괄 지정")
        self.btn_batch_mat.setObjectName("accentButton")
        self.btn_batch_mat.setToolTip("선택한 행의 재질(Material)을 일괄 변경합니다.")
        self.btn_batch_mat.clicked.connect(self._on_batch_material_clicked)
        action_layout.addWidget(self.btn_batch_mat)

        self.btn_batch_remark = QPushButton("📝 비고 일괄 지정")
        self.btn_batch_remark.setObjectName("accentButton")
        self.btn_batch_remark.setToolTip("선택한 행의 비고(REMARK)를 일괄 변경합니다.")
        self.btn_batch_remark.clicked.connect(self._on_batch_remark_clicked)
        action_layout.addWidget(self.btn_batch_remark)

        action_layout.addStretch()

        # 오른쪽: 내보내기 & SolidWorks 반영
        self.btn_export_excel = QPushButton("📊 Excel 내보내기")
        self.btn_export_excel.setObjectName("successButton")
        self.btn_export_excel.clicked.connect(self._on_export_excel_clicked)
        action_layout.addWidget(self.btn_export_excel)

        self.btn_export_csv = QPushButton("📄 CSV 내보내기")
        self.btn_export_csv.clicked.connect(self._on_export_csv_clicked)
        action_layout.addWidget(self.btn_export_csv)

        action_layout.addSpacing(10)

        self.btn_apply_sw = QPushButton("💾 SolidWorks에 속성 저장 (Apply)")
        self.btn_apply_sw.setObjectName("primaryButton")
        self.btn_apply_sw.setToolTip("입력/수정한 부품명, 재질, 수량, 비고를 각 파트의 Custom Property에 직접 저장합니다.")
        self.btn_apply_sw.clicked.connect(self._on_apply_sw_clicked)
        action_layout.addWidget(self.btn_apply_sw)

        main_layout.addWidget(action_panel)

        # 5. 상태 표시줄
        self.status_bar = QStatusBar()
        self.setStatusBar(self.status_bar)
        self.status_bar.showMessage("BOM Manager 준비 완료.")

        # 6. SolidWorks 문서 변경 자동 감지 타이머 (2.5초 주기)
        self.auto_timer = QTimer(self)
        self.auto_timer.setInterval(2500)
        self.auto_timer.timeout.connect(self._on_auto_check_timer)
        self.auto_timer.start()

    def _on_auto_check_timer(self):
        """백그라운드에서 주기적으로 활성 문서 변경을 감지하여 자동 동기화"""
        if self.mock_mode:
            return

        # 사용자가 셀을 편집 중이거나 수정된 항목이 있는 경우 강제 덮어쓰기 방지
        if any(item.is_modified for item in self.table_widget.bom_items):
            return

        assy_info = self.sw_connector.get_active_assembly_info()
        if assy_info.is_connected and not assy_info.error_message:
            if assy_info.title != self.current_assembly_info.title or len(self.table_widget.bom_items) == 0:
                self._check_sw_connection_and_load(initial=True, silent=True)
        elif not assy_info.is_connected:
            self.status_badge.setText("🔴 SolidWorks 미연결")
            self.status_badge.setStyleSheet("background-color: #FEE2E2; color: #991B1B; border: 1px solid #F87171;")

    def _check_sw_connection_and_load(self, initial: bool = False, silent: bool = False):
        """SolidWorks 연결 확인 및 활성 어셈블리 BOM 로드"""
        assy_info = self.sw_connector.get_active_assembly_info()
        self.current_assembly_info = assy_info

        if not assy_info.is_connected:
            self.status_badge.setText("🔴 SolidWorks 미연결")
            self.status_badge.setStyleSheet("background-color: #FEE2E2; color: #991B1B; border: 1px solid #F87171;")
            self.status_bar.showMessage(assy_info.error_message or "SolidWorks 2021이 실행되어 있지 않습니다.")
            if not initial and not silent and not self.mock_mode:
                QMessageBox.warning(self, "연결 확인", assy_info.error_message or "SolidWorks 2021 연결에 실패했습니다.\n\nSolidWorks가 켜져 있는지 확인해 주세요.")
            return

        if assy_info.error_message:
            self.status_badge.setText(f"🟡 SolidWorks 대기 중 ({assy_info.title or '문서 없음'})")
            self.status_badge.setStyleSheet("background-color: #FEF3C7; color: #92400E; border: 1px solid #FCD34D;")
            self.status_bar.showMessage(assy_info.error_message)
            if not initial and not silent:
                QMessageBox.information(self, "문서 확인", assy_info.error_message)
            return

        self.status_badge.setText(f"🟢 {assy_info.title}")
        self.status_badge.setStyleSheet("background-color: #D1FAE5; color: #065F46; border: 1px solid #34D399;")

        # BOM 데이터 로드
        top_level = self.chk_top_level.isChecked()
        items, err = self.sw_connector.load_bom(top_level_only=top_level)

        if err:
            self.status_bar.showMessage(f"오류: {err}")
            if not initial and not silent:
                QMessageBox.warning(self, "BOM 로드 실패", err)
            return

        self.table_widget.load_items(items)
        self.status_bar.showMessage(f"'{assy_info.title}'에서 {len(items)}개 파트 정보를 성공적으로 불러왔습니다.")

    def _on_reload_clicked(self):
        """새로고침 버튼 클릭 시 재로드"""
        self._check_sw_connection_and_load(initial=False, silent=False)

    def _on_search_text_changed(self, text: str):
        self.table_widget.filter_items(text)

    def _on_stats_updated(self, total_items: int, total_qty: int, modified_count: int, common_count: int):
        self.lbl_unique_parts.setText(str(total_items))
        self.lbl_total_qty.setText(str(total_qty))
        self.lbl_common_count.setText(str(common_count))
        self.lbl_modified_count.setText(str(modified_count))

    def _on_isolate_requested(self, items: List[BOMItem]):
        """선택된 파트(들)는 불투명(🟢), 나머지는 투명도(🔴) 높여서 SolidWorks 화면에 표시"""
        if not items:
            return
        names = ", ".join(i.part_name for i in items[:3])
        if len(items) > 3:
            names += f" 외 {len(items)-3}개"
        self.status_bar.showMessage(f"SolidWorks 화면에서 '{names}' 부품 불투명(🟢) 설정 및 나머지 투명화(🔴) 중...")
        
        success = self.sw_connector.set_components_transparency(items, self.table_widget.bom_items)
        self.table_widget.update_transparency_icons()

        if success:
            self.status_bar.showMessage(f"SolidWorks 화면에 '{names}' 부품이 불투명(🟢)하게 강조되었습니다.")
        else:
            self.status_bar.showMessage(f"SolidWorks 연결 확인 필요: '{names}'")

    def _on_show_all_clicked(self):
        """SolidWorks 화면의 모든 파트를 불투명(🟢) 상태로 복원"""
        self.sw_connector.show_all_opaque(self.table_widget.bom_items)
        self.table_widget.update_transparency_icons()
        self.status_bar.showMessage("SolidWorks 화면의 모든 부품을 불투명(🟢) 상태로 복원했습니다.")

    def _on_batch_material_clicked(self):
        selected_rows = set(index.row() for index in self.table_widget.selectedIndexes())
        if not selected_rows:
            QMessageBox.information(self, "안내", "재질을 일괄 변경할 파트 행을 먼저 선택해주세요.")
            return

        dlg = BatchMaterialDialog(self, count=len(selected_rows))
        if dlg.exec() == QDialog.Accepted:
            mat = dlg.get_selected_material()
            if mat:
                self.table_widget.apply_batch_material(mat)
                self.status_bar.showMessage(f"{len(selected_rows)}개 파트의 재질을 '{mat}'(으)로 변경했습니다.")

    def _on_batch_remark_clicked(self):
        selected_rows = set(index.row() for index in self.table_widget.selectedIndexes())
        if not selected_rows:
            QMessageBox.information(self, "안내", "비고를 일괄 변경할 파트 행을 먼저 선택해주세요.")
            return

        dlg = BatchRemarkDialog(self, count=len(selected_rows))
        if dlg.exec() == QDialog.Accepted:
            rem = dlg.get_remark_text()
            self.table_widget.apply_batch_remark(rem)
            self.status_bar.showMessage(f"{len(selected_rows)}개 파트의 비고를 변경했습니다.")

    def _on_export_excel_clicked(self):
        items = self.table_widget.bom_items
        if not items:
            QMessageBox.warning(self, "경고", "내보낼 BOM 데이터가 없습니다.")
            return

        default_name = f"BOM_{os.path.splitext(self.current_assembly_info.title)[0] or 'Assembly'}.xlsx"
        file_path, _ = QFileDialog.getSaveFileName(
            self, "BOM Excel 저장", default_name, "Excel Files (*.xlsx)"
        )
        if file_path:
            try:
                BOMExporter.export_to_excel(items, file_path, self.current_assembly_info.title)
                self.status_bar.showMessage(f"Excel 파일 저장 완료: {file_path}")
                QMessageBox.information(self, "완료", f"BOM 데이터가 Excel 파일로 성공적으로 저장되었습니다.\n\n경로: {file_path}")
            except Exception as e:
                QMessageBox.critical(self, "오류", f"Excel 파일 저장 중 오류가 발생했습니다:\n{str(e)}")

    def _on_export_csv_clicked(self):
        items = self.table_widget.bom_items
        if not items:
            QMessageBox.warning(self, "경고", "내보낼 BOM 데이터가 없습니다.")
            return

        default_name = f"BOM_{os.path.splitext(self.current_assembly_info.title)[0] or 'Assembly'}.csv"
        file_path, _ = QFileDialog.getSaveFileName(
            self, "BOM CSV 저장", default_name, "CSV Files (*.csv)"
        )
        if file_path:
            try:
                BOMExporter.export_to_csv(items, file_path)
                self.status_bar.showMessage(f"CSV 파일 저장 완료: {file_path}")
                QMessageBox.information(self, "완료", f"BOM 데이터가 CSV 파일로 성공적으로 저장되었습니다.\n\n경로: {file_path}")
            except Exception as e:
                QMessageBox.critical(self, "오류", f"CSV 파일 저장 중 오류가 발생했습니다:\n{str(e)}")

    def _on_apply_sw_clicked(self):
        items = self.table_widget.bom_items
        if not items:
            QMessageBox.warning(self, "경고", "적용할 BOM 항목이 없습니다.")
            return

        modified_items = [item for item in items if item.is_modified]
        target_items = modified_items if modified_items else items

        msg = (
            f"총 {len(target_items)}개 파트의 사용자 정의 속성(Custom Properties)에\n"
            f"입력하신 정보(Name of Part, Material, Q'TY, REMARK)를 SolidWorks 모델에 반영하시겠습니까?"
        )
        reply = QMessageBox.question(
            self, "SolidWorks 속성 저장 확인", msg,
            QMessageBox.Yes | QMessageBox.No, QMessageBox.Yes
        )
        if reply != QMessageBox.Yes:
            return

        self.status_bar.showMessage("SolidWorks 모델에 속성 저장 중...")
        success_cnt, fail_cnt, errors = self.sw_connector.apply_properties_to_solidworks(target_items)

        # 테이블 갱신
        self.table_widget.load_items(items)

        result_msg = f"성공: {success_cnt}개 파트 반영 완료"
        if fail_cnt > 0:
            result_msg += f"\n실패: {fail_cnt}개\n\n에러 세부내용:\n" + "\n".join(errors[:5])
            QMessageBox.warning(self, "속성 저장 결과", result_msg)
        else:
            QMessageBox.information(self, "저장 완료", f"{success_cnt}개 파트의 속성이 SolidWorks에 성공적으로 저장되었습니다.")

        self.status_bar.showMessage(f"SolidWorks 속성 저장 완료 (성공: {success_cnt}, 실패: {fail_cnt})")

    def _get_logo_pixmap(self, width: int = 36, height: int = 36) -> QPixmap:
        """페페 로고 이미지를 base64 또는 리소스 파일에서 로드하여 리사이즈된 QPixmap 반환"""
        try:
            from resources.icons_b64 import LOGO_PNG_B64_64
            import base64
            data = base64.b64decode(LOGO_PNG_B64_64)
            pixmap = QPixmap()
            pixmap.loadFromData(data, "PNG")
            if not pixmap.isNull():
                return pixmap.scaled(width, height, Qt.KeepAspectRatio, Qt.SmoothTransformation)
        except Exception:
            pass

        try:
            res_path = os.path.join(os.path.dirname(os.path.dirname(__file__)), "resources", "logo.png")
            if os.path.exists(res_path):
                pixmap = QPixmap(res_path)
                if not pixmap.isNull():
                    return pixmap.scaled(width, height, Qt.KeepAspectRatio, Qt.SmoothTransformation)
        except Exception:
            pass

        return QPixmap()
