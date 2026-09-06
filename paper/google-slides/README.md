# Google スライド用：危険度 R の見た目

## ファイル

| ファイル | 用途 |
|----------|------|
| `risk_appearance_slide_16x9.png` | **1枚スライド用**（1920×1080）いちばん簡単 |
| `risk_appearance_slide_16x9.svg` | 拡大してもきれいな版（Drive経由で挿入可） |
| `risk_R_0_0.png` … `risk_R_1_0.png` | Rごと1枚ずつ |

再生成する場合:

```bash
python paper/google-slides/export_risk_swatches.py
```

## Google スライドへの入れ方

1. [slides.google.com](https://slides.google.com) でスライドを開く
2. **挿入 → 画像 → パソコンからアップロード**
3. `risk_appearance_slide_16x9.png` を選ぶ
4. 必要ならスライドいっぱいにリサイズ（16:9想定）

個別に並べたい場合は `risk_R_*.png` を5枚挿入してください。
