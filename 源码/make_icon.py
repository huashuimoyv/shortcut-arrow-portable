from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
S = 4
N = 256

def box(coords):
    return tuple(round(v * S) for v in coords)

canvas = Image.new('RGBA', (N*S, N*S), (0, 0, 0, 0))
mask = Image.new('L', canvas.size, 0)
ImageDraw.Draw(mask).rounded_rectangle(box((14, 14, 242, 242)), radius=54*S, fill=255)
color = Image.new('RGBA', canvas.size)
pix = color.load()
for y in range(N*S):
    for x in range(N*S):
        t = min(1, max(0, (0.35*x + 0.65*y) / (N*S)))
        pix[x,y] = (round(55*(1-t)+23*t), round(137*(1-t)+83*t), round(112*(1-t)+70*t), 255)
color.putalpha(mask)
canvas.alpha_composite(color)
d = ImageDraw.Draw(canvas)

# A single clean tile, with a small separate sparkle at its upper-right edge.
d.rounded_rectangle(box((65, 77, 181, 193)), radius=26*S, fill=(246, 255, 251, 255))
cutout = Image.new('L', canvas.size, 0)
cd = ImageDraw.Draw(cutout)
cd.rounded_rectangle(box((80, 92, 166, 178)), radius=13*S, fill=255)

# Deliberate negative space keeps the two white forms distinct at small sizes.
cd.rounded_rectangle(box((148, 54, 205, 111)), radius=13*S, fill=255)
canvas.paste(color, (0, 0), cutout)
d = ImageDraw.Draw(canvas)
points = [(177,54),(185,74),(205,82),(185,90),(177,110),(169,90),(149,82),(169,74)]
d.polygon([(x*S,y*S) for x,y in points], fill=(214, 251, 226, 255))

canvas = canvas.resize((N, N), Image.Resampling.LANCZOS)
canvas.save(HERE / 'app-icon.png')
canvas.save(HERE / 'app-icon.ico', sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])

# Preview transparency against the two common desktop tones, plus real small sizes.
preview = Image.new('RGB', (680, 300), '#f3f5f4')
pd = ImageDraw.Draw(preview)
pd.rectangle((340,0,679,299), fill='#202a27')
preview.paste(canvas, (42,20), canvas)
preview.paste(canvas, (382,20), canvas)
preview.save(HERE / 'icon-preview.png')
print(HERE / 'app-icon.ico')
