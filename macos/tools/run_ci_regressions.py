#!/usr/bin/env python3
"""Run every discovered case with bounded process lifetimes and exact TRX checks.

This is assertion evidence in isolated test hosts, not production single-process
stability or hands-on native desktop acceptance. No failed job is retried.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path('macos/RocheScoreboard.Mac.Tests/RocheScoreboard.Mac.Tests.csproj')
LARGE_CLASS = 'RocheScoreboard.Mac.Tests.LiveSettingsClockChoiceTests'
PREFLIGHT = [
    'HeadlessSystemMotionPreferenceTests', 'RecoveryDiscardMotionTests',
    'SharedPreferenceMotionTests', 'WindowTests', 'PresentationLifetimeTests',
    'AppleDesignRefinementTests', 'AflStoppageMotionTests', 'ScoreEventCorrectionFlowTests',
]
NAMESPACE = 'RocheScoreboard.Mac.Tests.'
FQN = re.compile(r'[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+')
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    path = Path(path)
    if path.is_symlink():
        return hashlib.sha256(('symlink:' + os.readlink(path)).encode()).hexdigest()
    h = hashlib.sha256()
    with path.open('rb') as f:
        for data in iter(lambda: f.read(1024 * 1024), b''):
            h.update(data)
    return h.hexdigest()


def save(path, data):
    path = Path(path)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(data, indent=2, sort_keys=True) + '\n')
    temporary.replace(path)


def parse_discovery(text):
    marker = re.search(r'The following Tests are available:', text, re.I)
    if marker is None:
        raise ValueError('No test discovery header')
    cases = []
    for line in text[marker.end():].splitlines():
        name = line.strip()
        if not name:
            continue
        method = name.split('(', 1)[0]
        if not name.startswith(NAMESPACE) or not FQN.fullmatch(method):
            raise ValueError('Unexpected discovery line: ' + name)
        cases.append(name)
    if not cases or len(cases) != len(set(cases)):
        raise ValueError('Empty or duplicate case discovery')
    return sorted(cases)


def partition(cases):
    groups = defaultdict(list)
    for case in cases:
        method = case.split('(', 1)[0]
        cls = method.rsplit('.', 1)[0]
        key = ('method', method) if cls == LARGE_CLASS else ('class', cls)
        groups[key].append(case)
    jobs = []
    for (kind, identity), names in sorted(groups.items()):
        short = identity.rsplit('.', 1)[-1][:48]
        job_id = kind + '-' + short + '-' + hashlib.sha256(identity.encode()).hexdigest()[:12]
        jobs.append({'id': job_id, 'kind': kind, 'identity': identity,
                     'filter': 'FullyQualifiedName' + ('=' + identity if kind == 'method' else '~' + identity + '.'),
                     'expected': sorted(names)})
    if Counter(case for j in jobs for case in j['expected']) != Counter(cases):
        raise ValueError('Partition lost or duplicated a case')
    front = []
    for cls in PREFLIGHT:
        matching = [j for j in jobs if j['identity'] == NAMESPACE + cls]
        if len(matching) != 1:
            raise ValueError('Missing targeted preflight class: ' + cls)
        front.append(matching[0])
    used = {j['id'] for j in front}
    remaining = sorted((j for j in jobs if j['id'] not in used),
                       key=lambda j: (-len(j['expected']), j['id']))
    return front, remaining


def check_trx(path, expected):
    root = ET.parse(path).getroot()
    rows = root.findall('.//t:UnitTestResult', NS)
    actual = [r.get('testName', '') for r in rows]
    counters = root.find('.//t:Counters', NS)
    counters = {} if counters is None else counters.attrib
    count = len(expected)
    coverage = Counter(actual) == Counter(expected)
    consistent = all(counters.get(k) == str(count) for k in ('total', 'executed', 'passed'))
    consistent = consistent and all(counters.get(k, '0') == '0' for k in (
        'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
        'notRunnable', 'notExecuted', 'disconnected', 'warning', 'inProgress', 'pending'))
    nonpassed = [{'name': r.get('testName'), 'outcome': r.get('outcome')}
                 for r in rows if r.get('outcome') != 'Passed']
    return {'actual': actual, 'counters': counters, 'exact_coverage': coverage,
            'nonpassed': nonpassed, 'passed': coverage and consistent and not nonpassed,
            'sha256': digest(path)}


def changed(snapshot):
    return [name for name, value in snapshot.items()
            if not Path(name).is_file() or digest(name) != value]


def terminate(proc):
    try:
        os.killpg(proc.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    return proc.wait()


def run_job(job, output, captures, snapshot, dotnet, timeout):
    directory = output / job['id']
    directory.mkdir()
    capture = captures / job['id']
    capture.mkdir(parents=True)
    env = os.environ.copy()
    env['ROCHE_CAPTURE_DIR'] = str(capture)
    command = [dotnet, 'test', str(PROJECT), '-c', 'Release', '--no-build', '--no-restore',
               '--filter', job['filter'], '--logger', 'console;verbosity=normal',
               '--logger', 'trx;LogFileName=results.trx', '--results-directory', str(directory),
               '--blame-hang-timeout', '3m', '--blame-hang-dump-type', 'none']
    result = {'job': job, 'command': command, 'started_utc': utc(), 'passed': False,
              'phase': 'running', 'pre_changed_inputs': changed(snapshot)}
    receipt = directory / 'receipt.json'
    save(receipt, result)
    print('START', job['id'], 'cases=' + str(len(job['expected'])), flush=True)
    start = time.monotonic()
    proc = None
    try:
        if result['pre_changed_inputs']:
            raise ValueError('Frozen inputs changed before job')
        with (directory / 'console.log').open('x') as log:
            proc = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log,
                                    stderr=subprocess.STDOUT, start_new_session=True)
            try:
                result['exit_code'] = proc.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                result['timed_out'] = True
                result['exit_code'] = terminate(proc)
        trx = directory / 'results.trx'
        result['trx'] = check_trx(trx, job['expected']) if trx.exists() else None
        result['post_changed_inputs'] = changed(snapshot)
        result['passed'] = (result['exit_code'] == 0 and not result.get('timed_out', False)
                            and result['trx'] is not None and result['trx']['passed']
                            and not result['post_changed_inputs'])
    except Exception as error:
        if proc is not None and proc.poll() is None:
            terminate(proc)
        result['error'] = type(error).__name__ + ': ' + str(error)
    result.update(phase='completed', finished_utc=utc(), seconds=time.monotonic() - start)
    save(receipt, result)
    print('END', job['id'], 'PASS' if result['passed'] else 'FAIL',
          'seconds=' + str(round(result['seconds'], 2)), flush=True)
    if not result['passed']:
        tail = (directory / 'console.log')
        if tail.exists():
            print('\n'.join(tail.read_text(errors='replace').splitlines()[-70:]), flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected-cases', required=True, type=int)
    parser.add_argument('--output', type=Path, default=Path('artifacts/checks/isolated'))
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--job-timeout-seconds', type=int, default=1200)
    args = parser.parse_args()
    os.chdir(ROOT)
    if args.expected_cases < 1 or args.job_timeout_seconds < 1:
        parser.error('Counts and timeouts must be positive')
    output = args.output.resolve()
    output.mkdir(parents=True)  # Refuse an existing output namespace.
    summary = {'started_utc': utc(), 'all_passed': False,
               'evidence': 'Process-isolated assertions; not native desktop/single-process acceptance'}
    try:
        head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
        summary['head'] = head
        if os.environ.get('GITHUB_SHA'):
            if head != os.environ['GITHUB_SHA']:
                raise ValueError('Workflow SHA differs from checked-out commit')
            if subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip():
                raise ValueError('CI worktree is dirty before build')
        build = [args.dotnet, 'build', str(PROJECT), '-c', 'Release', '-m:1', '-nr:false',
                 '-p:UseSharedCompilation=false']
        print('BUILD', flush=True)
        with (output / 'build.log').open('x') as log:
            subprocess.run(build, stdout=log, stderr=subprocess.STDOUT, check=True)
        discovery = subprocess.run([args.dotnet, 'test', str(PROJECT), '-c', 'Release',
                                   '--no-build', '--no-restore', '--list-tests'],
                                  text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=True)
        (output / 'discovery.txt').write_text(discovery.stdout)
        cases = parse_discovery(discovery.stdout)
        if len(cases) != args.expected_cases:
            raise ValueError(f'Expected {args.expected_cases} cases, discovered {len(cases)}')
        preflight, remaining = partition(cases)
        paths = subprocess.check_output(['git', 'ls-files', '-z']).decode().split('\0')
        source = {str(ROOT / path) for path in paths if path}
        source.add(str(Path(__file__).resolve()))
        binaries = list((ROOT / PROJECT.parent / 'bin/Release/net10.0').glob('RocheScoreboard*.dll'))
        if len(binaries) != 3:
            raise ValueError('Expected app/core/tests executed binaries')
        snapshot = {path: digest(path) for path in sorted(source | {str(p) for p in binaries})}
        captures = Path(os.environ.get('ROCHE_CAPTURE_DIR', str(output / 'captures'))).resolve()
        captures.mkdir(parents=True, exist_ok=True)
        plan = {'head': head, 'expected': cases, 'preflight': preflight,
                'remaining': remaining, 'source_and_binary_hashes': snapshot,
                'max_hosts': 2, 'no_retries': True}
        save(output / 'plan.json', plan)
        print(f'DISCOVERED {len(cases)} cases; {len(preflight)} targeted jobs first; '
              f'{len(remaining)} further jobs; maximum2hosts', flush=True)
        results = []
        for job in preflight:
            result = run_job(job, output, captures, snapshot, args.dotnet, args.job_timeout_seconds)
            results.append(result)
            if not result['passed']:
                break
        if len(results) == len(preflight) and all(r['passed'] for r in results):
            print('TARGETED PREFLIGHT PASSED; proceeding to remaining original cases', flush=True)
            iterator = iter(remaining)
            stopped = False
            with ThreadPoolExecutor(max_workers=2) as pool:
                active = {}
                def launch():
                    job = next(iterator, None)
                    if job is None:
                        return False
                    future = pool.submit(run_job, job, output, captures, snapshot,
                                         args.dotnet, args.job_timeout_seconds)
                    active[future] = job
                    return True
                launch(); launch()
                while active:
                    done, _ = wait(active, return_when=FIRST_COMPLETED)
                    for future in done:
                        active.pop(future)
                        result = future.result()
                        results.append(result)
                        stopped |= not result['passed']
                    if not stopped:
                        while len(active) < 2 and launch():
                            pass
        actual = [name for r in results if r.get('trx') for name in r['trx']['actual']]
        complete = Counter(actual) == Counter(cases)
        final_changed = changed(snapshot)
        summary.update(finished_utc=utc(), expected_count=len(cases),
                       actual_count=len(actual), exact_case_coverage=complete,
                       jobs_completed=len(results), jobs_planned=len(preflight) + len(remaining),
                       targeted_preflight_passed=(len(results) >= len(preflight)
                           and all(r['passed'] for r in results[:len(preflight)])),
                       nonpassed_jobs=[r['job']['id'] for r in results if not r['passed']],
                       changed_inputs=final_changed,
                       all_passed=complete and all(r['passed'] for r in results) and not final_changed)
    except Exception as error:
        summary.update(finished_utc=utc(), error=type(error).__name__ + ': ' + str(error))
    save(output / 'summary.json', summary)
    print(json.dumps(summary, indent=2), flush=True)
    return 0 if summary['all_passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
