#!/usr/bin/env python3
"""Research-style 3D particle scatter: position (x,y) + heading (theta).
Point colors match RiskToVisualMapper (HSV).
Requires: pip install numpy matplotlib
"""
import colorsys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

T_MAX, D_MAX, GAMMA = 4.0, 13.6, 0.6
OUT_DIR = Path(__file__).resolve().parent.parent / "paper" / "google-slides"


def risk_color(R: float):
    R = float(np.clip(R, 0.0, 1.0))
    h = (1.0 - R) * 180.0 / 360.0
    s = 0.4 + 0.6 * R
    v = 0.5 + 0.3 * R
    a = 0.35 + 0.65 * R
    r, g, b = colorsys.hsv_to_rgb(h, s, v)
    return (r, g, b, a)


def score(d, t):
    r_ttc = 0.0 if (not np.isfinite(t) or t > T_MAX) else (max(0.0, 1.0 - t / T_MAX) ** GAMMA)
    r_prox = 0.0 if d >= D_MAX else (max(0.0, 1.0 - d / D_MAX) ** GAMMA)
    if not ((np.isfinite(t) and t <= T_MAX) or d <= D_MAX):
        return 0.0
    return max(0.08, r_ttc, r_prox)


def main():
    # --- labeled examples (worker frame: +y forward, +x right) ---
    examples = dict(
        A=(0.0, 10.0, 0.0, 1.2),
        B=(1.5, 0.2, 180.0, np.inf),
        C=(0.0, 3.0, 0.0, 1.0),
        D=(2.0, 1.0, 90.0, 3.5),
        E=(6.8, 0.5, 90.0, 2.0),
        G=(-5.5, 6.0, 45.0, 1.4),
        G2=(5.5, 6.0, -45.0, 1.4),
        H=(-11.0, 2.0, 90.0, 1.5),
    )

    # --- dense particle cloud (paper figure style) ---
    rng = np.random.default_rng(42)
    n = 2500
    ang = (rng.random(n) - 0.5) * np.pi
    rad = 0.8 + rng.random(n) * 14.0
    x = np.sin(ang) * rad
    y = np.cos(ang) * rad
    theta = rng.uniform(-180, 180, n)
    mask = rng.random(n) < 0.45
    theta[mask] = np.where(x[mask] < 0, 90.0, -90.0) + rng.normal(0, 12, mask.sum())
    speed = 0.6 + rng.random(n) * 1.6
    move_y = -np.cos(np.deg2rad(theta))
    t = np.full(n, np.inf)
    approach = (y > 0.3) & (move_y < -0.05)
    t[approach] = y[approach] / (np.abs(move_y[approach]) * speed[approach])
    t = np.clip(t, 0, 8)
    d = np.hypot(x, y)
    R = np.array([score(di, ti) for di, ti in zip(d, t)])
    keep = R > 0
    x, y, theta, R = x[keep], y[keep], theta[keep], R[keep]
    colors = np.array([risk_color(r) for r in R])

    fig = plt.figure(figsize=(8.5, 6.5), dpi=150)
    ax = fig.add_subplot(111, projection="3d")

    ax.scatter(
        x,
        y,
        theta,
        c=colors,
        s=4,
        linewidths=0,
        depthshade=False,
        rasterized=True,
    )

    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=80, edgecolors="k", linewidths=0.6)
        ax.text(xi, yi, thi, f"  {name}", fontsize=8)

    ax.set_xlabel("x (m)  lateral")
    ax.set_ylabel("y (m)  forward")
    ax.set_zlabel("θ (deg) heading")
    ax.set_title("AGV pose cloud colored by display risk R")
    ax.view_init(elev=22, azim=-58)
    plt.tight_layout()

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    pdf = OUT_DIR / "risk_particles_xyz_theta.pdf"
    png = OUT_DIR / "risk_particles_xyz_theta.png"
    plt.savefig(pdf, bbox_inches="tight")
    plt.savefig(png, dpi=200, bbox_inches="tight")
    print(f"wrote {pdf}")
    print(f"wrote {png}")
    plt.show()


if __name__ == "__main__":
    main()
