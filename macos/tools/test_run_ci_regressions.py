import tempfile
from pathlib import Path
import unittest

from run_ci_regressions import (LARGE_CLASS, NAMESPACE, PREFLIGHT, changed,
                                check_trx, digest, parse_discovery, partition)


class RegressionRunnerTests(unittest.TestCase):
    def test_discovery_requires_real_unique_fully_qualified_cases(self):
        text = 'build output\nThe following Tests are available:\n    ' + NAMESPACE + 'Example.Works\n'
        self.assertEqual([NAMESPACE + 'Example.Works'], parse_discovery(text))
        for bad in ['no header', text + '    ' + NAMESPACE + 'Example.Works\n',
                    'The following Tests are available:\n    Other.Namespace.Works\n']:
            with self.assertRaises(ValueError):
                parse_discovery(bad)

    def test_partition_preserves_all_theory_rows_and_prioritizes_targeted_proof(self):
        cases = [NAMESPACE + name + '.Check' for name in PREFLIGHT]
        cases += [LARGE_CLASS + '.Choice(value: True)', LARGE_CLASS + '.Choice(value: False)',
                  NAMESPACE + 'AnotherClass.Other']
        front, rest = partition(cases)
        self.assertEqual([NAMESPACE + name for name in PREFLIGHT], [j['identity'] for j in front])
        self.assertCountEqual(cases, [c for j in front + rest for c in j['expected']])
        method = next(j for j in rest if j['kind'] == 'method')
        self.assertEqual('FullyQualifiedName=' + LARGE_CLASS + '.Choice', method['filter'])
        self.assertEqual(2, len(method['expected']))
        self.assertTrue(all(j['filter'].endswith('.') for j in front))

    def test_missing_preflight_class_is_an_error_not_a_silent_exclusion(self):
        with self.assertRaises(ValueError):
            partition([NAMESPACE + 'AnotherClass.Other'])

    def write_trx(self, folder, rows, total=None, passed=None, skipped=0):
        total = len(rows) if total is None else total
        passed = len(rows) if passed is None else passed
        results = ''.join(f'<UnitTestResult testName="{name}" outcome="{outcome}" />'
                          for name, outcome in rows)
        path = Path(folder) / 'result.trx'
        path.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                        f'<Results>{results}</Results><ResultSummary><Counters total="{total}" '
                        f'executed="{total}" passed="{passed}" failed="0" notExecuted="{skipped}" />'
                        '</ResultSummary></TestRun>')
        return path

    def test_exact_passed_results_have_a_verified_receipt(self):
        with tempfile.TemporaryDirectory() as folder:
            path = self.write_trx(folder, [('A', 'Passed'), ('B', 'Passed')])
            result = check_trx(path, ['A', 'B'])
            self.assertTrue(result['passed'])
            self.assertEqual(digest(path), result['sha256'])

    def test_duplicate_or_missing_results_cannot_pass(self):
        with tempfile.TemporaryDirectory() as folder:
            for rows in [[('A', 'Passed')], [('A', 'Passed'), ('A', 'Passed')],
                         [('A', 'Passed'), ('C', 'Passed')]]:
                self.assertFalse(check_trx(self.write_trx(folder, rows), ['A', 'B'])['passed'])

    def test_skipped_failed_and_inconsistent_counters_cannot_pass(self):
        with tempfile.TemporaryDirectory() as folder:
            for rows, kwargs in [([('A', 'NotExecuted')], {}), ([('A', 'Failed')], {}),
                                 ([('A', 'Passed')], {'skipped': 1}),
                                 ([('A', 'Passed')], {'passed': 0}),
                                 ([('A', 'Passed')], {'total': 2})]:
                path = self.write_trx(folder, rows, **kwargs)
                self.assertFalse(check_trx(path, ['A'])['passed'])

    def test_source_changes_and_missing_inputs_are_reported(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'source.cs'
            path.write_text('original')
            frozen = {str(path): digest(path)}
            self.assertEqual([], changed(frozen))
            path.write_text('modified')
            self.assertEqual([str(path)], changed(frozen))
            path.unlink()
            self.assertEqual([str(path)], changed(frozen))


if __name__ == '__main__':
    unittest.main()
