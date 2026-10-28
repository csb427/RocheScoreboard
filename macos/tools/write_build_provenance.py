#!/usr/bin/env python3
"""Record local build inputs without treating an uncommitted port as a Git commit."""
import datetime
import hashlib
import json
import os
import platform
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[2]
MAC=ROOT/'macos'

def git(*args):
    try:return subprocess.check_output(['git',*args],cwd=ROOT,stderr=subprocess.DEVNULL).decode().strip()
    except (OSError,subprocess.CalledProcessError):return None

def main():
    paths=[]
    for directory in (ROOT/'Roche_Scoreboard',MAC):
        for path in directory.rglob('*'):
            if not path.is_file() or {'bin','obj','TestResults','__pycache__'}.intersection(path.parts):continue
            if path.suffix.lower() not in {'.cs','.csproj','.xaml','.axaml','.json','.png','.ico','.svg','.py','.sh','.manifest'}:continue
            if path.name in {'build-provenance.json','port-state.json'}:continue
            paths.append(path)
    digest=hashlib.sha256()
    for path in sorted(paths):
        digest.update(str(path.relative_to(ROOT)).encode()+b'\0'+hashlib.sha256(path.read_bytes()).digest())
    status=git('status','--porcelain')
    baseline=json.loads((MAC/'port-state.json').read_text())['source_baseline']
    result={'format_version':1,'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'windows_baseline':baseline,'workspace_branch':git('branch','--show-current') or os.environ.get('GITHUB_REF_NAME'),
            'workspace_head':git('rev-parse','HEAD'),'uncommitted_changes':bool(status) if status is not None else None,
            'github_run_id':os.environ.get('GITHUB_RUN_ID'),'build_platform':platform.system(),'build_architecture':platform.machine(),
            'input_file_count':len(paths),'input_sha256':digest.hexdigest(),
            'native_mac_acceptance_verified':False,
            'notes':'Preview from recorded source inputs; not a verified native Mac desktop release.'}
    (MAC/'build-provenance.json').write_text(json.dumps(result,indent=2)+'\n')
    print('Recorded input SHA-256 '+digest.hexdigest())

if __name__=='__main__':main()
