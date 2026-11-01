"""WPF nullable capture queries retain their null semantics in native code."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('capture_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class CaptureQueryTranslationTests(unittest.TestCase):
    def test_nullable_capture_query_preserves_null_and_current_capture(self):
        self.assertEqual(
            'if ((_pressedTarget is null ? (bool?)null : NativePointer.IsCaptureWithin(_pressedTarget)) != true) ReleasePress();',
            translator.map_native_api('if (_pressedTarget?.IsMouseCaptureWithin != true) ReleasePress();'))

    def test_direct_capture_query_keeps_existing_native_route(self):
        self.assertEqual('bool captured = NativePointer.IsCaptureWithin(target);',
                         translator.map_native_api('bool captured = target.IsMouseCaptureWithin;'))
