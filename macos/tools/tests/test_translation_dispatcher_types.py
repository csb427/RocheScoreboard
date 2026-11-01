"""Dispatcher types must not be rewritten as application dispatcher values."""
import importlib.util
from pathlib import Path
import unittest
SPEC = importlib.util.spec_from_file_location('dispatcher_type_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)

class DispatcherTypeTests(unittest.TestCase):
    def test_qualified_generic_type_is_not_instance(self):
        text = 'class Example { Func<int, Avalonia.Threading.Dispatcher, object> Factory; }'
        result = translator.map_native_api(text)
        self.assertIn('Func<int, Avalonia.Threading.Dispatcher, object>', result)
        self.assertNotIn('Dispatcher.UIThread, object>', result)

    def test_qualified_alias_remains_type(self):
        text = 'using UiDispatcher = Avalonia.Threading.Dispatcher; class Example {}'
        self.assertIn('using UiDispatcher = Avalonia.Threading.Dispatcher;', translator.map_native_api(text))

    def test_real_dispatcher_argument_still_uses_ui_thread(self):
        result = translator.map_native_api('class Example { void Start() { Work(Dispatcher, value); Work(owner.Dispatcher, value); } }')
        self.assertEqual(2, result.count('Work(Dispatcher.UIThread, value)'))
