"""Window type adaptation must not leak native implementation names into UI copy."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('window_copy_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class WindowCopyTranslationTests(unittest.TestCase):
    def test_types_change_but_action_status_labels_remain_human(self):
        source = 'Window? owner = new Window(); string status = open ? "Window open" : "Window closed";'
        actual = translator.native_main_window(source)
        self.assertIn('NativeWindow? owner = new NativeWindow();', actual)
        self.assertIn('open ? "Window open" : "Window closed"', actual)
        self.assertNotIn('"NativeWindow', actual)

    def test_verbatim_escaped_and_interpolated_copy_is_preserved(self):
        source = 'Window owner; var a = @"Window ""details"""; var b = "Window \\"details\\""; var c = $"Window {label}";'
        actual = translator.native_main_window(source)
        self.assertEqual(source.replace('Window owner;', 'NativeWindow owner;'), actual)

    def test_comments_and_character_literals_do_not_become_type_references(self):
        source = "// Window lifecycle\nWindow owner; /* Window copy */ char quote = '\"';"
        actual = translator.native_main_window(source)
        self.assertEqual(source.replace('Window owner;', 'NativeWindow owner;'), actual)
