#!/usr/bin/env python3
"""Aggregate HRI AGV experiment xlsx files and plot condition comparisons.

Reads:  C:/lab/Lessismore-Robot-data/origin/HRI_AGV_Result_*.xlsx  (Summary sheet)
Writes: C:/lab/Lessismore-Robot-data/aggregated/*.csv
        C:/lab/Lessismore-Robot-data/figures/*.png

Aggregation (within-subjects friendly):
  1) trial-level: every case row from every session file
  2) session × condition: mean across cases (primary unit for condition compare)
  3) condition: mean ± SD of session means

Requires: pip install pandas openpyxl matplotlib numpy
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.patches import Patch

DATA_ROOT = Path(r"C:\lab\Lessismore-Robot-data")
ORIGIN = DATA_ROOT / "origin"
AGG_DIR = DATA_ROOT / "aggregated"
FIG_DIR = DATA_ROOT / "figures"

CONDITION_ORDER = ["Baseline", "NoAR", "Proposed"]
CONDITION_ALIASES = {
    "baseline": "Baseline",
    "noar": "NoAR",
    "no-ar": "NoAR",
    "no_ar": "NoAR",
    "proposed": "Proposed",
}

# Paper-like palette (ref: grouped boxplot — soft red / blue / green, black edges)
CONDITION_COLORS = {
    "Baseline": "#F29F9B",
    "NoAR": "#85B7F9",
    "Proposed": "#A9F5A9",
}
CONDITION_LABELS = {
    "Baseline": "Baseline",
    "NoAR": "No-AR",
    "Proposed": "Proposed",
}

# Primary DVs aligned with H1–H3 in paper/sections/03_experiment.tex
METRICS = [
    ("Collisions", "Collisions (count)", "H1 safety"),
    ("PathMinApproach_m", "Path min approach (m)", "H1 safety (higher=safer)"),
    ("AgvYieldWait_s", "AGV yield wait (s)", "H1/H2 (interpret with care)"),
    ("CompletionTime_s", "Completion time (s)", "H2/H3 efficiency"),
    ("TraveledPath_m", "Traveled path (m)", "H2/H3 efficiency"),
    ("HmdYawRotation_deg", "HMD yaw rotation (°)", "H2 look-around"),
    ("HmdPitchRotation_deg", "HMD pitch rotation (°)", "H2 look-around"),
]


def normalize_condition(value) -> str:
    key = str(value).strip().lower().replace(" ", "")
    return CONDITION_ALIASES.get(key, str(value).strip())


def parse_collision_events(text: str) -> list[dict]:
    """Parse ((t,id,x,z),...) or legacy ((t,id,x,y,z),...)."""
    if text is None or (isinstance(text, float) and np.isnan(text)):
        return []
    s = str(text).strip()
    if not s or s == "()":
        return []
    events = []
    for m in re.finditer(r"\(([^()]+)\)", s):
        parts = [p.strip() for p in m.group(1).split(",")]
        if len(parts) == 4:
            t, rid, x, z = parts
            events.append(
                {
                    "Time_s": float(t),
                    "RobotId": int(float(rid)),
                    "X": float(x),
                    "Z": float(z),
                }
            )
        elif len(parts) == 5:
            t, rid, x, _y, z = parts
            events.append(
                {
                    "Time_s": float(t),
                    "RobotId": int(float(rid)),
                    "X": float(x),
                    "Z": float(z),
                }
            )
    return events


def load_all_trials(origin: Path) -> pd.DataFrame:
    files = sorted(origin.glob("HRI_AGV_Result_*.xlsx"))
    if not files:
        raise FileNotFoundError(f"No HRI_AGV_Result_*.xlsx in {origin}")

    frames = []
    for path in files:
        df = pd.read_excel(path, sheet_name="Summary")
        df["SourceFile"] = path.name
        frames.append(df)

    all_df = pd.concat(frames, ignore_index=True)
    all_df["Condition"] = all_df["Condition"].map(normalize_condition)
    all_df["CaseIndex"] = pd.to_numeric(all_df["CaseIndex"], errors="coerce").astype("Int64")

    for col, _, _ in METRICS:
        if col in all_df.columns:
            all_df[col] = pd.to_numeric(all_df[col], errors="coerce")

    # Empty PathMinApproach (never on path) stays NaN; do not fill with 0.
    return all_df


def session_condition_means(trials: pd.DataFrame) -> pd.DataFrame:
    metric_cols = [m for m, _, _ in METRICS if m in trials.columns]
    agg = {
        "N_cases": ("CaseIndex", "count"),
        "Cases": ("CaseIndex", lambda s: ",".join(str(int(x)) for x in sorted(s.dropna().unique()))),
    }
    for col in metric_cols:
        agg[col] = (col, "mean")
    if "Collisions" in trials.columns:
        agg["Collisions_sum"] = ("Collisions", "sum")

    return trials.groupby(["SessionID", "Condition"], dropna=False).agg(**agg).reset_index()


def condition_summary(session_means: pd.DataFrame) -> pd.DataFrame:
    metric_cols = [m for m, _, _ in METRICS if m in session_means.columns]
    rows = []
    for cond in CONDITION_ORDER:
        sub = session_means[session_means["Condition"] == cond]
        if sub.empty:
            continue
        row = {"Condition": cond, "N_sessions": len(sub)}
        for col in metric_cols:
            row[f"{col}_mean"] = sub[col].mean(skipna=True)
            row[f"{col}_sd"] = sub[col].std(skipna=True, ddof=1) if len(sub) > 1 else 0.0
            row[f"{col}_sem"] = (
                row[f"{col}_sd"] / np.sqrt(sub[col].notna().sum())
                if sub[col].notna().sum() > 0
                else np.nan
            )
        rows.append(row)
    return pd.DataFrame(rows)


def style_axes(ax: plt.Axes) -> None:
    """White face, black spines, horizontal dashed gray grid only. No title."""
    ax.set_facecolor("white")
    ax.set_axisbelow(True)
    ax.yaxis.grid(True, linestyle="--", linewidth=0.8, color="#B0B0B0", alpha=0.85)
    ax.xaxis.grid(False)
    for spine in ax.spines.values():
        spine.set_color("black")
        spine.set_linewidth(1.0)
    ax.tick_params(colors="black")
    ax.set_title("")


def apply_boxplot_style(bp, colors: list[str]) -> None:
    for patch, color in zip(bp["boxes"], colors):
        patch.set_facecolor(color)
        patch.set_edgecolor("black")
        patch.set_linewidth(1.0)
    for key in ("whiskers", "caps", "medians"):
        for line in bp[key]:
            line.set_color("black")
            line.set_linewidth(1.0)
    for line in bp.get("fliers", []):
        line.set_markeredgecolor("black")
        line.set_markerfacecolor("white")


def save_fig(fig: plt.Figure, path: Path) -> None:
    """Atomic-ish save; falls back if the target PNG is locked in a viewer."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp.png")
    try:
        fig.savefig(tmp, dpi=160, facecolor="white")
        tmp.replace(path)
    except OSError:
        alt = path.with_name(path.stem + "_new" + path.suffix)
        fig.savefig(alt, dpi=160, facecolor="white")
        print(f"warning: could not overwrite {path.name}; wrote {alt.name}")
    finally:
        if tmp.exists():
            try:
                tmp.unlink()
            except OSError:
                pass
        plt.close(fig)


def plot_condition_bars(session_means: pd.DataFrame, out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    for col, ylabel, _note in METRICS:
        if col not in session_means.columns:
            continue
        means, sems, labels, colors = [], [], [], []
        for cond in CONDITION_ORDER:
            sub = session_means.loc[session_means["Condition"] == cond, col]
            if sub.empty:
                continue
            labels.append(CONDITION_LABELS[cond])
            colors.append(CONDITION_COLORS[cond])
            means.append(sub.mean(skipna=True))
            n = sub.notna().sum()
            sd = sub.std(skipna=True, ddof=1) if n > 1 else 0.0
            sems.append(sd / np.sqrt(n) if n > 0 else 0.0)

        if not labels:
            continue

        fig, ax = plt.subplots(figsize=(5.2, 3.6), facecolor="white")
        x = np.arange(len(labels))
        bars = ax.bar(
            x,
            means,
            yerr=sems,
            capsize=4,
            color=colors,
            edgecolor="black",
            linewidth=1.0,
            error_kw={"ecolor": "black", "elinewidth": 1.0, "capthick": 1.0},
        )
        ax.set_xticks(x)
        ax.set_xticklabels(labels)
        ax.set_ylabel(ylabel)
        style_axes(ax)
        ax.legend(bars, labels, loc="lower right", frameon=True, fancybox=False, edgecolor="#888888")
        fig.tight_layout()
        save_fig(fig, out_dir / f"bar_{col}.png")


def plot_case_boxplots(trials: pd.DataFrame, out_dir: Path) -> None:
    """Case-level distribution (exploratory; primary stats use session means)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    for col, ylabel, _ in METRICS:
        if col not in trials.columns:
            continue
        data, labels, colors = [], [], []
        for cond in CONDITION_ORDER:
            vals = trials.loc[trials["Condition"] == cond, col].dropna().to_numpy()
            if len(vals) == 0:
                continue
            data.append(vals)
            labels.append(CONDITION_LABELS[cond])
            colors.append(CONDITION_COLORS[cond])
        if not data:
            continue
        fig, ax = plt.subplots(figsize=(5.2, 3.6), facecolor="white")
        bp = ax.boxplot(
            data,
            tick_labels=labels,
            patch_artist=True,
            showfliers=True,
            medianprops={"color": "black", "linewidth": 1.0},
        )
        apply_boxplot_style(bp, colors)
        ax.set_ylabel(ylabel)
        style_axes(ax)
        handles = [Patch(facecolor=c, edgecolor="black", label=l) for c, l in zip(colors, labels)]
        ax.legend(handles=handles, loc="lower right", frameon=True, fancybox=False, edgecolor="#888888")
        fig.tight_layout()
        save_fig(fig, out_dir / f"box_case_{col}.png")


def plot_learning_curves(trials: pd.DataFrame, out_dir: Path) -> None:
    if "CompletionTime_s" not in trials.columns:
        return
    fig, ax = plt.subplots(figsize=(6.2, 3.8), facecolor="white")
    for cond in CONDITION_ORDER:
        sub = trials[trials["Condition"] == cond]
        if sub.empty:
            continue
        g = sub.groupby("CaseIndex")["CompletionTime_s"].agg(["mean", "sem"])
        ax.errorbar(
            g.index.astype(float),
            g["mean"],
            yerr=g["sem"],
            label=CONDITION_LABELS[cond],
            color=CONDITION_COLORS[cond],
            marker="o",
            markeredgecolor="black",
            ecolor="black",
            elinewidth=1.0,
            capsize=3,
        )
    ax.set_xlabel("Case index")
    ax.set_ylabel("Completion time (s)")
    style_axes(ax)
    ax.legend(loc="lower right", frameon=True, fancybox=False, edgecolor="#888888")
    fig.tight_layout()
    save_fig(fig, out_dir / "learning_CompletionTime_s.png")


def export_collision_events(trials: pd.DataFrame, out_path: Path) -> pd.DataFrame:
    rows = []
    for _, r in trials.iterrows():
        for e in parse_collision_events(r.get("CollisionEvents")):
            rows.append(
                {
                    "SessionID": r["SessionID"],
                    "Condition": r["Condition"],
                    "CaseIndex": r["CaseIndex"],
                    **e,
                }
            )
    df = pd.DataFrame(rows)
    df.to_csv(out_path, index=False)
    return df


def write_readme(agg_dir: Path, n_files: int, n_trials: int, n_sessions: int) -> None:
    text = f"""# Aggregated experiment data

Generated by `scripts/aggregate_experiment_data.py`.

## Layout
- `origin/` — raw session xlsx (append over time)
- `aggregated/` — cleaned tables
- `figures/` — PNG plots

## How to aggregate (this experiment)
Within-subjects: **3 conditions × 10 cases / person**.

1. **Case (trial) level** — `trials_all.csv`  
   Every completed case. Use for learning curves and diagnostics.
2. **Session × condition** — `by_session_condition.csv` (**primary**)  
   Mean of the 10 cases within each condition for each SessionID.  
   Condition comparisons should start here (reduces case noise).
3. **Condition** — `by_condition.csv`  
   Mean ± SD/SEM of session means. Good for overview bars while N is small.

### Metrics ↔ hypotheses
| Metric | Hypothesis focus |
|--------|------------------|
| Collisions ↓ | H1 safety |
| PathMinApproach_m ↑ | H1 clearance when on AGV path |
| AgvYieldWait_s | H1/H2 — more wait can mean more blocking; interpret with collisions |
| CompletionTime_s ↓ | H2/H3 efficiency |
| TraveledPath_m ↓ | H2/H3 less detour (not “always shorter=better” alone) |
| HmdYaw/Pitch ↓ | H2 less visual search / look-around |

### Stats later (when N grows)
- Repeated-measures ANOVA or Friedman on session×condition means
- Pairwise: Proposed vs Baseline, Proposed vs NoAR (Bonferroni)

## This run
- source files: {n_files}
- trial rows: {n_trials}
- sessions: {n_sessions}
"""
    (agg_dir / "README.md").write_text(text, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--origin", type=Path, default=ORIGIN)
    parser.add_argument("--agg-dir", type=Path, default=AGG_DIR)
    parser.add_argument("--fig-dir", type=Path, default=FIG_DIR)
    args = parser.parse_args()

    args.agg_dir.mkdir(parents=True, exist_ok=True)
    args.fig_dir.mkdir(parents=True, exist_ok=True)

    trials = load_all_trials(args.origin)
    session_means = session_condition_means(trials)
    cond_summary = condition_summary(session_means)

    trials.to_csv(args.agg_dir / "trials_all.csv", index=False)
    session_means.to_csv(args.agg_dir / "by_session_condition.csv", index=False)
    cond_summary.to_csv(args.agg_dir / "by_condition.csv", index=False)
    export_collision_events(trials, args.agg_dir / "collision_events.csv")

    plot_condition_bars(session_means, args.fig_dir)
    plot_case_boxplots(trials, args.fig_dir)
    plot_learning_curves(trials, args.fig_dir)

    n_files = trials["SourceFile"].nunique()
    n_sessions = trials["SessionID"].nunique()
    write_readme(args.agg_dir, n_files, len(trials), n_sessions)

    print(f"trials: {len(trials)} from {n_files} files / {n_sessions} sessions")
    print(f"wrote: {args.agg_dir}")
    print(f"figures: {args.fig_dir}")
    if not cond_summary.empty:
        print(cond_summary.to_string(index=False))


if __name__ == "__main__":
    main()
