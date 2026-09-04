import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from core.bom_model import BOMItem, AssemblyInfo
from core.sw_connector import SolidWorksConnector
from utils.exporter import BOMExporter

class TestBOMManager(unittest.TestCase):

    def test_bom_item_creation_and_modifications(self):
        item = BOMItem(
            item_no=1,
            part_name="BASE_PLATE",
            material="AL6061",
            qty=2,
            remark="Anodizing Black",
            file_path=r"C:\CAD\BASE_PLATE.SLDPRT"
        )
        self.assertEqual(item.file_name, "BASE_PLATE.SLDPRT")
        self.assertEqual(item.part_name, "BASE_PLATE")
        self.assertFalse(item.check_modified())

        # Modify material
        item.material = "SUS304"
        self.assertTrue(item.check_modified())

        # Reset material
        item.material = "AL6061"
        self.assertFalse(item.check_modified())

    def test_mock_sw_connector(self):
        connector = SolidWorksConnector(mock_mode=True)
        connected, msg = connector.connect()
        self.assertTrue(connected)

        assy_info = connector.get_active_assembly_info()
        self.assertTrue(assy_info.is_connected)
        self.assertEqual(assy_info.title, "PUMP_UNIT_ASSY.SLDASM")

        items, err = connector.load_bom()
        self.assertIsNone(err)
        self.assertGreater(len(items), 0)
        self.assertEqual(items[0].part_name, "PUMP_UNIT_ASSY")
        self.assertTrue(items[0].is_subassembly)
        self.assertEqual(items[0].level, 0)
        self.assertEqual(items[1].part_name, "BASE_FRAME")
        self.assertFalse(items[1].is_subassembly)
        self.assertEqual(items[1].level, 1)

        # Test transparency isolation of a child part (items[3] is MOTOR_BRACKET, items[2] is parent MOTOR_MODULE_ASSY)
        connector.set_components_transparency([items[3]], items)
        self.assertTrue(items[3].is_opaque, "Target child part MOTOR_BRACKET must be opaque")
        self.assertFalse(items[2].is_opaque, "Parent subassembly MOTOR_MODULE_ASSY must NOT be opaque")
        self.assertFalse(items[0].is_opaque, "Root assembly PUMP_UNIT_ASSY must NOT be opaque")
        self.assertFalse(items[4].is_opaque, "Sibling part MAIN_SHAFT_D25 must NOT be opaque")

        # Test transparency isolation of a subassembly (items[2] is MOTOR_MODULE_ASSY)
        connector.set_components_transparency([items[2]], items)
        self.assertTrue(items[2].is_opaque, "Target subassembly MOTOR_MODULE_ASSY must be opaque")
        self.assertTrue(items[3].is_opaque, "Child part MOTOR_BRACKET under subassembly must be opaque")
        self.assertTrue(items[4].is_opaque, "Child part MAIN_SHAFT_D25 under subassembly must be opaque")
        self.assertFalse(items[1].is_opaque, "Outside part BASE_FRAME must NOT be opaque")
        self.assertFalse(items[5].is_opaque, "Outside part IMPELLER_HOUSING must NOT be opaque")

        # Test leaf name extraction
        from core.sw_connector import _extract_leaf_name
        self.assertEqual(_extract_leaf_name("MOTOR_MODULE_ASSY-1/MOTOR_BRACKET-2"), "motor_bracket")
        self.assertEqual(_extract_leaf_name("MOTOR_BRACKET-1@MOTOR_MODULE_ASSY-1"), "motor_bracket")
        self.assertEqual(_extract_leaf_name("BASE_FRAME-1"), "base_frame")

        # Test apply
        items[1].material = "AL7075"
        success_cnt, fail_cnt, errors = connector.apply_properties_to_solidworks(items)
        self.assertEqual(success_cnt, len(items))
        self.assertEqual(fail_cnt, 0)

    def test_exporter_excel_and_csv(self):
        items = [
            BOMItem(1, "PART_A", "AL6061", 2, "Note 1", "PART_A.SLDPRT", r"C:\CAD\PART_A.SLDPRT"),
            BOMItem(2, "PART_B", "SUS304", 4, "Note 2", "PART_B.SLDPRT", r"C:\CAD\PART_B.SLDPRT"),
        ]
        test_dir = os.path.join(os.path.dirname(__file__), "output")
        os.makedirs(test_dir, exist_ok=True)

        excel_path = os.path.join(test_dir, "test_bom.xlsx")
        csv_path = os.path.join(test_dir, "test_bom.csv")

        # Export Excel
        res_excel = BOMExporter.export_to_excel(items, excel_path, "TEST_ASSY")
        self.assertTrue(res_excel)
        self.assertTrue(os.path.exists(excel_path))

        # Export CSV
        res_csv = BOMExporter.export_to_csv(items, csv_path)
        self.assertTrue(res_csv)
        self.assertTrue(os.path.exists(csv_path))

if __name__ == "__main__":
    unittest.main()
