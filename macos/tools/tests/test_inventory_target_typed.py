"""Proposed scanner regressions only. Not executed or installed by reviewer.

Root can integrate these cases into macos/tools/tests/test_inventory_operator_motion.py
once the frozen application run finishes. No application runtime is required.
"""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest


def _module_path():
    override = os.environ.get('ROCHE_INVENTORY_MODULE')
    if override:
        return Path(override)
    candidates = (
        Path(__file__).resolve().parents[1] / 'inventory_operator_motion.py',
        Path.cwd() / 'macos/tools/inventory_operator_motion.py',
        Path('/workspace/RocheScoreboard/macos/tools/inventory_operator_motion.py'),
    )
    return next(path for path in candidates if path.is_file())


SPEC = importlib.util.spec_from_file_location('target_typed_inventory', _module_path())
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)


class TargetTypedInventoryTests(unittest.TestCase):
    def scan(self, body, declarations=''):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            views = root / 'Roche_Scoreboard/Views'
            views.mkdir(parents=True)
            (views / 'Example.cs').write_text(
                'public class Example : UserControl {\n' + body + '\n}\n' + declarations + '\n'
            )
            previous = inventory.ROOT, inventory.SRC
            try:
                inventory.ROOT, inventory.SRC = root, root / 'Roche_Scoreboard'
                entries, _ = inventory.scan()
                return entries
            finally:
                inventory.ROOT, inventory.SRC = previous

    @staticmethod
    def constructions(entries, type_name):
        return [e for e in entries if e['type'] == type_name and e['kind'] in (
            'dynamic/factory', 'dynamic visual', 'implicit factory'
        )]

    def test_actual_score_editor_target_typed_custom_window_is_recorded(self):
        entries = self.scan('''private void OpenEditor() {
ScoreEventEditorWindow editor = new(ev, home, away)
{ Owner = this };
editor.ShowDialog();
}''', 'public class ScoreEventEditorWindow : Window {}')
        sites = self.constructions(entries, 'ScoreEventEditorWindow')
        self.assertEqual(1, len(sites))
        self.assertEqual('Views/Example.cs::OpenEditor::ScoreEventEditorWindow:1', sites[0]['id'])
        self.assertEqual(3, sites[0]['line'])
        self.assertIn('ScoreEventEditorWindow editor = new(', sites[0]['current_source']['snippet'])
        self.assertEqual(1, len(self.constructions(entries, 'ShowDialog')))

    def test_actual_team_library_arguments_and_owner_do_not_hide_construction(self):
        entries = self.scan('''private void BrowseTeamLibrary(bool home) {
TeamLibraryWindow browser = new(home ? "Home" : "Away", TeamLibrarySport.Cricket)
{ Owner = Window.GetWindow(this) };
}''', 'public class TeamLibraryWindow : Window {}')
        sites = self.constructions(entries, 'TeamLibraryWindow')
        self.assertEqual(1, len(sites))
        self.assertEqual('Views/Example.cs::BrowseTeamLibrary::TeamLibraryWindow:1', sites[0]['id'])

    def test_xaml_backed_custom_type_is_included_without_csharp_base_clause(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            views = root / 'Roche_Scoreboard/Views'
            views.mkdir(parents=True)
            (views / 'Example.cs').write_text('public class Example : UserControl {\nprivate void Build() {\nCustomView item = new();\n}\n}')
            (views / 'CustomView.xaml').write_text('<UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Example.CustomView"/>')
            previous = inventory.ROOT, inventory.SRC
            try:
                inventory.ROOT, inventory.SRC = root, root / 'Roche_Scoreboard'
                entries, _ = inventory.scan()
            finally:
                inventory.ROOT, inventory.SRC = previous
        self.assertEqual(1, len(self.constructions(entries, 'CustomView')))

    def test_noninteractive_border_and_local_material_field_are_visual_sites(self):
        entries = self.scan('''private readonly OperatorGlassSurface _navigation = new()
{ Padding = new Thickness(12) };
private void BuildRow() {
Border accentStripe = new() { Width = 4 };
}''', 'public class OperatorGlassSurface : Border {}')
        material = self.constructions(entries, 'OperatorGlassSurface')
        border = self.constructions(entries, 'Border')
        self.assertEqual(1, len(material))
        self.assertEqual(1, len(border))
        self.assertEqual('dynamic visual', material[0]['kind'])
        self.assertEqual('dynamic visual', border[0]['kind'])

    def test_native_file_picker_target_typed_constructor_is_recorded(self):
        entries = self.scan('''private void Browse() {
OpenFileDialog picker = new() { Filter = "Image|*.png" };
picker.ShowDialog();
}''')
        self.assertEqual(1, len(self.constructions(entries, 'OpenFileDialog')))
        self.assertEqual(1, len(self.constructions(entries, 'ShowDialog')))

    def test_actual_scrollviewer_get_only_auto_property_initializer_is_recorded(self):
        entries = self.scan('''public ScrollViewer Scroller { get; } = new()
{ VerticalScrollBarVisibility = ScrollBarVisibility.Auto };''')
        sites = self.constructions(entries, 'ScrollViewer')
        self.assertEqual(1, len(sites))
        self.assertEqual('Views/Example.cs::initialization::ScrollViewer:1', sites[0]['id'])
        self.assertEqual(2, sites[0]['line'])

    def test_existing_button_declaration_has_one_site_and_keeps_its_exact_id(self):
        entries = self.scan('''private void Build() {
Button existing = new();
CustomView newView = new();
}''', 'public class CustomView : UserControl {}')
        buttons = self.constructions(entries, 'Button')
        self.assertEqual(1, len(buttons))
        self.assertEqual('Views/Example.cs::Build::Button:1', buttons[0]['id'])
        self.assertEqual(1, len(self.constructions(entries, 'CustomView')))
        self.assertEqual(len(entries), len({e['id'] for e in entries}))

    def test_comments_literals_and_non_ui_model_types_create_no_new_ui_sites(self):
        entries = self.scan('''private void Build() {
// TeamLibraryWindow browser = new();
var text = "Border accent = new()";
var verbatim = @"OpenFileDialog picker = new()";
/* ScrollViewer Scroller { get; } = new(); */
TrainingTimer timer = new();
TimeSpan duration = new(0, 2, 0);
}''', 'public class TeamLibraryWindow : Window {}\npublic class TrainingTimer {}')
        self.assertFalse([e for e in entries if e['kind'] in ('dynamic/factory', 'dynamic visual', 'implicit factory')])

    def test_all_declarators_share_the_declared_ui_type(self):
        entries = self.scan('private readonly Border first = new() { Tag = (1, 2) }, second = new();')
        sites = self.constructions(entries, 'Border')
        self.assertEqual(2, len(sites))
        self.assertEqual({'first', 'second'}, {site['name'] for site in sites})
        self.assertEqual(2, len({site['id'] for site in sites}))
        self.assertEqual({'field/property initializer'}, {site['current_source']['scope'] for site in sites})

    def test_earlier_new_site_does_not_renumber_an_existing_same_kind_site(self):
        entries = self.scan('''private void Build() {
Border added = new();
var original = new Border();
}''')
        sites = {site['id']: site for site in self.constructions(entries, 'Border')}
        self.assertEqual(2, len(sites))
        self.assertEqual(4, sites['Views/Example.cs::Build::Border:1']['line'])
        self.assertEqual(3, sites['Views/Example.cs::Build::Border:2']['line'])

    def test_single_line_method_local_keeps_its_actual_method_scope(self):
        entries = self.scan('private void Build() { CustomView child = new(); }',
                            'public class CustomView : UserControl {}')
        site = self.constructions(entries, 'CustomView')[0]
        self.assertEqual('Views/Example.cs::Build::CustomView:1', site['id'])
        self.assertEqual('local initializer', site['current_source']['scope'])

    def test_compact_expression_bodied_ui_factory_is_recorded(self):
        entries = self.scan('private static OperatorSegmentedPanel Segments()=>new(){ Tag = 1 };')
        sites = self.constructions(entries, 'OperatorSegmentedPanel')
        self.assertEqual(1, len(sites))
        self.assertEqual('implicit factory', sites[0]['kind'])

    def test_multiline_target_typed_arguments_are_counted_on_declaration_line(self):
        entries = self.scan('''private void Open() {
CustomView view = new
(
value
);
}''', 'public class CustomView : UserControl {}')
        sites = self.constructions(entries, 'CustomView')
        self.assertEqual(1, len(sites))
        self.assertEqual(3, sites[0]['line'])


if __name__ == '__main__':
    unittest.main()
