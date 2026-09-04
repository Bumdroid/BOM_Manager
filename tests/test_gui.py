import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Offscreen QPA platform for headless testing
os.environ["QT_QPA_PLATFORM"] = "offscreen"

from PySide6.QtWidgets import QApplication
from ui.main_window import MainWindow

class TestBOMManagerGUI(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication(sys.argv)

    def test_main_window_mock_mode(self):
        window = MainWindow(mock_mode=True)
        self.assertIsNotNone(window)
        self.assertEqual(window.table_widget.rowCount(), 10)

        # Check table contents (Part Name is col 3)
        first_part_name = window.table_widget.item(0, 3).text()
        self.assertEqual(first_part_name, "PUMP_UNIT_ASSY")
        # Subassembly should have assy icon
        self.assertFalse(window.table_widget.item(0, 3).icon().isNull())

        # Second row is child level 1 -> displays "    └  BASE_FRAME"
        second_part_display = window.table_widget.item(1, 3).text()
        self.assertEqual(second_part_display, "    └  BASE_FRAME")

        # Edit a cell in table (Material is col 4 on row 1)
        window.table_widget.item(1, 4).setText("SUS")
        self.assertTrue(window.table_widget.bom_items[1].is_modified)

        # Filter test
        window.table_widget.filter_items("SHAFT")
        # Row 4 (MAIN_SHAFT_D25) should not be hidden
        self.assertFalse(window.table_widget.isRowHidden(4))
        # Row 1 (BASE_FRAME) should be hidden
        self.assertTrue(window.table_widget.isRowHidden(1))

        # Clear filter
        window.table_widget.filter_items("")
        self.assertFalse(window.table_widget.isRowHidden(1))

if __name__ == "__main__":
    unittest.main()
