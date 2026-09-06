# スライド書き出し（危険度・研究課題）

## すぐ使えるファイル

| ファイル | 用途 |
|----------|------|
| **`research_question_slide.pptx`** | **研究課題・仮説・条件（1枚）** |
| **`research_question_slide.pdf`** | 同上（PDF） |
| `research_question_slide_16x9.png` | 同上（画像） |
| **`risk_formula_slide.pptx`** | 危険度 R の計算式（1枚） |
| **`risk_formula_slide.pdf`** | 同上（PDF） |
| `risk_formula_slide_16x9.png` | 同上（画像） |
| `risk_appearance.pptx` | 見た目サンプル（全7枚） |
| `risk_appearance.pdf` | 見た目サンプル（PDF） |

## 研究課題スライドの内容

背景と問題 → RQ → ストーリーライン → 3条件 → 仮説 H1–H3 → 妥当性

## Google スライドへ

- PPTX を Drive に上げて「Google スライドで開く」
- または PNG / PDF を挿入

## 再生成

```bash
python paper/google-slides/export_research_question_slide.py
python paper/google-slides/export_risk_formula_slide.py
python paper/google-slides/export_risk_swatches.py
python paper/google-slides/export_pptx_pdf.py
```
