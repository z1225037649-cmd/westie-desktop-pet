from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
tiles = [('idle-52-512.png', '待机'), ('feed-52-512.png', '喂食'),
         ('play-52-512.png', '玩球'), ('sleep-52-512.png', '趴睡'),
         ('sleep-view-side-512.png', '趴睡侧面'), ('walk-52-512.png', '步行')]
board = Image.new('RGB', (960, 650), '#f4f4ef')
draw = ImageDraw.Draw(board)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
for i, (name, label) in enumerate(tiles):
    x, y = (i % 3) * 320, (i // 3) * 325
    image = Image.open(root / 'qa/generated' / name).convert('RGBA')
    image.thumbnail((310, 275), Image.Resampling.LANCZOS)
    board.paste(image, (x + (320 - image.width) // 2, y + 5), image)
    draw.text((x + 160, y + 292), label, font=font, fill='#38434b', anchor='mm')
board.save(root / 'docs/images/westie-generated-review.jpg', quality=93)

sleep_preview = Image.new('RGB', (800, 285), '#f4f4ef')
preview_draw = ImageDraw.Draw(sleep_preview)
for i, (name, label) in enumerate([('sleep-view-threequarter-512.png', '趴睡'), ('sleep-view-side-512.png', '侧面')]):
    image = Image.open(root / 'qa/generated' / name).convert('RGBA')
    image = image.crop(image.getbbox())
    image.thumbnail((380, 225), Image.Resampling.LANCZOS)
    sleep_preview.paste(image, (i * 400 + (400 - image.width) // 2, 235 - image.height), image)
    preview_draw.text((i * 400 + 200, 263), label, font=font, fill='#38434b', anchor='mm')
sleep_preview.save(root / 'docs/images/westie-sleep-preview.png')
