"""Saved expectations must fail read-only checks when source identity drifts."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('audit', Path(__file__).parents[1] / 'audit_parity.py')
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class SavedSourceContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / 'contract.json'
        self.contract = {'reference': 'Roche_Scoreboard', 'xaml': [
            {'source': 'Roche_Scoreboard/Screen.xaml', 'sha256': 'xaml-hash', 'handlers': []}],
            'code': [{'source': 'Roche_Scoreboard/Screen.cs', 'sha256': 'code-hash', 'lines': 5, 'native_libraries': []}]}
        self.path.write_text(json.dumps(self.contract), encoding='utf-8')

    def check(self, contract):
        before = self.path.read_bytes()
        result = audit.check_saved_contract(contract, self.path)
        self.assertEqual(before, self.path.read_bytes(), 'A check must not rewrite its expectations.')
        return '\n'.join(result)

    def test_exact_identity_passes_without_rewriting_formatting(self):
        self.assertEqual('', self.check(self.contract))

    def test_inventory_has_platform_independent_case_sensitive_posix_order(self):
        root = Path(self.temporary.name)
        source = root / 'Roche_Scoreboard'
        source.mkdir()
        for name in ('Scorebug', 'ScoreEvent', 'alpha', 'Zulu'):
            (source / (name + '.cs')).write_text('class Example {}\n', encoding='utf-8')
            (source / (name + '.xaml')).write_text('<Window/>\n', encoding='utf-8')
        with mock.patch.object(audit, 'ROOT', root):
            actual = audit.inventory()
        for section, suffix in [('code', '.cs'), ('xaml', '.xaml')]:
            self.assertEqual(['Roche_Scoreboard/' + name + suffix
                              for name in ('ScoreEvent', 'Scorebug', 'Zulu', 'alpha')],
                             [row['source'] for row in actual[section]])

    def test_changed_hash_line_count_and_native_library_are_detected_independently(self):
        for field, value in [('sha256', 'changed'), ('lines', 6), ('native_libraries', ['native.dll'])]:
            with self.subTest(field=field):
                current = copy.deepcopy(self.contract)
                current['code'][0][field] = value
                self.assertIn(f'Stale saved source: Roche_Scoreboard/Screen.cs ({field})', self.check(current))

    def test_new_and_removed_source_paths_do_not_cancel_each_other(self):
        current = copy.deepcopy(self.contract)
        current['code'][0]['source'] = 'Roche_Scoreboard/NewQa.cs'
        errors = self.check(current)
        self.assertIn('Missing saved source: Roche_Scoreboard/NewQa.cs', errors)
        self.assertIn('Removed source still saved: Roche_Scoreboard/Screen.cs', errors)

    def test_xaml_hash_and_handler_changes_are_detected(self):
        for field, value in [('sha256', 'changed'), ('handlers', [{'event': 'Click', 'handler': 'Go'}])]:
            with self.subTest(field=field):
                current = copy.deepcopy(self.contract)
                current['xaml'][0][field] = value
                self.assertIn(f'Stale saved source: Roche_Scoreboard/Screen.xaml ({field})', self.check(current))

    def test_duplicate_saved_entries_fail(self):
        saved = copy.deepcopy(self.contract)
        saved['code'].append(saved['code'][0])
        self.path.write_text(json.dumps(saved), encoding='utf-8')
        self.assertIn('Duplicate saved source entries: code', self.check(self.contract))

    def test_reference_or_unknown_metadata_drift_fails(self):
        for change in [{'reference': 'Other'}, {'unexpected': True}]:
            with self.subTest(change=change):
                self.assertIn('does not match', self.check(self.contract | change))

    def test_invalid_missing_or_malformed_saved_contract_fails_without_repair(self):
        for saved in ('bad JSON', '[]', '{"code": null}', '{"code": [null]}'):
            with self.subTest(saved=saved):
                self.path.write_text(saved, encoding='utf-8')
                self.assertTrue(self.check(self.contract))
        self.path.unlink()
        self.assertIn('Cannot read saved production source contract', '\n'.join(audit.check_saved_contract(self.contract, self.path)))
        self.assertFalse(self.path.exists())

    def test_all_check_modes_reject_combining_a_refresh(self):
        for flag in ('--check-contract', '--check-translation', '--check-package'):
            with self.subTest(flag=flag), mock.patch('sys.argv', ['audit', '--write-contract', flag]), \
                    contextlib.redirect_stderr(io.StringIO()), mock.patch.object(audit, 'inventory') as inventory:
                with self.assertRaises(SystemExit) as error:
                    audit.main()
                self.assertEqual(2, error.exception.code)
                inventory.assert_not_called()

    def test_translation_and_package_modes_cannot_bypass_saved_freshness(self):
        for flag in ('--check-contract', '--check-translation', '--check-package'):
            with self.subTest(flag=flag), mock.patch('sys.argv', ['audit', flag]), \
                    mock.patch.object(audit, 'CONTRACT', self.path), \
                    mock.patch.object(audit, 'inventory', return_value=self.contract | {'reference': 'changed'}), \
                    mock.patch.object(audit, 'check_source_line_endings', return_value=[]), \
                    mock.patch.object(audit, 'check_translation', return_value=[]), \
                    mock.patch.object(audit, 'check_package', return_value=[]), \
                    contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                before = self.path.read_bytes()
                self.assertEqual(1, audit.main())
                self.assertEqual(before, self.path.read_bytes())

    def test_lf_policy_preserves_raw_hash_identity_including_bom(self):
        source = Path(self.temporary.name) / 'Source.cs'
        contract = {'xaml': [], 'code': [{'source': 'Source.cs'}]}
        with mock.patch.object(audit, 'ROOT', Path(self.temporary.name)):
            for data in (b'class Source {}\n', b'\xef\xbb\xbfclass Source {}\n'):
                source.write_bytes(data)
                self.assertEqual([], audit.check_source_line_endings(contract))
                self.assertEqual(data, source.read_bytes())
            for data in (b'class Source {}\r\n', b'class Source {}\r'):
                source.write_bytes(data)
                self.assertEqual(['Source must use LF bytes before generation/contract publication: Source.cs'],
                                 audit.check_source_line_endings(contract))
                self.assertEqual(data, source.read_bytes())

    def test_refresh_and_checks_refuse_nonportable_source_without_writing(self):
        for flag in ('--write-contract', '--check-contract', '--check-translation', '--check-package'):
            with self.subTest(flag=flag), mock.patch('sys.argv', ['audit', flag]), \
                    mock.patch.object(audit, 'CONTRACT', self.path), \
                    mock.patch.object(audit, 'inventory', return_value=self.contract), \
                    mock.patch.object(audit, 'check_source_line_endings', return_value=['Source must use LF bytes']), \
                    contextlib.redirect_stderr(io.StringIO()):
                before = self.path.read_bytes()
                self.assertEqual(1, audit.main())
                self.assertEqual(before, self.path.read_bytes())


if __name__ == '__main__':
    unittest.main()
