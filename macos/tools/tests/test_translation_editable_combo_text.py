"""Avalonia editable combo templates explicitly bind their editor Text."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('editable_combo_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class EditableComboTextTests(unittest.TestCase):
    def translate(self, body):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            (source / 'Views').mkdir(parents=True)
            (source / 'Views/Example.xaml').write_text(
                '<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" '
                'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Example"><Grid>' + body + '</Grid></UserControl>')
            previous = translator.ROOT, translator.SRC, translator.OUT
            try:
                translator.ROOT, translator.SRC, translator.OUT = root, source, root / 'output'
                translator.translate_markup('Example')
                return ET.parse(root / 'output/Example.axaml').getroot()
            finally:
                translator.ROOT, translator.SRC, translator.OUT = previous

    def test_missing_combo_editor_binding_is_two_way_for_both_template_type_spellings(self):
        for target in ('ComboBox', '{x:Type ComboBox}'):
            with self.subTest(target=target):
                result = self.translate(f'<ControlTemplate TargetType="{target}"><TextBox x:Name="PART_EditableTextBox"/></ControlTemplate>')
                editor = next(n for n in result.iter() if n.tag.endswith('}TextBox'))
                self.assertEqual('{TemplateBinding Text, Mode=TwoWay}', editor.get('Text'))

    def test_explicit_editor_binding_remains_authoritative(self):
        result = self.translate('<ControlTemplate TargetType="ComboBox"><TextBox x:Name="PART_EditableTextBox" Text="{Binding Draft, Mode=TwoWay}"/></ControlTemplate>')
        editor = next(n for n in result.iter() if n.tag.endswith('}TextBox'))
        self.assertEqual('{Binding Draft, Mode=TwoWay}', editor.get('Text'))

    def test_same_named_textbox_outside_combo_template_is_not_changed(self):
        result = self.translate('<ControlTemplate TargetType="TextBox"><TextBox x:Name="PART_EditableTextBox"/></ControlTemplate>')
        self.assertNotIn('Text', next(n for n in result.iter() if n.tag.endswith('}TextBox')).attrib)


if __name__ == '__main__':
    unittest.main()
