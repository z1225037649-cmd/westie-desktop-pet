from pathlib import Path
import argparse
from PIL import Image, ImageOps, ImageDraw

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description='Compose rendered Westie play frames into a GIF.')
parser.add_argument('--frames-dir', type=Path, default=root)
options = parser.parse_args()
background = (226, 230, 224, 255)
frames = []
for path in sorted(options.frames_dir.glob('play-motion-*.png')):
    frame = Image.open(path).convert('RGBA')
    canvas = Image.new('RGBA', frame.size, background)
    canvas.alpha_composite(frame)
    frames.append(canvas.convert('RGB').crop((55, 130, 600, 620)).resize((436, 392)))
if len(frames) != 24:
    raise RuntimeError(f'Expected 24 production frames, got {len(frames)}')
frames[0].save(root / 'play-preview.gif', save_all=True, append_images=frames[1:], duration=167, loop=0)
sheet = Image.new('RGB', (872, 784), background[:3])
for index, number in enumerate([0, 6, 12, 18]):
    sheet.paste(frames[number], ((index % 2) * 436, (index // 2) * 392))
sheet.save(root / 'play-contact.png')
sleep_source = root / 'details' / 'full-sleep.png'
if sleep_source.exists():
    sleep = Image.open(sleep_source).convert('RGB')
    sleep.crop((5, 215, 565, 500)).save(root / 'sleep-preview.png')
print('Saved rendered previews.')
