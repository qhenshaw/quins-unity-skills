"""
Selective .unitypackage extractor (GUIDs preserved).

usage:
  python extract_unitypackage.py <project_root> <package.unitypackage> <regex> [<regex> ...] [--dry-run] [--keep-code] [--no-deps]

Extracts every asset whose pathname matches one of the regexes (case-insensitive, searched anywhere in the path),
plus - recursively - every asset in the SAME package that those assets reference by GUID:
  - text assets (.prefab .mat .asset .controller .anim .overrideController .mask) are scanned for `guid:` references
  - every .meta is scanned too (model importers reference external materials / avatars by GUID)
References to GUIDs outside the package (built-in resources, other packages) are ignored.

By default scripts, assemblies, shaders and scenes are never extracted (.cs .asmdef .asmref .dll .shader .shadergraph
.shadersubgraph .hlsl .cginc .unity .lighting) - convert materials to your own shader instead. --keep-code disables that.
Files that already exist are skipped, so it is safe to re-run with more patterns. Folder .meta files from the package
are written for every folder created, so folder GUIDs match the package too.

A .unitypackage is a gzipped tar: <guid>/pathname, <guid>/asset, <guid>/asset.meta (folders have no asset).
"""
import os
import re
import sys
import tarfile

args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = {a for a in sys.argv[1:] if a.startswith('--')}
if len(args) < 3:
    print(__doc__)
    sys.exit(1)

root, pkg, patterns = args[0], args[1], [re.compile(p, re.I) for p in args[2:]]
dry_run = '--dry-run' in flags
follow_deps = '--no-deps' not in flags
SKIP = None if '--keep-code' in flags else re.compile(
    r'\.(cs|asmdef|asmref|dll|shader|shadergraph|shadersubgraph|hlsl|cginc|unity|lighting)$', re.I)
SCAN_EXT = ('.prefab', '.mat', '.asset', '.controller', '.anim', '.overridecontroller', '.mask')
GUID_RE = re.compile(rb'guid: ([0-9a-f]{32})')


def skipped(path):
    return SKIP is not None and SKIP.search(path)


entries = {}  # guid -> {'pathname', 'asset', 'meta'}
with tarfile.open(pkg, 'r:gz') as tf:
    for m in tf:
        parts = m.name.lstrip('./').split('/')
        if len(parts) != 2 or not m.isfile():
            continue
        guid, kind = parts
        e = entries.setdefault(guid, {})
        if kind == 'pathname':
            e['pathname'] = tf.extractfile(m).read().decode('utf8').splitlines()[0].strip()
        elif kind == 'asset':
            e['asset'] = tf.extractfile(m).read()
        elif kind == 'asset.meta':
            e['meta'] = tf.extractfile(m).read()

queue = [g for g, e in entries.items()
         if 'asset' in e and not skipped(e.get('pathname', '')) and any(r.search(e.get('pathname', '')) for r in patterns)]
matched = len(queue)

wanted = set()
while queue:
    g = queue.pop()
    if g in wanted:
        continue
    e = entries.get(g)
    if not e or 'asset' not in e or skipped(e.get('pathname', '')):
        continue
    wanted.add(g)
    if not follow_deps:
        continue
    blobs = []
    if e['pathname'].lower().endswith(SCAN_EXT):
        blobs.append(e['asset'])
    if 'meta' in e:
        blobs.append(e['meta'])
    for b in blobs:
        for ref in GUID_RE.findall(b):
            r = ref.decode()
            if r in entries and r not in wanted:
                queue.append(r)

size = sum(len(entries[g]['asset']) for g in wanted)
print(f"{os.path.basename(pkg)}: {matched} matched, {len(wanted)} with dependencies, {size / 1048576:.1f} MB")

if dry_run:
    for g in sorted(wanted, key=lambda g: entries[g]['pathname']):
        print(f"  {entries[g]['pathname']}")
    sys.exit(0)

written = existed = 0
for g in wanted:
    e = entries[g]
    dst = os.path.join(root, e['pathname'])
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if os.path.exists(dst):
        existed += 1
        continue
    with open(dst, 'wb') as f:
        f.write(e['asset'])
    if 'meta' in e:
        with open(dst + '.meta', 'wb') as f:
            f.write(e['meta'])
    written += 1

# Folder metas for created directories (stable folder GUIDs, no Unity warnings).
for g, e in entries.items():
    if 'asset' not in e and 'meta' in e and 'pathname' in e:
        d = os.path.join(root, e['pathname'])
        if os.path.isdir(d) and not os.path.exists(d + '.meta'):
            with open(d + '.meta', 'wb') as f:
                f.write(e['meta'])

print(f"  {written} written, {existed} already existed")
