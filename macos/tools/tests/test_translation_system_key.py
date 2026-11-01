"""WPF's normalized Alt/System key idiom must work in every native partial."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('system_key_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class SystemKeyTranslationTests(unittest.TestCase):
    def test_standard_wpf_normalization_is_generic(self):
        for argument in ['e', 'args']:
            with self.subTest(argument=argument):
                source = f'Key key = {argument}.Key == Key.System ? {argument}.SystemKey : {argument}.Key;'
                self.assertEqual(f'Key key = {argument}.Key;', translator.map_native_api(source))

    def test_multiline_normalization_preserves_real_alt_modifier_guard(self):
        source = 'Key key = input.Key == Key.System\n ? input.SystemKey : input.Key;\nif (key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;'
        result = translator.map_native_api(source)
        self.assertIn('Key key = input.Key;', result)
        self.assertNotIn('SystemKey', result)
        self.assertIn('NativeKeyboard.Modifiers & KeyModifiers.Alt', result)

    def test_ordinary_key_access_is_unchanged(self):
        self.assertEqual('Key key = e.Key;', translator.map_native_api('Key key = e.Key;'))
