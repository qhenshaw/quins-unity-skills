"""
Index .unitypackage files without extracting them.

usage: python index_unitypackage.py <out_dir> <package.unitypackage> [<package> ...]

Writes <out_dir>/<package name>.txt with one line per entry: <guid>\t<pathname>\t<size KB>, sorted by path.
Grep these indexes to choose extraction patterns (prefab names, folders, texture sets).
"""
import os
import sys
import tarfile

if len(sys.argv) < 3:
    print(__doc__)
    sys.exit(1)

out_dir = sys.argv[1]
os.makedirs(out_dir, exist_ok=True)
for pkg in sys.argv[2:]:
    name = os.path.splitext(os.path.basename(pkg))[0]
    paths, sizes = {}, {}
    with tarfile.open(pkg, 'r:gz') as tf:
        for m in tf:
            parts = m.name.lstrip('./').split('/')
            if len(parts) != 2:
                continue
            guid, kind = parts
            if kind == 'pathname':
                paths[guid] = tf.extractfile(m).read().decode('utf8', 'replace').splitlines()[0].strip()
            elif kind == 'asset':
                sizes[guid] = m.size
    with open(os.path.join(out_dir, name + '.txt'), 'w', encoding='utf8') as f:
        for g, p in sorted(paths.items(), key=lambda kv: kv[1]):
            f.write(f"{g}\t{p}\t{sizes.get(g, 0) // 1024}\n")
    total = sum(sizes.values()) / 1048576
    print(f"{name}: {len(paths)} entries, {total:.0f} MB")
