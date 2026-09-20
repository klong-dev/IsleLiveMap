from collections import deque
from pathlib import Path
from PIL import Image

for path in Path('src/TheIsleOverlay.App/Assets/MapLayers/Icons/png').glob('*.png'):
    image = Image.open(path).convert('RGBA')
    pixels = image.load()
    width, height = image.size
    queue = deque()
    seen = set()
    for x in range(width):
        queue.extend(((x, 0), (x, height - 1)))
    for y in range(height):
        queue.extend(((0, y), (width - 1, y)))
    while queue:
        x, y = queue.popleft()
        if (x, y) in seen or not (0 <= x < width and 0 <= y < height):
            continue
        seen.add((x, y))
        red, green, blue, alpha = pixels[x, y]
        if alpha == 0 or min(red, green, blue) < 238 or max(red, green, blue) - min(red, green, blue) > 10:
            continue
        pixels[x, y] = (red, green, blue, 0)
        queue.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    image.save(path)
