"""Pixel-space backdrop samples must remain absolute in the native adapter."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('brush_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class BrushViewboxTests(unittest.TestCase):
    def test_actual_retained_glass_mutations_preserve_pixel_units_and_null_assertion(self):
        source = (Path(__file__).resolve().parents[3] / 'Roche_Scoreboard/Views/OperatorGlassSurface.cs').read_text(encoding='utf-8-sig')
        actual = translator.map_native_api(source)
        self.assertIn('_liveBrush!.SourceRect = new RelativeRect(bounds, RelativeUnit.Absolute);', actual)
        self.assertIn('_frostBrush.SourceRect = new RelativeRect(new Rect(size), RelativeUnit.Absolute);', actual)
        self.assertNotRegex(actual, r'\.Viewbox\b')
        self.assertNotRegex(actual, r'SourceRect[^;\n]*RelativeUnit.Relative')

    def test_retained_mutations_require_the_explicit_absolute_initializer(self):
        source = '''class OperatorGlassSurface {
_liveBrush=new VisualBrush(source){ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
_liveBrush!.Viewbox = new Rect(location, new Size(width, height));
}'''
        actual = translator.native_retained_glass_viewboxes(source)
        self.assertIn('_liveBrush!.SourceRect = new RelativeRect(new Rect(location, new Size(width, height)), RelativeUnit.Absolute);', actual)
        for changed in (source.replace('BrushMappingMode.Absolute', 'BrushMappingMode.RelativeToBoundingBox'),
                        source.replace('ViewboxUnits=BrushMappingMode.Absolute,', ''),
                        source + '\n_liveBrush.ViewboxUnits=BrushMappingMode.RelativeToBoundingBox;'):
            with self.subTest(source=changed), self.assertRaisesRegex(ValueError, 'Viewbox units'):
                translator.native_retained_glass_viewboxes(changed)

    def test_retained_glass_rewrite_is_scoped_and_preserves_literals_and_comments(self):
        source = '''class OperatorGlassSurface {
_liveBrush=new VisualBrush(source){ViewboxUnits=BrushMappingMode.Absolute};
// _liveBrush!.Viewbox=ignored;
var text = "_liveBrush.Viewbox=ignored;";
other.Viewbox=bounds;
_liveBrush!.Viewbox=bounds;
}'''
        actual = translator.native_retained_glass_viewboxes(source)
        self.assertIn('// _liveBrush!.Viewbox=ignored;', actual)
        self.assertIn('"_liveBrush.Viewbox=ignored;"', actual)
        self.assertIn('other.Viewbox=bounds;', actual)
        self.assertEqual(1, actual.count('RelativeUnit.Absolute'))
        unrelated = source.replace('class OperatorGlassSurface', 'class Other')
        self.assertEqual(unrelated, translator.native_retained_glass_viewboxes(unrelated))

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
