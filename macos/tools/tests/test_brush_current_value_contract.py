"""Source-contract regression only; does not claim an Avalonia runtime test."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]

class BrushCurrentValueContractTests(unittest.TestCase):
    def test_current_value_clone_copies_effective_values_into_new_mutable_brush(self):
        source = (ROOT / 'macos/RocheScoreboard.Mac/Native/Images.cs').read_text(encoding='utf-8')
        self.assertRegex(source, r'public static SolidColorBrush CloneCurrentValue\(this SolidColorBrush brush\)\s*=>\s*new\(brush.Color,\s*brush.Opacity\);')

    def test_generated_countdown_has_adapter_namespace_and_current_value_calls(self):
        source = (ROOT / 'macos/RocheScoreboard.Mac/Native/Generated/SetupWizard.axaml.cs').read_text(encoding='utf-8')
        self.assertIn('using RocheScoreboard.Mac.Native;', source)
        self.assertEqual(3, source.count('.CloneCurrentValue()'))
