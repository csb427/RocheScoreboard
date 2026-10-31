"""OUTSIDE adapter regression draft. UNRUN; imports do not regenerate files.
Root may move into macos/tools/tests and use the normal adjacent tool locator.
"""
import importlib.util
import os
from pathlib import Path
import unittest

ROOT = Path(os.environ.get('ROCHE_REPO_ROOT', '/workspace/RocheScoreboard'))
SPEC = importlib.util.spec_from_file_location('glass_read_translator', ROOT / 'macos/tools/translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


def mapped(source):
    source = translator.native_event_subscriptions(source)
    for old, new in translator.REPLACEMENTS.items():
        source = source.replace(old, new)
    return translator.map_native_api(source)


class GlassReadCompatibilityTests(unittest.TestCase):
    def test_every_real_renderer_brush_read_accepts_the_public_interface(self):
        code = mapped((ROOT / 'Roche_Scoreboard/Views/OperatorGlassSurface.cs').read_text())
        for read in (
            'Background is ISolidColorBrush { Color.A: 0 }',
            'BorderBrush is ISolidColorBrush { Color.A: 0 }',
            'is ISolidColorBrush surface && surface.Color.R>180',
            'Background switch { ISolidColorBrush solid => solid.Color',
            'IGradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color',
            'is ISolidColorBrush opaque)colour=opaque.Color',
            'owner.GetValue(TextBlock.ForegroundProperty) as ISolidColorBrush',
        ):
            self.assertIn(read, code)

    def test_real_mutable_construction_and_gradient_population_are_preserved(self):
        code = mapped((ROOT / 'Roche_Scoreboard/Views/OperatorGlassSurface.cs').read_text())
        self.assertIn('new SolidColorBrush(colour)', code)
        self.assertIn('var brush=new RadialGradientBrush{', code)
        self.assertIn('var brush=new LinearGradientBrush {', code)
        self.assertIn('brush.GradientStops.Add(new GradientStop(', code)
        self.assertNotIn('new ISolidColorBrush', code)
        self.assertNotIn('new IGradientBrush', code)

    def test_other_classes_keep_their_existing_concrete_read_contract(self):
        source = '''class Other {
 bool Empty => Background is SolidColorBrush { Color.A: 0 };
 object Read() => Background switch { SolidColorBrush solid => solid.Color,
 GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color, _ => null };
 object Ink() => owner.GetValue(TextBlock.ForegroundProperty) as SolidColorBrush;
}'''
        code = mapped(source)
        self.assertIn('Background is SolidColorBrush { Color.A: 0 }', code)
        self.assertIn('SolidColorBrush solid => solid.Color', code)
        self.assertIn('GradientBrush gradient when', code)
        self.assertIn('as SolidColorBrush', code)
        self.assertNotIn('ISolidColorBrush', code)
        self.assertNotIn('IGradientBrush', code)

    def test_renderer_string_and_comment_payloads_are_not_type_rewritten(self):
        source = '''class OperatorGlassSurface {
 string Hint = "Background is SolidColorBrush { Color.A: 0 }";
 string Example = "SolidColorBrush solid => solid.Color";
 // owner.GetValue(TextBlock.ForegroundProperty) as SolidColorBrush
 /* GradientBrush gradient when gradient.GradientStops.Count > 0 */
 bool Empty => Background is SolidColorBrush { Color.A: 0 };
}'''
        code = mapped(source)
        self.assertIn('string Hint = "Background is SolidColorBrush { Color.A: 0 }";', code)
        self.assertIn('string Example = "SolidColorBrush solid => solid.Color";', code)
        self.assertIn('// owner.GetValue(TextBlock.ForegroundProperty) as SolidColorBrush', code)
        self.assertIn('/* GradientBrush gradient when gradient.GradientStops.Count > 0 */', code)
        self.assertIn('Background is ISolidColorBrush { Color.A: 0 };', code)

    def test_renderer_mutation_site_and_concrete_cast_are_not_broadened(self):
        source = '''class OperatorGlassSurface {
 void Mutate() {
   var owned = new SolidColorBrush(); owned.Color = Colors.White;
   if (brush is SolidColorBrush editable) editable.Color = Colors.Black;
   var mutable = (SolidColorBrush)Background; mutable.Color = Colors.White;
 }
 bool Empty => Background is SolidColorBrush { Color.A: 0 };
}'''
        code = mapped(source)
        self.assertIn('new SolidColorBrush()', code)
        self.assertIn('brush is SolidColorBrush editable', code)
        self.assertIn('(SolidColorBrush)Background', code)
        self.assertIn('Background is ISolidColorBrush { Color.A: 0 };', code)


if __name__ == '__main__':
    unittest.main()
