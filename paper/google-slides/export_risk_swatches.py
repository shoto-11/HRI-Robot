from PIL import Image, ImageDraw, ImageFont
import colorsys
import os

out_dir = r"C:\lab\HRI-Robot\paper\google-slides"
os.makedirs(out_dir, exist_ok=True)

samples = [0.0, 0.25, 0.5, 0.75, 1.0]
looks = ["青緑・うすい", "緑・ややうすい", "黄緑・中くらい", "橙黄・やや濃い", "赤・はっきり"]


def map_r(r):
    h = (1 - r) * 180 / 360
    s = 0.4 + 0.6 * r
    v = 0.5 + 0.3 * r
    a = 0.35 + 0.65 * r
    rgb = colorsys.hsv_to_rgb(h, s, v)
    return h * 360, s, v, a, tuple(int(c * 255) for c in rgb)


def font(size):
    for p in [
        r"C:\Windows\Fonts\meiryo.ttc",
        r"C:\Windows\Fonts\YuGothM.ttc",
        r"C:\Windows\Fonts\msgothic.ttc",
        r"C:\Windows\Fonts\arial.ttf",
    ]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size, index=0)
            except Exception:
                continue
    return ImageFont.load_default()


def paint_swatch_with_alpha(img, box, rgb, a, tile=14):
    x0, y0, x1, y1 = box
    R, G, B = rgb
    for y in range(y0, y1):
        for x in range(x0, x1):
            bg = 220 if ((x // tile) + (y // tile)) % 2 == 0 else 255
            px = int(R * a + bg * (1 - a))
            py = int(G * a + bg * (1 - a))
            pz = int(B * a + bg * (1 - a))
            img.putpixel((x, y), (px, py, pz))


W, H = 1920, 1080
img = Image.new("RGB", (W, H), (255, 255, 255))
draw = ImageDraw.Draw(img)
f_title = font(44)
f_sub = font(22)
f_lab = font(26)
f_num = font(20)
f_small = font(18)

draw.text((80, 48), "危険度スコア R ごとの経路表示（色・不透明度）", fill=(20, 20, 20), font=f_title)
draw.text(
    (80, 110),
    "R = 0.85·winner(Rp,Rc)+0.15·Rd　→　RiskToVisualMapper",
    fill=(90, 90, 90),
    font=f_sub,
)
draw.text(
    (80, 145),
    "α = 0.35 + 0.65R    H = (1−R)×180°    S = 0.4+0.6R    V = 0.5+0.3R",
    fill=(90, 90, 90),
    font=f_sub,
)
draw.text(
    (80, 180),
    "上段：色のみ（不透明）　／　下段：不透明度込み（市松模様の上）　／　表示は d≤13.6 m のみ",
    fill=(90, 90, 90),
    font=f_sub,
)

margin = 80
n = 5
gap = 28
usable = W - 2 * margin
cell_w = (usable - gap * (n - 1)) // n
y_top = 240
sw_h = 110

for i, r in enumerate(samples):
    Hdeg, S, V, a, rgb = map_r(r)
    x = margin + i * (cell_w + gap)
    draw.rectangle([x, y_top, x + cell_w, y_top + sw_h], fill=rgb, outline=(30, 30, 30), width=2)
    box = (x, y_top + sw_h + 16, x + cell_w, y_top + 2 * sw_h + 16)
    paint_swatch_with_alpha(img, box, rgb, a)
    draw.rectangle(list(box), outline=(30, 30, 30), width=2)

    ty = box[3] + 24
    lines = [
        f"R = {r:g}",
        looks[i],
        f"H = {Hdeg:.0f}°",
        f"α = {a:.3f}",
        f"S = {S:.2f}  V = {V:.2f}",
        f"#{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X}",
    ]
    for j, t in enumerate(lines):
        col = (10, 10, 10) if j == 0 else (50, 50, 50)
        f = f_lab if j <= 1 else f_num
        draw.text((x + 8, ty + j * 32), t, fill=col, font=f)

draw.text(
    (80, H - 60),
    "HRI-Robot  /  RiskToVisualMapper  /  Google Slides用",
    fill=(140, 140, 140),
    font=f_small,
)

slide_path = os.path.join(out_dir, "risk_appearance_slide_16x9.png")
img.save(slide_path, "PNG")
print("wrote", slide_path)

for i, r in enumerate(samples):
    Hdeg, S, V, a, rgb = map_r(r)
    tw, th = 480, 640
    tile = Image.new("RGB", (tw, th), (255, 255, 255))
    d = ImageDraw.Draw(tile)
    d.rectangle([40, 40, tw - 40, 200], fill=rgb, outline=(30, 30, 30), width=3)
    box = (40, 220, tw - 40, 380)
    paint_swatch_with_alpha(tile, box, rgb, a, tile=16)
    d.rectangle(list(box), outline=(30, 30, 30), width=3)
    lines = [
        f"R = {r:g}",
        looks[i],
        f"色相 H = {Hdeg:.0f}°",
        f"不透明度 α = {a:.3f}",
        f"S = {S:.2f}　V = {V:.2f}",
        f"#{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X}  RGB({rgb[0]}, {rgb[1]}, {rgb[2]})",
    ]
    yy = 410
    for j, t in enumerate(lines):
        d.text(
            (40, yy + j * 36),
            t,
            fill=(20, 20, 20) if j == 0 else (50, 50, 50),
            font=f_lab if j <= 1 else f_num,
        )
    name = f"risk_R_{str(r).replace('.', '_')}.png"
    p = os.path.join(out_dir, name)
    tile.save(p, "PNG")
    print("wrote", p)

svg_parts = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080" viewBox="0 0 1920 1080">',
    '<rect width="1920" height="1080" fill="#ffffff"/>',
    '<text x="80" y="80" font-family="Meiryo, Yu Gothic, sans-serif" font-size="44" fill="#141414">危険度スコア R ごとの経路表示（色・不透明度）</text>',
    '<text x="80" y="125" font-family="Meiryo, Yu Gothic, sans-serif" font-size="22" fill="#5a5a5a">R = 0.85·winner(Rp,Rc)+0.15·Rd　→　RiskToVisualMapper</text>',
    '<text x="80" y="160" font-family="Meiryo, Yu Gothic, sans-serif" font-size="22" fill="#5a5a5a">α = 0.35 + 0.65R　　H = (1−R)×180°　　S = 0.4+0.6R　　V = 0.5+0.3R</text>',
    '<defs><pattern id="chk" width="14" height="14" patternUnits="userSpaceOnUse">',
    '<rect width="14" height="14" fill="#ffffff"/><rect width="7" height="7" fill="#dcdcdc"/><rect x="7" y="7" width="7" height="7" fill="#dcdcdc"/>',
    "</pattern></defs>",
]
for i, r in enumerate(samples):
    Hdeg, S, V, a, rgb = map_r(r)
    x = 80 + i * (cell_w + gap)
    hexcol = f"#{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X}"
    y1 = 220
    svg_parts.append(
        f'<rect x="{x}" y="{y1}" width="{cell_w}" height="{sw_h}" fill="{hexcol}" stroke="#1e1e1e" stroke-width="2"/>'
    )
    y2 = y1 + sw_h + 16
    svg_parts.append(
        f'<rect x="{x}" y="{y2}" width="{cell_w}" height="{sw_h}" fill="url(#chk)" stroke="#1e1e1e" stroke-width="2"/>'
    )
    svg_parts.append(
        f'<rect x="{x}" y="{y2}" width="{cell_w}" height="{sw_h}" fill="{hexcol}" fill-opacity="{a:.4f}" stroke="#1e1e1e" stroke-width="2"/>'
    )
    ty = y2 + sw_h + 40
    svg_parts.append(
        f'<text x="{x + 8}" y="{ty}" font-family="Meiryo, Yu Gothic, sans-serif" font-size="26" fill="#0a0a0a">R = {r:g}</text>'
    )
    svg_parts.append(
        f'<text x="{x + 8}" y="{ty + 36}" font-family="Meiryo, Yu Gothic, sans-serif" font-size="22" fill="#323232">{looks[i]}</text>'
    )
    svg_parts.append(
        f'<text x="{x + 8}" y="{ty + 72}" font-family="Meiryo, Yu Gothic, sans-serif" font-size="20" fill="#323232">H = {Hdeg:.0f}°</text>'
    )
    svg_parts.append(
        f'<text x="{x + 8}" y="{ty + 104}" font-family="Meiryo, Yu Gothic, sans-serif" font-size="20" fill="#323232">α = {a:.3f}</text>'
    )
    svg_parts.append(
        f'<text x="{x + 8}" y="{ty + 136}" font-family="Meiryo, Yu Gothic, sans-serif" font-size="20" fill="#323232">{hexcol}</text>'
    )
svg_parts.append("</svg>")
svg_path = os.path.join(out_dir, "risk_appearance_slide_16x9.svg")
with open(svg_path, "w", encoding="utf-8") as f:
    f.write("\n".join(svg_parts))
print("wrote", svg_path)
