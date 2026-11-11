"""
Find likely flipbook grids (columns x rows) for VFX textures by looking for empty gutters between frames.

usage:
  python flipbook_grids.py <texture_folder> [--max 8] [--only "Smoke_*,Fire_*"]      # propose grids for every texture
  python flipbook_grids.py <texture_folder> --check "Tx_Smoke_radial_02.png=4x4" ...  # verify specific guesses
  python flipbook_grids.py <texture_folder> --overlay <out_folder>                    # also save images with the grid drawn

For each candidate grid it measures the brightest pixel (colour x alpha) along every internal frame boundary.
A clean flipbook has ~0 there. Candidates are listed best-first; several can pass (a 4x4 sheet also passes 2x2),
so prefer the FINEST grid whose frames still look like whole sprites, and always confirm with the overlay or a
contact sheet. Textures where nothing passes are single images, strips, or not clean grids (don't use them as
flipbooks). Requires Pillow and numpy.
"""
import argparse
import fnmatch
import os

import numpy as np
from PIL import Image, ImageDraw

EXTS = ('.png', '.tga', '.jpg', '.jpeg', '.tif', '.tiff')
GUTTER_OK = 40  # max brightness (0-255) allowed along a frame boundary


def luminance(path):
    a = np.asarray(Image.open(path).convert('RGBA'), dtype=np.float32)
    return a[..., :3].max(axis=2) * (a[..., 3] / 255.0)


def gutter(lum, cols, rows):
    h, w = lum.shape
    fw, fh = w / cols, h / rows
    vals = [0.0]
    for i in range(1, cols):
        x = int(round(i * fw))
        vals.append(lum[:, max(0, x - 2):x + 2].max())
    for j in range(1, rows):
        y = int(round(j * fh))
        vals.append(lum[max(0, y - 2):y + 2, :].max())
    return max(vals)


def frames_used(lum, cols, rows):
    """How many frames actually contain something (a 4x4 sheet with 13 frames has 3 empty cells)."""
    h, w = lum.shape
    n = 0
    for r in range(rows):
        for c in range(cols):
            cell = lum[int(r * h / rows):int((r + 1) * h / rows), int(c * w / cols):int((c + 1) * w / cols)]
            n += cell.max() > GUTTER_OK
    return n


def draw_overlay(path, cols, rows, out):
    im = Image.open(path).convert('RGBA')
    bg = Image.new('RGBA', im.size, (0, 0, 0, 255))
    bg.alpha_composite(im)
    d = ImageDraw.Draw(bg)
    w, h = im.size
    for i in range(1, cols):
        d.line([(i * w / cols, 0), (i * w / cols, h)], fill=(255, 0, 255, 255), width=max(2, w // 400))
    for j in range(1, rows):
        d.line([(0, j * h / rows), (w, j * h / rows)], fill=(255, 0, 255, 255), width=max(2, h // 400))
    s = 800 / max(w, h)
    bg.convert('RGB').resize((int(w * s), int(h * s))).save(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('src')
    ap.add_argument('--max', type=int, default=8, help='largest columns/rows to try')
    ap.add_argument('--only', default='', help='comma-separated filename globs')
    ap.add_argument('--check', nargs='*', default=[], help='file=COLSxROWS guesses to verify')
    ap.add_argument('--overlay', default='', help='folder to write grid overlays for the best candidate / checks')
    a = ap.parse_args()
    if a.overlay:
        os.makedirs(a.overlay, exist_ok=True)

    if a.check:
        for c in a.check:
            name, grid = c.split('=')
            cols, rows = (int(v) for v in grid.lower().split('x'))
            lum = luminance(os.path.join(a.src, name))
            g = gutter(lum, cols, rows)
            print(f"{'OK ' if g < GUTTER_OK else 'BAD'} {name:40s} {cols}x{rows} gutterMax={g:.0f} frames={frames_used(lum, cols, rows)}")
            if a.overlay:
                draw_overlay(os.path.join(a.src, name), cols, rows, os.path.join(a.overlay, f'{os.path.splitext(name)[0]}_{cols}x{rows}.png'))
        return

    globs = [g.strip() for g in a.only.split(',') if g.strip()]
    for f in sorted(os.listdir(a.src)):
        if not f.lower().endswith(EXTS) or (globs and not any(fnmatch.fnmatch(f, g) for g in globs)):
            continue
        lum = luminance(os.path.join(a.src, f))
        h, w = lum.shape
        cands = []
        for cols in range(1, a.max + 1):
            for rows in range(1, a.max + 1):
                if cols * rows < 2:
                    continue
                fw, fh = w / cols, h / rows
                if fw < 32 or fh < 32:
                    continue
                g = gutter(lum, cols, rows)
                if g < GUTTER_OK:
                    used = frames_used(lum, cols, rows) / (cols * rows)
                    # Rank: grids whose frames are (nearly) all used first, then the finest. Sparse sprites with
                    # empty borders also pass finer grids, but those leave many empty cells.
                    cands.append((used >= 0.75, cols * rows, cols, rows, g))
        if not cands:
            print(f'--  {f:40s} {w}x{h}  single image / strip / not a clean grid')
            continue
        cands.sort(reverse=True)
        best = ', '.join(f'{c}x{r}({frames_used(lum, c, r)}f)' for _, _, c, r, _ in cands[:4])
        print(f'OK  {f:40s} {w}x{h}  candidates: {best}')
        if a.overlay:
            _, _, c, r, _ = cands[0]
            draw_overlay(os.path.join(a.src, f), c, r, os.path.join(a.overlay, f'{os.path.splitext(f)[0]}_{c}x{r}.png'))


if __name__ == '__main__':
    main()
