"""Actual WPF control-size bindings must target Avalonia's arranged Bounds."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('dimension_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class DimensionBindingTests(unittest.TestCase):
    def translate(self, body):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            (source / 'Views').mkdir(parents=True)
            (source / 'Views/Example.xaml').write_text(
                '<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" '
                'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Example"><Grid>'
                + body + '</Grid></UserControl>')
            previous = translator.ROOT, translator.SRC, translator.OUT
            try:
                translator.ROOT, translator.SRC, translator.OUT = root, source, root / 'output'
                translator.translate_markup('Example')
                return ET.parse(root / 'output/Example.axaml').getroot()
            finally:
                translator.ROOT, translator.SRC, translator.OUT = previous

    def test_compact_and_explicit_paths_map_control_dimensions(self):
        root = self.translate('''<Border Width="{Binding ActualWidth, RelativeSource={RelativeSource AncestorType=ComboBox}}"/>
<Border Height="{Binding Path=ActualHeight, ElementName=Row}"/>
<Border MinWidth="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=ActualWidth}"/>''')
        borders = [node for node in root.iter() if node.tag.endswith('}Border')]
        self.assertEqual('{Binding Bounds.Width, RelativeSource={RelativeSource AncestorType=ComboBox}}', borders[0].get('Width'))
        self.assertEqual('{Binding Path=Bounds.Height, ElementName=Row}', borders[1].get('Height'))
        self.assertEqual('{Binding RelativeSource={RelativeSource TemplatedParent}, Path=Bounds.Width}', borders[2].get('MinWidth'))

    def test_literals_and_other_binding_parameters_are_preserved(self):
        root = self.translate('''<TextBlock Text="ActualWidth and ActualHeight"/>
<Border Tag="{Binding Tag, ElementName=ActualWidth, ConverterParameter=ActualHeight}"/>''')
        text = next(node for node in root.iter() if node.tag.endswith('}TextBlock'))
        border = next(node for node in root.iter() if node.tag.endswith('}Border'))
        self.assertEqual('ActualWidth and ActualHeight', text.get('Text'))
        self.assertEqual('{Binding Tag, ElementName=ActualWidth, ConverterParameter=ActualHeight}', border.get('Tag'))

    def test_quoted_parameters_are_not_binding_paths(self):
        root = self.translate('''<Border Tag="{Binding Tag, ConverterParameter='literal, Path=ActualWidth'}"/>
<Border Tag="{Binding Tag, ConverterParameter='literal {Binding ActualHeight}'}"/>
<Border Tag="{Binding ConverterParameter='a}b, Path=ActualHeight', Path=ActualWidth}"/>''')
        borders = [node for node in root.iter() if node.tag.endswith('}Border')]
        self.assertEqual("{Binding Tag, ConverterParameter='literal, Path=ActualWidth'}", borders[0].get('Tag'))
        self.assertEqual("{Binding Tag, ConverterParameter='literal {Binding ActualHeight}'}", borders[1].get('Tag'))
        self.assertEqual("{Binding ConverterParameter='a}b, Path=ActualHeight', Path=Bounds.Width}", borders[2].get('Tag'))

    def test_element_binding_path_maps_without_renaming_its_source(self):
        root = self.translate('''<Border><Border.Width><Binding Path="ActualWidth" ElementName="ActualHeight"/></Border.Width></Border>''')
        binding = next(node for node in root.iter() if node.tag.endswith('}Binding'))
        self.assertEqual('Bounds.Width', binding.get('Path'))
        self.assertEqual('ActualHeight', binding.get('ElementName'))

    def test_combo_popup_reports_pointer_closure_to_its_owner(self):
        root = self.translate('''<ControlTemplate TargetType="ComboBox"><Grid>
<Popup IsOpen="{TemplateBinding IsDropDownOpen}"/></Grid></ControlTemplate>
<ControlTemplate TargetType="Button"><Popup IsOpen="{TemplateBinding IsDropDownOpen}"/></ControlTemplate>''')
        popups = [node for node in root.iter() if node.tag.endswith('}Popup')]
        self.assertEqual('{TemplateBinding IsDropDownOpen, Mode=TwoWay}', popups[0].get('IsOpen'))
        self.assertEqual('{TemplateBinding IsDropDownOpen}', popups[1].get('IsOpen'))

    def test_explicit_popup_binding_modes_are_preserved(self):
        root = self.translate('''<ControlTemplate TargetType="ComboBox"><Grid>
<Popup IsOpen="{TemplateBinding IsDropDownOpen, Mode=TwoWay}"/>
<Popup IsOpen="{TemplateBinding IsDropDownOpen, Mode=OneWay}"/></Grid></ControlTemplate>''')
        popups = [node for node in root.iter() if node.tag.endswith('}Popup')]
        self.assertEqual('{TemplateBinding IsDropDownOpen, Mode=TwoWay}', popups[0].get('IsOpen'))
        self.assertEqual('{TemplateBinding IsDropDownOpen, Mode=OneWay}', popups[1].get('IsOpen'))


if __name__ == '__main__':
    unittest.main()
