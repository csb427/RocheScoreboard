"""Nullable brush wrappers must retain their annotation in typed native properties."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('nullable_brush_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


def translate(source):
    for old, new in translator.REPLACEMENTS.items():
        source = source.replace(old, new)
    return translator.map_native_api(source)


class NullableBrushTranslationTests(unittest.TestCase):
    def test_optional_brush_annotation_matches_wrapper_without_changing_registration(self):
        source = '''public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(Surface), new FrameworkPropertyMetadata(null));
public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }'''
        result = translate(source)
        self.assertIn('AttachedProperty<Brush?> FillBrushProperty = (AttachedProperty<Brush?>)', result)
        self.assertIn('typeof(Brush)', result)
        self.assertIn('new FrameworkPropertyMetadata(null)', result)
        self.assertIn('set => SetValue(FillBrushProperty, value)', result)

    def test_required_brush_and_value_type_properties_are_not_inferred_nullable(self):
        source = '''public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(Surface), new FrameworkPropertyMetadata(Brushes.Black));
public Brush FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
public static readonly DependencyProperty EnabledProperty = DependencyProperty.Register(nameof(Enabled), typeof(bool), typeof(Surface), new FrameworkPropertyMetadata(false));
public bool? Enabled { get => (bool?)GetValue(EnabledProperty); set => SetValue(EnabledProperty, value); }'''
        result = translate(source)
        self.assertIn('AttachedProperty<Brush> FillBrushProperty', result)
        self.assertIn('AttachedProperty<bool> EnabledProperty', result)
        self.assertNotIn('AttachedProperty<bool?>', result)
