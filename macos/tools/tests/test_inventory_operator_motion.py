"""Scanner regressions; no application or user files are touched."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('inventory', Path(__file__).parents[1] / 'inventory_operator_motion.py')
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)


class InventoryScannerTests(unittest.TestCase):
    def scan(self, body):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            views = root / 'Roche_Scoreboard' / 'Views'
            views.mkdir(parents=True)
            source = views / 'Example.cs'
            source.write_text('public class Example : UserControl {\n' + body + '\n}\n')
            previous = inventory.ROOT, inventory.SRC
            try:
                inventory.ROOT, inventory.SRC = root, root / 'Roche_Scoreboard'
                entries, _ = inventory.scan()
                return entries
            finally:
                inventory.ROOT, inventory.SRC = previous

    def test_constructor_brace_on_next_line_is_counted_at_the_constructor(self):
        entries = self.scan('''public void Build() {
var button = new Button
{ Content = "Start" };
var symbol = new OperatorSymbol
{ Kind = "transport" };
}''')
        sites = [e for e in entries if e['type'] in ('Button', 'OperatorSymbol')]
        self.assertEqual(['Button', 'OperatorSymbol'], [e['type'] for e in sites])
        self.assertEqual([3, 5], [e['line'] for e in sites])
        self.assertTrue(all('::Build::' in e['id'] for e in sites))

    def test_implicit_constructor_arguments_can_begin_on_next_line(self):
        entries = self.scan('''public void Build() {
Button button = new
();
}''')
        self.assertEqual(1, len([e for e in entries if e['type'] == 'Button']))

    def test_comments_and_strings_are_not_controls_or_local_factories(self):
        entries = self.scan('''public void Build() {
// Ignore shortcuts when typing in a TextBox
if (isTyping) return;
var example = "new Button()";
/* new OperatorSymbol() */
var verbatim = @"new TextBox()";
// class Ghost : Window {}
}''')
        self.assertFalse([e for e in entries if e['type'] in ('if', 'Button', 'TextBox', 'OperatorSymbol')])
        self.assertFalse([e for e in entries if e['name'] == 'Ghost'])

    def test_multiline_factory_call_is_recorded_but_its_declaration_is_not(self):
        entries = self.scan('''public void Build() {
MakeButton
();
}
private Button MakeButton() {
return new Button();
}''')
        calls = [e for e in entries if e['type'] == 'MakeButton']
        self.assertEqual(1, len(calls))
        self.assertEqual(3, calls[0]['line'])
        self.assertIn('::Build::', calls[0]['id'])

    def test_custom_rendered_surface_is_inventoried_independently_of_its_uses(self):
        entries = self.scan('public class Glyph : FrameworkElement {}')
        self.assertEqual(1, len([e for e in entries if e['kind'] == 'code surface' and e['name'] == 'Glyph']))
        self.assertEqual(len(entries),len({e['id'] for e in entries}))


if __name__ == '__main__':
    unittest.main()
