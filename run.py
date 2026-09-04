"""
BOM Manager V0.0 - SolidWorks 2021 Active Assembly BOM Automation
Entry Point Application with Single-Instance Enforcement
"""

import sys
import os
import argparse
import ctypes
from ctypes import wintypes

# 경로 보정 (서브폴더 모듈 import 지원)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from PySide6.QtWidgets import QApplication, QMessageBox
from PySide6.QtCore import Qt
from PySide6.QtGui import QFont, QIcon, QPixmap

from ui.main_window import MainWindow


def ensure_single_instance():
    """Windows Mutex 및 윈도우 검색을 통한 단일 인스턴스 보장"""
    MUTEX_NAME = "Global\\BOM_Manager_V0_0_SingleInstance_Mutex_2021"
    
    # 1. Mutex 생성 시도
    mutex = ctypes.windll.kernel32.CreateMutexW(None, False, MUTEX_NAME)
    last_error = ctypes.windll.kernel32.GetLastError()
    ERROR_ALREADY_EXISTS = 183

    if last_error == ERROR_ALREADY_EXISTS:
        # 이미 실행 중인 인스턴스가 존재함!
        # 기존 창을 찾아서 맨 앞으로 활성화 (Restore & Bring to Front)
        try:
            def enum_cb(hwnd, _):
                if ctypes.windll.user32.IsWindowVisible(hwnd):
                    length = ctypes.windll.user32.GetWindowTextLengthW(hwnd)
                    if length > 0:
                        buff = ctypes.create_unicode_buffer(length + 1)
                        ctypes.windll.user32.GetWindowTextW(hwnd, buff, length + 1)
                        title = buff.value
                        if "BOM Manager" in title:
                            # 창 최소화 해제 및 포커스 부여
                            ctypes.windll.user32.ShowWindow(hwnd, 9) # SW_RESTORE
                            ctypes.windll.user32.SetForegroundWindow(hwnd)
                            return False
                return True

            EnumWindowsProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
            ctypes.windll.user32.EnumWindows(EnumWindowsProc(enum_cb), 0)
        except Exception:
            pass

        # 팝업 알림 표시
        app = QApplication.instance() or QApplication(sys.argv)
        msg_box = QMessageBox()
        msg_box.setWindowTitle("BOM Manager 실행 알림")
        msg_box.setIcon(QMessageBox.Warning)
        msg_box.setText("⚠️ BOM Manager가 이미 실행 중입니다.")
        msg_box.setInformativeText("프로그램이 이미 열려 있습니다. 작업 표시줄이나 화면에 열려 있는 창을 확인해 주세요.")
        msg_box.setStandardButtons(QMessageBox.Ok)
        msg_box.exec()
        sys.exit(0)

    return mutex


def parse_arguments():
    parser = argparse.ArgumentParser(description="SolidWorks 2021 BOM Manager V0.0")
    parser.add_argument(
        "--mock",
        action="store_true",
        help="Run application in Mock mode with sample CAD assembly data (no SolidWorks required)"
    )
    return parser.parse_args()


def main():
    args = parse_arguments()

    # 중복 실행 방지 체크
    mutex_handle = ensure_single_instance()

    # High DPI 스케일링 설정
    QApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough
    )

    app = QApplication(sys.argv)
    app.setApplicationName("BOM Manager V0.0")
    app.setOrganizationName("CAD Automation")

    # 기본 폰트 설정
    default_font = QFont("Segoe UI", 10)
    app.setFont(default_font)

    try:
        window = MainWindow(mock_mode=args.mock)
        window.show()
        sys.exit(app.exec())
    except Exception as e:
        import traceback
        err_msg = traceback.format_exc()
        print(f"치명적 오류 발생:\n{err_msg}", file=sys.stderr)
        QMessageBox.critical(None, "실행 오류", f"프로그램 실행 중 치명적 오류가 발생했습니다:\n\n{str(e)}")
        sys.exit(1)


if __name__ == "__main__":
    main()
