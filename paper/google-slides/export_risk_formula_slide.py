"""One-slide summary of risk score R and visual mapping formulas."""
from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.util import Inches, Pt, Emu
from reportlab.lib.colors import Color, HexColor, black, white
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas as pdf_canvas

OUT = Path(__file__).resolve().parent
W, H = 1920, 1080


def jp_font(size: int):
    for p in [
        r"C:\Windows\Fonts\meiryo.ttc",
        r"C:\Windows\Fonts\YuGothM.ttc",
        r"C:\Windows\Fonts\msgothic.ttc",
    ]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size, index=0)
            except Exception:
                continue
    return ImageFont.load_default()


def register_pdf_font() -> str:
    for name, path in [
        ("Meiryo", r"C:\Windows\Fonts\meiryo.ttc"),
        ("YuGothic", r"C:\Windows\Fonts\YuGothM.ttc"),
    ]:
        if os.path.exists(path):
            try:
                pdfmetrics.registerFont(TTFont(name, path, subfontIndex=0))
                return name
            except Exception:
                continue
    return "Helvetica"


def rounded_rect(draw, box, fill, outline=(200, 200, 200), radius=16, width=2):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_formula_slide_png() -> Path:
    img = Image.new("RGB", (W, H), (250, 250, 252))
    d = ImageDraw.Draw(img)
    f_title = jp_font(40)
    f_h = jp_font(24)
    f_body = jp_font(20)
    f_eq = jp_font(22)
    f_small = jp_font(16)
    f_tiny = jp_font(14)

    # Title bar
    d.rectangle([0, 0, W, 88], fill=(32, 56, 88))
    d.text((48, 22), "危険度スコア R の計算と視覚マッピング（Proposed）", fill=(255, 255, 255), font=f_title)

    # Column layout: left risk, right visual
    # Card 1: Visibility
    cards = []
    # left column x=40..940, right 980..1880
    # 4 cards left stacked, 2 cards right + params

    def card(xyxy, title, lines, title_bg=(45, 90, 140)):
        x0, y0, x1, y1 = xyxy
        rounded_rect(d, xyxy, (255, 255, 255), (210, 215, 225), 14)
        d.rounded_rectangle([x0, y0, x1, y0 + 44], radius=14, fill=title_bg)
        d.rectangle([x0, y0 + 22, x1, y0 + 44], fill=title_bg)
        d.text((x0 + 18, y0 + 10), title, fill=(255, 255, 255), font=f_h)
        yy = y0 + 58
        for kind, text in lines:
            f = f_eq if kind == "eq" else (f_small if kind == "small" else f_body)
            color = (25, 25, 30) if kind != "small" else (80, 85, 95)
            d.text((x0 + 18, yy), text, fill=color, font=f)
            yy += 30 if kind != "eq" else 34

    # Left: pipeline
    card(
        (40, 110, 940, 250),
        "① 表示可否（Visibility）",
        [
            ("eq", "visible = ( T ≤ Tmax )  ∨  ( d ≤ Dmax )"),
            ("body", "非表示なら R = 0　／　Tmax = 4.0 s　／　Dmax = 13.6 m"),
            ("small", "Dmax = (vAGV,max + vped) × Tmax = (2.0 + 1.4) × 4"),
        ],
        (40, 110, 160),
    )
    card(
        (40, 268, 940, 460),
        "② TTC（Time To Conflict）",
        [
            ("eq", "T = s / max(v,  vmin)     vmin = 0.1 m/s"),
            ("body", "s : AGV残存経路が視線方向の太線ゾーンと交わるまでの距離"),
            ("eq", "Rttc = 0          (T = ∞)"),
            ("eq", "Rttc = (1 − T/Tmax)^γ     (0 ≤ T ≤ Tmax)"),
            ("small", "γ = 0.6　／　視線基準線の半幅 w = 0.5 m（全幅 1.0 m）"),
        ],
        (30, 120, 100),
    )
    card(
        (40, 478, 940, 650),
        "③ 近接距離由来の危険度",
        [
            ("eq", "Rprox = 0                 (d ≥ Dmax)"),
            ("eq", "Rprox = (1 − d/Dmax)^γ    (d < Dmax)"),
            ("body", "d : 参加者と AGV の 3D 距離　／　γ = 0.6"),
        ],
        (120, 80, 40),
    )
    card(
        (40, 668, 940, 860),
        "④ 統合スコア R",
        [
            ("eq", "R = max( Rttc ,  Rprox ,  Rfloor )"),
            ("body", "R ∈ [0, 1]　／　大きいほど危険　／　Rfloor = 0.08（表示中の下限）"),
            ("small", "TTC と近接の大きい方を採用 → 交差しないが近い／交差するが遠い の両方をカバー"),
        ],
        (140, 50, 50),
    )

    # Right: visual mapping
    card(
        (980, 110, 1880, 430),
        "⑤ 色・不透明度（連続）",
        [
            ("eq", "H(R) = (1 − R) × 180°     青緑(安全) → 赤(危険)"),
            ("eq", "S(R) = 0.4 + 0.6 R"),
            ("eq", "V(R) = 0.5 + 0.3 R"),
            ("eq", "α(R) = 0.35 + 0.65 R      半透明 → 不透明"),
            ("body", "Proposed 条件のみ適用　／　色も不透明度も R に対して連続変化"),
            ("small", "Baseline: 固定青・α=1　／　No-AR: 経路非表示"),
        ],
        (70, 50, 130),
    )

    # Color bar strip
    import colorsys

    bar_x0, bar_y0, bar_x1, bar_y1 = 1000, 460, 1860, 540
    d.text((1000, 448), "R: 0 → 1 の見た目", fill=(60, 60, 70), font=f_small)
    for x in range(bar_x0, bar_x1):
        t = (x - bar_x0) / (bar_x1 - bar_x0)
        h = (1 - t) * 180 / 360
        s = 0.4 + 0.6 * t
        v = 0.5 + 0.3 * t
        a = 0.35 + 0.65 * t
        r, g, b = colorsys.hsv_to_rgb(h, s, v)
        # blend with white for alpha preview
        rr = int((r * a + 1 * (1 - a)) * 255)
        gg = int((g * a + 1 * (1 - a)) * 255)
        bb = int((b * a + 1 * (1 - a)) * 255)
        d.line([(x, bar_y0), (x, bar_y1)], fill=(rr, gg, bb))
    d.rectangle([bar_x0, bar_y0, bar_x1, bar_y1], outline=(80, 80, 90), width=2)
    d.text((bar_x0, bar_y1 + 8), "R=0  青緑・うすい", fill=(50, 50, 60), font=f_tiny)
    d.text((bar_x1 - 160, bar_y1 + 8), "R=1  赤・はっきり", fill=(50, 50, 60), font=f_tiny)

    # Params table card
    card(
        (980, 590, 1880, 860),
        "主要パラメータ",
        [
            ("body", "Tmax = 4.0 s　　Dmax = 13.6 m　　γ = 0.6　　Rfloor = 0.08"),
            ("body", "w = 0.5 m（基準線半幅）　　vAGV,max = 2.0 m/s　　vped = 1.4 m/s"),
            ("body", "実装: VehicleRiskCalculator → RiskToVisualMapper → PathRenderer"),
            ("small", "視線方向の太線ゾーン × AGV残存経路交差 → TTC　／　近接距離も併用"),
        ],
        (50, 70, 90),
    )

    # Footer
    d.rectangle([0, 1000, W, H], fill=(235, 238, 245))
    d.text(
        (48, 1025),
        "HRI-Robot  Proposed eHMI  ／  危険度計算の1枚まとめ",
        fill=(70, 75, 90),
        font=f_small,
    )

    out = OUT / "risk_formula_slide_16x9.png"
    img.save(out, "PNG")
    return out


def export_pptx(png: Path) -> Path:
    prs = Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    s = prs.slides.add_slide(prs.slide_layouts[6])
    s.shapes.add_picture(str(png), Inches(0), Inches(0), width=prs.slide_width)
    out = OUT / "risk_formula_slide.pptx"
    prs.save(out)
    return out


def export_pdf(png: Path) -> Path:
    out = OUT / "risk_formula_slide.pdf"
    c = pdf_canvas.Canvas(str(out), pagesize=(W, H))
    c.drawImage(ImageReader(str(png)), 0, 0, width=W, height=H)
    c.showPage()
    c.save()
    return out


def main():
    png = draw_formula_slide_png()
    pptx = export_pptx(png)
    pdf = export_pdf(png)
    print("png:", png)
    print("pptx:", pptx)
    print("pdf:", pdf)


if __name__ == "__main__":
    main()
