"""
Contact sheets for a VFX texture pack: every texture as a labelled thumbnail on a dark background, so you can
see what each one is (flipbook? radial burst? trail strip? noise?) before mapping textures to effects.

usage: python contact_sheets.py <texture_folder> <output_folder> [--per-sheet 12] [--cell 300] [--include-noise]

Writes sheet_00.png, sheet_01.png ... (4 columns x 3 rows by default). Alpha textures are drawn as their colour
masked by alpha on black; greyscale textures as-is. Noise / caustics textures are skipped unless --include-noise.
Requires Pillow (pip install pillow).
"""
import argparse
import os

from PIL import Image, ImageDraw

EXTS = ('.png', '.tga', '.jpg', '.jpeg', '.psd', '.tif', '.tiff', '.exr')


def thumbnail(path, cell):
    im = Image.open(path)
    if im.mode not in ('RGBA', 'RGB'):
        im = im.convert('RGBA') if 'A' in im.mode else im.convert('RGB')
    if im.mode == 'RGBA':
        bg = Image.new('RGB', im.size, (0, 0, 0))
        bg.paste(im.convert('RGB'), mask=im.split()[3])
        im = bg
    w, h = im.size
    s = cell / max(w, h)
    return im.resize((max(1, int(w * s)), max(1, int(h * s)))), (w, h)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('src')
    ap.add_argument('out')
    ap.add_argument('--per-sheet', type=int, default=12)
    ap.add_argument('--cell', type=int, default=300)
    ap.add_argument('--include-noise', action='store_true')
    a = ap.parse_args()

    os.makedirs(a.out, exist_ok=True)
    files = sorted(f for f in os.listdir(a.src) if f.lower().endswith(EXTS))
    if not a.include_noise:
        files = [f for f in files if 'noise' not in f.lower() and 'caustic' not in f.lower()]
    cols = 4
    rows = (a.per_sheet + cols - 1) // cols
    for si in range(0, len(files), a.per_sheet):
        chunk = files[si:si + a.per_sheet]
        sheet = Image.new('RGB', (a.cell * cols, (a.cell + 20) * rows), (40, 40, 40))
        d = ImageDraw.Draw(sheet)
        for i, f in enumerate(chunk):
            try:
                im, (w, h) = thumbnail(os.path.join(a.src, f), a.cell)
            except Exception as e:  # unreadable format: label it and move on
                print(f'skip {f}: {e}')
                continue
            x, y = (i % cols) * a.cell, (i // cols) * (a.cell + 20)
            sheet.paste(im, (x, y + 20))
            d.text((x + 2, y + 2), f'{f} {w}x{h}', fill=(255, 255, 0))
        name = os.path.join(a.out, f'sheet_{si // a.per_sheet:02d}.png')
        sheet.save(name)
        print(name)
    print(f'{len(files)} textures, {(len(files) + a.per_sheet - 1) // a.per_sheet} sheets')


if __name__ == '__main__':
    main()
