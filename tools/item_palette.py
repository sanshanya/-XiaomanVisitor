"""Render reviewed color substitutions without changing pixel geometry or alpha."""
from PIL import ImageColor


def apply_palette(image, palette):
    image = image.convert('RGBA')
    rgba = image.tobytes()
    pixels = [tuple(rgba[i:i + 4]) for i in range(0, len(rgba), 4)]
    colors = {pixel[:3] for pixel in pixels if pixel[3]}
    replacements = {ImageColor.getrgb(before): ImageColor.getrgb(after) for before, after in palette.items()}
    if set(replacements) - colors:
        raise ValueError('Palette refers to colors absent from its source')
    result = image.copy()
    result.putdata([(*replacements.get(pixel[:3], pixel[:3]), pixel[3]) if pixel[3] else pixel for pixel in pixels])
    assert result.size == image.size and result.getchannel('A').tobytes() == image.getchannel('A').tobytes()
    return result
