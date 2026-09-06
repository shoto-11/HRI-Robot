"""One-slide summary of research question, hypotheses, and conditions."""
from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from pptx import Presentation
from pptx.util import Inches
from reportlab.lib.utils import ImageReader
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


def card(d, box, title, lines, title_bg, f_h, f_body, f_small, f_eq=None):
    x0, y0, x1, y1 = box
    d.rounded_rectangle(box, radius=14, fill=(255, 255, 255), outline=(210, 215, 225), width=2)
    d.rounded_rectangle([x0, y0, x1, y0 + 44], radius=14, fill=title_bg)
    d.rectangle([x0, y0 + 22, x1, y0 + 44], fill=title_bg)
    d.text((x0 + 18, y0 + 10), title, fill=(255, 255, 255), font=f_h)
    yy = y0 + 56
    for kind, text in lines:
        if kind == "eq":
            f, color, step = (f_eq or f_body), (20, 30, 50), 32
        elif kind == "small":
            f, color, step = f_small, (85, 90, 100), 26
        else:
            f, color, step = f_body, (30, 35, 45), 30
        d.text((x0 + 18, yy), text, fill=color, font=f)
        yy += step


def draw_slide() -> Path:
    img = Image.new("RGB", (W, H), (248, 249, 252))
    d = ImageDraw.Draw(img)
    f_title = jp_font(38)
    f_h = jp_font(22)
    f_body = jp_font(19)
    f_eq = jp_font(20)
    f_small = jp_font(15)
    f_rq = jp_font(23)

    d.rectangle([0, 0, W, 86], fill=(28, 55, 95))
    d.text((48, 22), "研究課題・仮説・実験条件（1枚まとめ）", fill=(255, 255, 255), font=f_title)

    # Background / problem
    card(
        d,
        (40, 106, 940, 280),
        "背景と問題",
        [
            ("body", "工場で AGV 10〜20 台が同時走行し、人と通路を共有"),
            ("body", "どの AGV が自分にとって危険か、瞬時に判断しにくい"),
            ("small", "経路を均一にAR表示するだけでは危険の優先順位が付けられず、注意が分散する"),
        ],
        (40, 100, 150),
        f_h,
        f_body,
        f_small,
    )

    # RQ highlight
    d.rounded_rectangle([980, 106, 1880, 280], radius=14, fill=(255, 248, 230), outline=(220, 170, 60), width=3)
    d.rounded_rectangle([980, 106, 1880, 150], radius=14, fill=(180, 120, 20))
    d.rectangle([980, 128, 1880, 150], fill=(180, 120, 20))
    d.text((998, 116), "研究課題（RQ）", fill=(255, 255, 255), font=f_h)
    d.text((1000, 168), "AGV残存経路の可視化において、", fill=(40, 35, 20), font=f_rq)
    d.text((1000, 202), "危険度に応じた視覚エンコーディングは、", fill=(40, 35, 20), font=f_rq)
    d.text((1000, 236), "作業員の移動効率と安全性にどのような影響を与えるか？", fill=(40, 35, 20), font=f_rq)

    # Storyline
    card(
        d,
        (40, 298, 940, 520),
        "ストーリーライン",
        [
            ("body", "1. 問題　多数AGVで衝突リスク予測が困難"),
            ("body", "2. 限界　均一な経路表示では優先順位付け不可"),
            ("body", "3. 提案　TTC＋近接 → R → 色・不透明度を連続変化"),
            ("body", "4. 検証　VR工場で Station A → B 移動"),
            ("small", "期待: Proposed は No-AR より安全、Baseline より情報過多を抑制"),
        ],
        (50, 90, 70),
        f_h,
        f_body,
        f_small,
    )

    # 3 conditions
    card(
        d,
        (980, 298, 1880, 520),
        "比較する3条件（被験者内）",
        [
            ("eq", "Baseline　経路あり・固定色　　危険度連動なし"),
            ("eq", "No-AR　　　経路なし　　　　　　　—"),
            ("eq", "Proposed　経路あり・色α連動　　危険度連動あり（本提案）"),
            ("small", "同一ケース番号では AGV 台数・速度・軌道をシード固定（条件差＝表示方式のみ）"),
        ],
        (70, 55, 130),
        f_h,
        f_body,
        f_small,
        f_eq,
    )

    # Hypotheses
    card(
        d,
        (40, 538, 1240, 860),
        "研究仮説",
        [
            ("eq", "H1（安全性）　Proposed ＜ No-AR　（ニアミス回数）"),
            ("small", "　　根拠: 危険AGVの経路が強調され、回避が促進される"),
            ("eq", "H2（効率）　　Proposed ＜ Baseline　（完了時間 / 移動距離）"),
            ("small", "　　根拠: 危険度連動で非危険AGVへの注意が減り、移動がスムーズ"),
            ("eq", "H3（情報）　　No-AR ＞ Proposed　（完了時間 / 移動距離）"),
            ("small", "　　根拠: 経路情報がないと予測できず、迂回・停止が増える"),
            ("body", "従属変数: 完了時間・ニアミス回数・移動距離（自動計測）"),
        ],
        (140, 55, 55),
        f_h,
        f_body,
        f_small,
        f_eq,
    )

    # Validity
    card(
        d,
        (1260, 538, 1880, 860),
        "なぜこの研究か（妥当性）",
        [
            ("body", "① 理論: TTC＋近接は交通/HRIの慣行"),
            ("body", "② 操作: 3条件で危険度連動を分離"),
            ("body", "③ 測定: 効率・安全を客観指標で検証"),
            ("small", "H1–H3 はいずれも従属変数と1対1で対応"),
            ("small", "結果は AR経路eHMI の相対比較として解釈"),
        ],
        (40, 85, 110),
        f_h,
        f_body,
        f_small,
    )

    d.rectangle([0, 1000, W, H], fill=(232, 236, 244))
    d.text(
        (48, 1025),
        "HRI-Robot  Proposed eHMI  ／  研究課題の1枚まとめ",
        fill=(70, 75, 90),
        font=f_small,
    )

    out = OUT / "research_question_slide_16x9.png"
    img.save(out, "PNG")
    return out


def export_pptx(png: Path) -> Path:
    prs = Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    s = prs.slides.add_slide(prs.slide_layouts[6])
    s.shapes.add_picture(str(png), Inches(0), Inches(0), width=prs.slide_width)
    out = OUT / "research_question_slide.pptx"
    prs.save(out)
    return out


def export_pdf(png: Path) -> Path:
    out = OUT / "research_question_slide.pdf"
    c = pdf_canvas.Canvas(str(out), pagesize=(W, H))
    c.drawImage(ImageReader(str(png)), 0, 0, width=W, height=H)
    c.showPage()
    c.save()
    return out


def main():
    png = draw_slide()
    pptx = export_pptx(png)
    pdf = export_pdf(png)
    print("png:", png)
    print("pptx:", pptx)
    print("pdf:", pdf)


if __name__ == "__main__":
    main()
