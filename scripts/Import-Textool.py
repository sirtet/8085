"""Place the user's hardware cutouts over the original continuous front panel."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

resources = Path(__file__).resolve().parents[1] / 'Src' / 'Resources'
source = Image.open(resources / 'Textool.tiff').convert('RGB')
assert source.size == (440, 560)
base = Image.open(resources / 'pkw-photo-panel.png').convert('RGB')
base_pixels = np.asarray(base).astype(float)
clean = base_pixels.copy()
# Remove the obsolete socket, interpolating the panel between unobstructed sides.
# Blend at the outer border; no rectangular TIFF background is carried across.
for y in range(478, 990):
    left_colour = np.median(base_pixels[y,680:699],axis=0)
    right_colour = np.median(base_pixels[y,875:901],axis=0)
    texture = base_pixels[y,850:888]
    texture = np.concatenate((texture,texture[::-1]))
    texture = texture - texture.mean(axis=0)
    for x in range(688, 888):
        t = (x - 688) / 199
        background = left_colour * (1-t) + right_colour * t + texture[(x-688)%76]
        weight = min(1, (x-688)/9, (887-x)/9, (y-478)/9, (989-y)/9)
        clean[y,x] = base_pixels[y,x]*(1-weight) + background*weight
clean = Image.fromarray(np.clip(clean,0,255).astype('uint8')).crop((688,478,888,990)).convert('RGBA')
offset_y = 5
for pins, left in [(24, 10), (28, 230)]:
    panel = source.crop((left, 38, left + 200, 550)).convert('RGBA')
    mask = Image.new('L', panel.size)
    draw = ImageDraw.Draw(mask)
    draw.polygon([(23,115),(136,115),(144,121),(144,362),(134,366),
                  (134,397),(128,403),(20,403),(15,398),(15,121)],fill=255)
    bottom = 153 if pins == 24 else 98
    draw.polygon([(27,15),(133,15),(139,20),(139,bottom-4),
                  (134,bottom),(25,bottom),(20,bottom-4),(20,22)],fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(.5))
    hardware = panel.copy()
    hardware.putalpha(mask)
    frame = clean.copy()
    shadow = Image.new('RGBA',panel.size,(0,0,0,0))
    shadow.putalpha(mask.filter(ImageFilter.GaussianBlur(2)).point(lambda v: int(v*.3)))
    frame.alpha_composite(shadow,(2,offset_y+2))
    frame.alpha_composite(hardware,(0,offset_y))
    closed = frame.copy()
    # Keep the complete bent shaft and handle together, at original resolution.
    # The pivot is under the SIDE STEP, not next to the lower mounting screw.
    lever_mask = Image.new('L', panel.size)
    lever_draw = ImageDraw.Draw(lever_mask)
    lever_draw.polygon([(134,364),(139,364),(144,375),(144,435),
                        (134,435),(134,377),(132,371)],fill=255)
    lever_draw.rounded_rectangle((122,429,155,480),radius=16,fill=255)
    lever_mask = lever_mask.filter(ImageFilter.GaussianBlur(.45))
    lever = panel.copy()
    lever.putalpha(lever_mask)
    closed.alpha_composite(lever,(0,offset_y))
    closed.save(resources / f'pkw-socket-photo-{pins}-closed.png')
    opened = frame.copy()
    # A foreshortened photographic grip, centered on the actual pivot (138,365).
    # This unphotographed upright view remains an approximation.
    handle_mask = Image.new('L',panel.size)
    ImageDraw.Draw(handle_mask).rounded_rectangle((122,429,155,480),radius=16,fill=255)
    handle = panel.copy()
    handle.putalpha(handle_mask.filter(ImageFilter.GaussianBlur(.45)))
    # Exclude the original white housing/background fringe when the handle moves.
    pixels = np.asarray(handle).copy()
    rgb = pixels[:,:,:3].astype(float)
    coverage = np.clip(np.minimum(rgb[:,:,1]-rgb[:,:,0]*1.15,
                                  rgb[:,:,2]-rgb[:,:,0]*1.05)/12,0,1)
    pixels[:,:,3] = (pixels[:,:,3]*coverage).astype('uint8')
    handle = Image.fromarray(pixels)
    handle = handle.crop((121,428,157,482)).resize((30,25),Image.Resampling.LANCZOS)
    opened.alpha_composite(handle,(123,353+offset_y))
    opened.save(resources / f'pkw-socket-photo-{pins}-open.png')
print('Imported revised Textool hardware; continuous panel background; vertical offset +5.')
