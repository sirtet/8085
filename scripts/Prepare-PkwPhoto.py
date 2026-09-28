"""Reuse the accepted pre-rounding photo panel, replacing only the socket area.

Pillow is required. The base and socket source are earlier image-edit outputs.
No perspective transform or change to the housing shape is applied here.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

resources = Path(__file__).resolve().parents[1] / 'Src' / 'Resources'
base = Image.open(resources / 'pkw-photo-base.png').convert('RGB')
socket = Image.open(resources / 'pkw-photo-socket-source.png').convert('RGB')
mask = Image.new('L', base.size)
draw = ImageDraw.Draw(mask)
draw.rectangle((702, 491, 845, 886), fill=255)
draw.rectangle((813, 872, 846, 958), fill=255)
# The already requested stain removal is local to the upper-right seam.
draw.rectangle((1491, 245, 1529, 315), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(2))
result = Image.composite(socket, base, mask)
result.save(resources / 'pkw-photo-panel.png')
original = Image.open(resources / 'pkw-photo-original.jpg')
original.crop((1239,922,1271,967)).save(resources / 'pkw-switch-vertical.png')
original.crop((1529,1095,1572,1127)).save(resources / 'pkw-switch-horizontal.png')
# Guard against accidentally using the later rounded/geometrically warped case.
assert result.crop((0,965,1536,1024)).tobytes() == base.crop((0,965,1536,1024)).tobytes()
assert result.crop((0,0,1480,480)).tobytes() == base.crop((0,0,1480,480)).tobytes()
print('Restored pre-rounding housing, inserted empty socket/hinge, removed stain.')
