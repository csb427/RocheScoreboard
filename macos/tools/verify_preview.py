#!/usr/bin/env python3
"""Verify local Mac preview ZIP structure; this does not run macOS acceptance."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import plistlib
import struct
import zipfile

def verify(path,cpu):
    digest=hashlib.sha256(path.read_bytes()).hexdigest()
    assert path.with_suffix('.zip.sha256').read_text().split()[0]==digest,'Archive digest differs'
    prefix='Roche Scoreboard.app/Contents/'
    with zipfile.ZipFile(path) as archive:
        names=archive.namelist();assert archive.testzip() is None,'Archive CRC failure'
        assert all(not name.startswith('/') and '..' not in PurePosixPath(name).parts for name in names),'Unsafe archive path'
        for relative in ['MacOS/RocheScoreboard.Mac','MacOS/RocheScoreboard.Mac.dll','MacOS/libcoreclr.dylib','MacOS/libhostfxr.dylib','Resources/BuildProvenance.json','Resources/ThirdParty/VLC/COPYING','Resources/ThirdParty/VLC/COPYING.LIB']:
            assert prefix+relative in names,'Missing '+relative
        for name in ['Prepare Roche Scoreboard.command','sign-mac.sh','Read me.txt','Mac acceptance.md','PORT_VERIFICATION.md','PLATFORM_BEHAVIOR_AUDIT.md']:
            assert name in names,'Missing '+name
        for name in [prefix+'MacOS/RocheScoreboard.Mac','Prepare Roche Scoreboard.command','sign-mac.sh']:
            assert (archive.getinfo(name).external_attr>>16)&0o111,'Missing executable bit: '+name
        plist=plistlib.loads(archive.read(prefix+'Info.plist'))
        assert plist['CFBundleShortVersionString']=='5.0.0' and plist['RocheBuildChannel']=='preview'
        assert struct.unpack('<II',archive.read(prefix+'MacOS/RocheScoreboard.Mac')[:8])==(0xfeedfacf,cpu),'Wrong apphost architecture'
        assert struct.unpack('<II',archive.read(prefix+'Frameworks/vlc/lib/libvlc.dylib')[:8])==(0xfeedfacf,cpu),'Wrong VLC architecture'
        plugins=[name for name in names if name.startswith(prefix+'Frameworks/vlc/plugins/') and name.endswith('.dylib')]
        assert len(plugins)>100,'Incomplete VLC plugins'
        provenance=json.loads(archive.read(prefix+'Resources/BuildProvenance.json'))
        assert provenance['native_mac_acceptance_verified'] is False
        if expected_commit:=os.environ.get('GITHUB_SHA'):
            assert provenance['workspace_head']==expected_commit,'Preview does not match the workflow commit'
        return {'file':path.name,'bytes':path.stat().st_size,'sha256':digest,'entries':len(names),'vlc_plugins':len(plugins),'input_sha256':provenance['input_sha256'],'source_commit':provenance['workspace_head'],'archive_checks_passed':True,'native_mac_executed':False}

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    parser.add_argument('--rid',choices=['osx-arm64','osx-x64']);args=parser.parse_args()
    packages=[verify(args.directory/('Roche-Scoreboard-Mac-Preview-'+rid+'.zip'),cpu) for rid,cpu in [('osx-arm64',0x0100000c),('osx-x64',0x01000007)] if args.rid is None or rid==args.rid]
    assert len({package['input_sha256'] for package in packages})==1,'Build inputs differ between architectures'
    result={'format_version':1,'packages':packages,'native_mac_acceptance_verified':False}
    (args.directory/'artifact-manifest.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))

if __name__=='__main__':main()
