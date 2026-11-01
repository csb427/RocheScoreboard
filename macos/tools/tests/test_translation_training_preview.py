"""Preserve the Training owner's decision guard before child Enter handlers."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('training_preview_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)

class TrainingPreviewTranslationTests(unittest.TestCase):
    def test_training_owner_preview_is_registered_once_in_the_tunnel(self):
        for registration in ['PreviewKeyDown += OnPanelPreviewKeyDown;', 'this.PreviewKeyDown  +=  OnPanelPreviewKeyDown;']:
            with self.subTest(registration=registration):
                source='public partial class TrainingControlPanel : UserControl {\n    '+registration+'\n}'
                result=translator.map_native_api(source)
                expected='AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnPanelPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);'
                self.assertEqual(1,result.count(expected))
                self.assertNotIn('KeyDown += OnPanelPreviewKeyDown;',result)
                self.assertNotIn('handledEventsToo',result)

    def test_unrelated_owner_and_child_registrations_are_unchanged(self):
        other='class OtherPanel : UserControl {\n    PreviewKeyDown += OnPanelPreviewKeyDown;\n}'
        self.assertEqual(other.replace('PreviewKeyDown +=','KeyDown +='),translator.map_native_api(other))
        child='class TrainingControlPanel : UserControl {\n    child.PreviewKeyDown += OnPanelPreviewKeyDown;\n}'
        self.assertEqual(child.replace('PreviewKeyDown +=','KeyDown +='),translator.map_native_api(child))
