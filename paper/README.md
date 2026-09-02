# Overleaf 用論文（3章構成）

## 構成

| 章 | ファイル | 内容 |
|----|----------|------|
| 1 | `sections/01_story.tex` | **研究ストーリー**（RQ，仮説，概念的妥当性） |
| 2 | `sections/02_proposal.tex` | **Proposal**（危険度連動eHMI，数理モデル） |
| 3 | `sections/03_experiment.tex` | **実験設定**（手続き，従属変数，実験妥当性） |

妥当性は第1章（なぜこの研究か）と第3章（なぜこの実験設定か）に組み込まれています。

## Overleaf

1. `paper` フォルダを ZIP でアップロード
2. Compiler: **LuaLaTeX**
3. `main.tex` を Recompile

## 旧ファイル

`sections/04_experiment.tex` など旧版は参照用に残っている場合がありますが，
`main.tex` からは読み込まれません．
