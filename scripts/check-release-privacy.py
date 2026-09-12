"""Inspect rebuilt release files and ZIP contents; never print matched values.

Inno installers must ALSO be unpacked and their payload inspected with --directory.
Scanning only the compressed executable cannot validate its embedded payload.
"""
import argparse
import importlib.util
import pathlib
import sys
import zipfile

spec = importlib.util.spec_from_file_location('repository_privacy', pathlib.Path(__file__).with_name('check-repository-privacy.py'))
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)

PRIVATE_NAMES = {'cookies', 'cookies-journal', 'login data', 'local state',
                 'credentials.json', 'queue-v1.json', 'portable.flag', 'session.json',
                 'sessions.json', 'inventory.json', 'appsettings.local.json'}
PRIVATE_SUFFIXES = {'.pdb', '.db', '.sqlite', '.sqlite3', '.log', '.dmp', '.pfx', '.p12', '.pyc', '.binlog'}

def inspect(name, data):
    path = pathlib.PurePosixPath(name.replace('\\', '/'))
    issues = privacy.inspect(str(path), data)
    if path.name.lower() in PRIVATE_NAMES or path.suffix.lower() in PRIVATE_SUFFIXES:
        issues.append('private runtime data or symbols in release')
    if any(p.lower() in {'webview2', 'ebwebview', 'sessions', 'testresults', '.git'} for p in path.parts):
        issues.append('private/generated directory in release')
    return issues

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--directory', type=pathlib.Path)
    group.add_argument('--zip', type=pathlib.Path)
    args = parser.parse_args()
    count = 0
    failures = []
    def check(name, data):
        nonlocal count
        count += 1
        failures.extend(f'{name}: {reason}' for reason in inspect(name, data))
    if args.directory:
        root = args.directory.resolve(strict=True)
        for path in sorted(root.rglob('*')):
            if path.is_symlink():
                failures.append('Release contains a symlink; inspection refused.')
            elif path.is_file():
                check(path.relative_to(root).as_posix(), path.read_bytes())
    else:
        with zipfile.ZipFile(args.zip) as archive:
            seen = set()
            for entry in archive.infolist():
                path = pathlib.PurePosixPath(entry.filename.replace('\\', '/'))
                if path.is_absolute() or '..' in path.parts or ':' in entry.filename or entry.filename.casefold() in seen:
                    failures.append('Archive contains an unsafe or duplicate member.')
                    continue
                seen.add(entry.filename.casefold())
                if not entry.is_dir():
                    check(entry.filename, archive.read(entry))
    if failures:
        print('\n'.join(failures), file=sys.stderr)
        return 1
    print(f'Release privacy checks passed for {count} files (including binary strings).')
    return 0

if __name__ == '__main__':
    sys.exit(main())
