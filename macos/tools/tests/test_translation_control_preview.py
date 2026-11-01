"""Native control feedback must retain WPF preview routing and exact teardown."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('control_preview_translator', Path(__file__).parents[1] / 'translate_production.py')
translator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(translator)


class ControlPreviewTranslationTests(unittest.TestCase):
    def test_control_motion_preview_handlers_tunnel_even_when_native_control_handles_input(self):
        source = '''class OperatorControlMotion {
control.PreviewMouseLeftButtonDown += Down; control.PreviewMouseLeftButtonUp += Up;
control.PreviewKeyDown += KeyDown; control.PreviewKeyUp += KeyUp;
c.PreviewMouseLeftButtonDown -= Down; c.PreviewMouseLeftButtonUp -= Up;
c.PreviewKeyDown -= KeyDown; c.PreviewKeyUp -= KeyUp;
private void Down(object sender, PointerEventArgs e) { _pressed=true; }
private void Up(object sender, PointerEventArgs e) { _pressed=false; }
}'''
        result = translator.map_native_api(source)
        for event, handler in [('PointerPressed','Down'),('PointerReleased','Up'),('KeyDown','KeyDown'),('KeyUp','KeyUp')]:
            with self.subTest(event=event):
                self.assertIn(f'control.AddHandler(Avalonia.Input.InputElement.{event}Event, {handler}, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true)', result)
                self.assertIn(f'c.RemoveHandler(Avalonia.Input.InputElement.{event}Event, {handler})', result)
                self.assertNotIn(f'control.{event} +=', result)
        self.assertIn('PointerUpdateKind.LeftButtonPressed', result)
        self.assertIn('PointerUpdateKind.LeftButtonReleased', result)

    def test_ordinary_pointer_subscription_outside_this_adapter_is_not_rewritten(self):
        source = 'class Other { control.PointerPressed += Down; c.PointerPressed -= Down; }'
        self.assertEqual(source, translator.map_native_api(source))
