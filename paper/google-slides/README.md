# 危険度 R の見た目 — スライド書き出し

## すぐ使えるファイル

| ファイル | 用途 |
|----------|------|
| **`risk_formula_slide.pptx`** | **計算式1枚まとめ**（おすすめ） |
| **`risk_formula_slide.pdf`** | 計算式1枚（PDF） |
| `risk_formula_slide_16x9.png` | 計算式1枚（画像） |
| `risk_appearance.pptx` | 見た目サンプル（全7枚） |
| `risk_appearance.pdf` | 見た目サンプル（PDF） |
| `risk_appearance_slide_16x9.png` | 見た目一覧1枚 |
| `risk_R_*.png` | Rごと個別画像 |

## 計算式スライドの内容

表示可否 → TTC → 近接 → 統合 R → 色・不透明度（連続）→ パラメータ

## Google スライドへ

- PPTX を Drive に上げて「Google スライドで開く」
- または PNG / PDF を挿入

## 再生成

```bash
python paper/google-slides/export_risk_swatches.py
python paper/google-slides/export_pptx_pdf.py
python paper/google-slides/export_risk_formula_slide.py
```
