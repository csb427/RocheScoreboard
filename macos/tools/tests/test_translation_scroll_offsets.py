"""Nullable WPF scroll offsets retain null propagation in native code."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('scroll_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class ScrollOffsetTranslationTests(unittest.TestCase):
    def test_nullable_scroll_offset_uses_native_vector_component(self):
        self.assertEqual('double offset = scroll?.Offset.Y ?? 0;',
                         translator.map_native_api('double offset = scroll?.VerticalOffset ?? 0;'))

    def test_direct_scroll_offset_stays_supported(self):
        self.assertEqual('double offset = scroll.Offset.Y;',
                         translator.map_native_api('double offset = scroll.VerticalOffset;'))
