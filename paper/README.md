# Overleaf 用論文 LaTeX

## ファイル構成

```
paper/
  main.tex              … メインファイル（ここからコンパイル）
  sections/
    01_introduction.tex
    02_research_story.tex
    03_proposed_method.tex
    04_experiment.tex
    05_validity.tex
    06_implementation.tex
    07_discussion.tex
    08_conclusion.tex
```

## Overleaf での使い方

1. [Overleaf](https://www.overleaf.com/) で New Project → Upload Project
2. `paper` フォルダごと ZIP にしてアップロード
3. **Menu → Compiler → LuaLaTeX** を選択
4. `main.tex` を Recompile

## 日本語が通らない場合

- Compiler が **LuaLaTeX** になっているか確認
- `jlreq` が使えない場合は，`main.tex` 先頭を次に差し替え:

```latex
\documentclass[a4paper,11pt]{ltjsarticle}
\usepackage[margin=25mm]{geometry}
```

## 著者・文献

- `main.tex` の `\author{}` を編集
- `\begin{thebibliography}` 内の文献はプレースホルダー（TODO）なので差し替え

## 元ドキュメント

- 手法詳細: `提案書類.md`
- 実装・手続き: `実装書類.md`
