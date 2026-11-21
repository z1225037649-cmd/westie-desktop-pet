from pathlib import Path
import argparse
import json
import sys
from PIL import Image, ImageOps, ImageDraw

root = Path(__file__).resolve().parent


def study_tile(path, size, color=(226, 230, 224)):
    """Fit a complete source frame; never crop ears, feet or the tail."""
    with Image.open(path) as source:
        frame = source.convert('RGBA')
    frame.thumbnail(size, Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', size, color + (255,))
    canvas.alpha_composite(frame, ((size[0] - frame.width) // 2,
                                  (size[1] - frame.height) // 2))
    return canvas.convert('RGB')


def study_sheet(paths, labels, output, columns=3, size=(384, 336)):
    rows = (len(paths) + columns - 1) // columns
    tile_height = size[1] + 30
    sheet = Image.new('RGB', (columns * size[0], rows * tile_height), (226, 230, 224))
    draw = ImageDraw.Draw(sheet)
    for index, (path, label) in enumerate(zip(paths, labels)):
        x, y = index % columns * size[0], index // columns * tile_height
        draw.text((x + 12, y + 9), label, fill=(45, 58, 49))
        sheet.paste(study_tile(path, size), (x, y + 30))
    sheet.save(output)


def compose_visual_study(directory):
    manifest_path = directory / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    if manifest.get('schema') != 1:
        raise RuntimeError(f'Unsupported visual study manifest: {manifest_path}')
    paths, labels = [], []
    for style in ['natural', 'animated']:
        for surface in ['base', 'coat']:
            for angle in ['front', 'side', 'threequarter']:
                paths.append(directory / f'{style}-{surface}-{angle}.png')
                labels.append(f'{style.title()} eyes / {surface} / {angle}')
    study_sheet(paths, labels, directory / 'style-comparison.png')
    study_sheet([directory / 'face-natural.png', directory / 'face-animated.png'],
                ['Natural eyes / shared body', 'Animated eyes / shared body'], directory / 'face-comparison.png',
                columns=2, size=(480, 480))
    before = root / 'visual-before' / 'full-idle.png'
    if before.exists():
        study_sheet([before, directory / 'animated-coat-threequarter.png'],
                    ['Before / original model', 'After / animated eyes'],
                    directory / 'before-after.png', columns=2, size=(512, 448))
    if manifest.get('stillsOnly'):
        print(f'Saved visual study comparison PNGs: {directory}')
        return
    for action, clip in manifest['clips'].items():
        expected = clip['frameCount']
        sources = [directory / f'{action}-{number:03}.png' for number in range(expected)]
        missing = [str(path) for path in sources if not path.is_file()]
        if missing:
            raise RuntimeError(f'Missing {action} frames: {missing[:3]}')
        # Keep 30 Hz transparent PNG source frames. GIFs use every second frame.
        # Accumulated GIF centiseconds preserve the source duration without drift.
        selected = list(range(0, expected, 2))
        frames = [study_tile(sources[number], (512, 448)) for number in selected]
        sample = Image.new('RGB', (160 * min(8, len(frames)), 140))
        for i in range(min(8, len(frames))):
            number = round(i * (len(frames) - 1) / max(1, min(8, len(frames)) - 1))
            sample.paste(frames[number].resize((160, 140), Image.Resampling.LANCZOS), (i * 160, 0))
        palette = sample.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
        indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
        total_ms = clip['durationSeconds'] * 1000
        boundaries = [round(total_ms * i / len(frames) / 10) * 10
                      for i in range(len(frames) + 1)]
        durations = [boundaries[i + 1] - boundaries[i] for i in range(len(frames))]
        indexed[0].save(directory / f'{action}-preview.gif', save_all=True,
                        append_images=indexed[1:], duration=durations, loop=0,
                        disposal=2, optimize=False)
        if action == 'play':
            numbers = [0, 18, 22, 30, 45, 65, 100, expected - 1]
        elif action == 'idle':
            # The clip follows two seconds of warm-up; the first scheduled
            # blink at absolute 3.4 s therefore appears near clip time 1.4 s.
            numbers = [0, 40, 43, 45, 48, expected - 1]
        else:
            numbers = [round(i * (expected - 1) / 5) for i in range(6)]
        study_sheet([sources[number] for number in numbers],
                    [f'{action} / {number * clip["durationSeconds"] / expected:.2f} s'
                     for number in numbers], directory / f'{action}-contact.png',
                    columns=4 if action == 'play' else 3, size=(320, 280))
        print(f'Saved {action}: {expected} transparent frames; '
              f'{len(frames)} GIF frames; {sum(durations)} ms.')
    print(f'Saved visual study previews: {directory}')


parser = argparse.ArgumentParser(description='Compose rendered Westie interaction frames into GIFs.')
parser.add_argument('--frames-dir', type=Path, default=root)
parser.add_argument('--visual-study', type=Path, nargs='?', const=root / 'visual-study',
                    help='Compose offscreen VisualStudy output (default: qa/visual-study).')
options = parser.parse_args()
if options.visual_study is not None:
    compose_visual_study(options.visual_study.resolve())
    sys.exit(0)
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
for action, expected in [('pet', 23), ('feed', 28)]:
    action_frames = []
    for path in sorted(options.frames_dir.glob(f'{action}-motion-*.png')):
        frame = Image.open(path).convert('RGBA')
        canvas = Image.new('RGBA', frame.size, background)
        canvas.alpha_composite(frame)
        action_frames.append(canvas.convert('RGB').crop((55, 130, 600, 620)).resize((436, 392)))
    if len(action_frames) != expected:
        raise RuntimeError(f'Expected {expected} {action} frames, got {len(action_frames)}')
    action_frames[0].save(root / f'{action}-preview.gif', save_all=True,
                         append_images=action_frames[1:], duration=167, loop=0)
    contact = Image.new('RGB', (1308, 784), background[:3])
    for index, number in enumerate([0, 4, 8, 12, 17, expected - 1]):
        contact.paste(action_frames[number], ((index % 3) * 436, (index // 3) * 392))
    contact.save(root / f'{action}-contact.png')
sleep_source = root / 'details' / 'full-sleep.png'
if sleep_source.exists():
    sleep = Image.open(sleep_source).convert('RGB')
    sleep.crop((5, 215, 565, 500)).save(root / 'sleep-preview.png')
print('Saved rendered previews.')
