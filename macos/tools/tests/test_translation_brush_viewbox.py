"""Pixel-space backdrop samples must remain absolute in the native adapter."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('brush_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class BrushViewboxTests(unittest.TestCase):
    def test_actual_live_backdrop_and_frost_pixel_rectangles_stay_absolute(self):
        source = '''var live = new VisualBrush(source) { ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(location,size),Stretch=Stretch.Fill };
var frost = new VisualBrush(panel) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size), Stretch = Stretch.Fill };'''
        actual = translator.map_native_api(source)
        self.assertIn('SourceRect = new RelativeRect(new Rect(location,size), RelativeUnit.Absolute)', actual)
        self.assertIn('SourceRect = new RelativeRect(new Rect(size), RelativeUnit.Absolute)', actual)
        self.assertNotIn('RelativeUnit.Relative', actual)

    def test_snapshot_nested_rectangle_uses_same_absolute_contract(self):
        actual = translator.map_native_api('new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(location, new Size(width,height)), Stretch = Stretch.Fill }')
        self.assertIn('SourceRect = new RelativeRect(new Rect(location, new Size(width,height)), RelativeUnit.Absolute)', actual)

    def test_explicit_relative_rectangles_keep_normalized_units(self):
        actual = translator.map_native_api('new VisualBrush(source) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect(0, 0, 1, 1) }')
        self.assertIn('SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative)', actual)
        self.assertNotIn('RelativeUnit.Absolute', actual)


if __name__ == '__main__':
    unittest.main()
