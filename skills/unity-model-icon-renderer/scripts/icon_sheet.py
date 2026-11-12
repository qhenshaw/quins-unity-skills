"""
Contact sheet of rendered icons: each icon composited on a dark slot-coloured background with its name underneath,
so one image shows which icons are mis-posed, too small, edge-on or the wrong model.

usage: python icon_sheet.py <icon_folder> <out.png> [--match REGEX ...] [--cols 7] [--cell 200] [--strip-prefix]

  --match         keep only files whose name matches any of these regexes (case-insensitive), e.g. --match ^Armor_ Shield
  --strip-prefix  label "Armor_IronHelm.png" as "IronHelm"
Icons keep their aspect ratio inside the cell, so a 3x1 icon shows as a wide strip - just like in the game's grid.
Requires Pillow (pip install pillow).
"""
import argparse
import os
import re

from PIL import Image, ImageDraw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('folder')
    ap.add_argument('out')
    ap.add_argument('--match', nargs='*', default=[])
    ap.add_argument('--cols', type=int, default=7)
    ap.add_argument('--cell', type=int, default=200)
    ap.add_argument('--strip-prefix', action='store_true')
    a = ap.parse_args()

    pats = [re.compile(p, re.I) for p in a.match]
    names = sorted(f for f in os.listdir(a.folder)
                   if f.lower().endswith('.png') and (not pats or any(p.search(f) for p in pats)))
    if not names:
        raise SystemExit('no icons matched')

    cell, cols = a.cell, a.cols
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * cell, rows * (cell + 20)), (40, 44, 56))
    draw = ImageDraw.Draw(sheet)
    for i, n in enumerate(names):
        im = Image.open(os.path.join(a.folder, n)).convert('RGBA')
        im.thumbnail((cell - 10, cell - 10))
        x, y = (i % cols) * cell, (i // cols) * (cell + 20)
        sheet.paste(im, (x + (cell - im.width) // 2, y + (cell - im.height) // 2), im)
        label = n[:-4]
        if a.strip_prefix and '_' in label:
            label = label.split('_', 1)[1]
        draw.text((x + 4, y + cell + 2), label, fill=(230, 230, 230))
    sheet.save(a.out)
    print(f'{a.out}: {len(names)} icons')


if __name__ == '__main__':
    main()
