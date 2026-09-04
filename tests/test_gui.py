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
        # Verify modified cell background is amber
        mat_cell = window.table_widget.item(1, 4)
        self.assertEqual(mat_cell.background().color().name().upper(), "#FEF3C7")

        # Edit Part Name on row 1 (level 1) using delegate logic
        from PySide6.QtWidgets import QLineEdit
        from PySide6.QtCore import QModelIndex
        editor = window.table_widget.part_name_delegate.createEditor(window.table_widget, None, window.table_widget.model().index(1, 3))
        window.table_widget.part_name_delegate.setEditorData(editor, window.table_widget.model().index(1, 3))
        self.assertEqual(editor.text(), "BASE_FRAME", "Editor must contain pure part name without '    └  ' prefix")
        editor.setText("BASE_FRAME_NEW")
        window.table_widget.part_name_delegate.setModelData(editor, window.table_widget.model(), window.table_widget.model().index(1, 3))
        self.assertEqual(window.table_widget.item(1, 3).text(), "    └  BASE_FRAME_NEW")
        self.assertEqual(window.table_widget.bom_items[1].part_name, "BASE_FRAME_NEW")
        self.assertEqual(window.table_widget.item(1, 3).background().color().name().upper(), "#FEF3C7")

        # Simulate Apply (save) -> background should return to default
        window.sw_connector.apply_properties_to_solidworks([window.table_widget.bom_items[1]])
        window.table_widget.load_items(window.table_widget.bom_items)
        self.assertFalse(window.table_widget.bom_items[1].is_modified)
        self.assertEqual(window.table_widget.item(1, 4).background().color().name().upper(), "#000000" if window.table_widget.item(1, 4).background().style().name == "NoBrush" else window.table_widget.item(1, 4).background().color().name().upper())

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
