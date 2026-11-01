"""Constructor adaptation must preserve array element types and unrelated names."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('control_array_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class ControlArrayTests(unittest.TestCase):
    def test_border_array_keeps_declared_base_type_for_factory_values(self):
        code = translator.map_native_api('class Example { Border[] rows = new Border[3]; Border Make() => new(); }')
        self.assertIn('new Border[3]', code)
        self.assertNotIn('new NativeBorder[', code)

    def test_button_array_keeps_declared_base_type(self):
        code = translator.map_native_api('class Example { Button[] actions = new Button[2]; Button Make() => new(); }')
        self.assertIn('new Button[2]', code)
        self.assertNotIn('new NativeButton[', code)

    def test_real_border_constructors_still_use_adapter(self):
        code = translator.map_native_api('class Example { Border first = new Border(); Border second = new Border { Width = 12 }; }')
        self.assertIn('new NativeBorder()', code)
        self.assertIn('new NativeBorder {', code)

    def test_real_button_constructors_still_use_adapter(self):
        code = translator.map_native_api('class Example { Button first = new Button(); Button second = new Button { Width = 12 }; }')
        self.assertIn('new NativeButton()', code)
        self.assertIn('new NativeButton {', code)

    def test_longer_type_names_are_not_border_constructors(self):
        code = translator.map_native_api('class Example { object value = new BorderAnimationState(); }')
        self.assertIn('new BorderAnimationState()', code)
        self.assertNotIn('new NativeBorderAnimationState', code)
