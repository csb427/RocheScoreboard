"""Qualified WPF visibility constants must translate to valid bool syntax."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class QualifiedVisibilityTests(unittest.TestCase):
    def test_global_constants_are_complete_tokens(self):
        for state, expected in [('Visible', 'true'), ('Collapsed', 'false'), ('Hidden', 'false')]:
            with self.subTest(state=state):
                code = f'field.Visibility = global::System.Windows.Visibility.{state};'
                for old, new in translator.REPLACEMENTS.items():
                    code = code.replace(old, new)
                code = translator.map_native_api(code)
                self.assertEqual(f'field.IsVisible = {expected};', code)


if __name__ == '__main__':
    unittest.main()
