# 危険度 R の見た目 — スライド書き出し

## すぐ使えるファイル

| ファイル | 用途 |
|----------|------|
| **`risk_appearance.pptx`** | PowerPoint（16:9、全7枚） |
| **`risk_appearance.pdf`** | PDF（同じ内容） |
| `risk_appearance_slide_16x9.png` | 1枚画像（Google スライドにも可） |
| `risk_R_*.png` | Rごと個別画像 |

## PowerPoint / PDF の中身

1. 一覧スライド（色＋不透明度の見た目）
2. 数値表＋カラーチップ
3. R=0 / 0.25 / 0.5 / 0.75 / 1 の個別スライド

## Google スライドへ

- **方法A**: `risk_appearance.pptx` を Drive にアップロード → 右クリックで Google スライドで開く  
- **方法B**: PDF をアップロードして挿入、または PNG を画像として挿入

## 再生成

```bash
python paper/google-slides/export_risk_swatches.py
python paper/google-slides/export_pptx_pdf.py
```
