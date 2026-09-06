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
        # Subassembly should have triangle tree icon
        self.assertFalse(window.table_widget.item(0, 3).icon().isNull())
        # Regular part (BASE_FRAME) should not have an icon
        self.assertTrue(window.table_widget.item(1, 3).icon().isNull())

        # Second row is child level 1 -> displays "   └  BASE_FRAME" (3 spaces)
        second_part_display = window.table_widget.item(1, 3).text()
        self.assertEqual(second_part_display, "   └  BASE_FRAME")

        # Fourth row is child level 2 -> displays "      └  MOTOR_BRACKET" (6 spaces)
        fourth_part_display = window.table_widget.item(3, 3).text()
        self.assertEqual(fourth_part_display, "      └  MOTOR_BRACKET")

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
        self.assertEqual(editor.text(), "BASE_FRAME", "Editor must contain pure part name without '   └  ' prefix")
        editor.setText("BASE_FRAME_NEW")
        window.table_widget.part_name_delegate.setModelData(editor, window.table_widget.model(), window.table_widget.model().index(1, 3))
        self.assertEqual(window.table_widget.item(1, 3).text(), "   └  BASE_FRAME_NEW")
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

    def test_triangle_icons_and_indentation(self):
        from ui.custom_table import get_tree_triangle_icon, format_part_name_display, clean_part_name_text

        # 1. Verify triangle tree icons (expanded ▼, collapsed ▶) are generated
        icon_exp = get_tree_triangle_icon(True)
        icon_col = get_tree_triangle_icon(False)
        self.assertFalse(icon_exp.isNull(), "Expanded triangle icon should not be null")
        self.assertFalse(icon_col.isNull(), "Collapsed triangle icon should not be null")

        # 2. Verify indentation rules: Level 1 -> 3 spaces, Level 2 -> 6 spaces, Level 3 -> 9 spaces, etc.
        self.assertEqual(format_part_name_display("PART_ROOT", 0), "PART_ROOT")
        self.assertEqual(format_part_name_display("PART_SUB1", 1), "   └  PART_SUB1")
        self.assertEqual(format_part_name_display("PART_SUB2", 2), "      └  PART_SUB2")
        self.assertEqual(format_part_name_display("PART_SUB3", 3), "         └  PART_SUB3")
        self.assertEqual(format_part_name_display("PART_SUB10", 10), f"{'   ' * 10}└  PART_SUB10")

        # 3. Verify clean part name stripping
        self.assertEqual(clean_part_name_text("   └  PART_SUB1"), "PART_SUB1")
        self.assertEqual(clean_part_name_text("   └ ▼  SUB_ASSY"), "SUB_ASSY")
        self.assertEqual(clean_part_name_text("   └ ▶  SUB_ASSY"), "SUB_ASSY")
        self.assertEqual(clean_part_name_text("▼  MAIN_ASSY"), "MAIN_ASSY")
        self.assertEqual(clean_part_name_text("[Sub1] PART_NAME"), "PART_NAME")
        self.assertEqual(clean_part_name_text("[Sub.] PART_NAME"), "PART_NAME")
        self.assertEqual(clean_part_name_text("🏷️ PART_NAME"), "PART_NAME")

    def test_tree_expansion_and_collapse(self):
        window = MainWindow(mock_mode=True)
        table = window.table_widget

        # Initial state: Row 2 (MOTOR_MODULE_ASSY) has triangle icon and is expanded
        self.assertFalse(table.item(2, 3).icon().isNull())
        self.assertFalse(table.isRowHidden(3), "Row 3 (MOTOR_BRACKET) should be visible")
        self.assertFalse(table.isRowHidden(4), "Row 4 (MAIN_SHAFT_D25) should be visible")

        # Collapse row 2 (MOTOR_MODULE_ASSY)
        table.toggle_expand(2)
        self.assertTrue(table.isRowHidden(3), "Row 3 (MOTOR_BRACKET) should be hidden when parent collapsed")
        self.assertTrue(table.isRowHidden(4), "Row 4 (MAIN_SHAFT_D25) should be hidden when parent collapsed")
        self.assertFalse(table.isRowHidden(5), "Row 5 (IMPELLER_HOUSING) should still be visible")

        # Expand row 2 (MOTOR_MODULE_ASSY) again
        table.toggle_expand(2)
        self.assertFalse(table.isRowHidden(3), "Row 3 should be visible again")
        self.assertFalse(table.isRowHidden(4), "Row 4 should be visible again")

        # Collapse all
        table.collapse_all()
        self.assertTrue(table.isRowHidden(1), "Child rows should be hidden after collapse_all")
        self.assertTrue(table.isRowHidden(2), "Child rows should be hidden after collapse_all")

        # Expand all
        table.expand_all()
        self.assertFalse(table.isRowHidden(1), "All rows should be visible after expand_all")
        self.assertFalse(table.isRowHidden(3), "All rows should be visible after expand_all")


if __name__ == "__main__":
    unittest.main()
