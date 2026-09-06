#!/usr/bin/env python3
"""Research-style particle scatter: (x, y, θ) with 3D + xy / yz / xz projections.
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


def build_cloud(rng, n=2500):
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
    return x[keep], y[keep], theta[keep], R[keep]


def plot_projection(ax, u, v, colors, examples, u_key, v_key, xlabel, ylabel, title):
    """2D orthographic projection of the particle cloud."""
    ax.scatter(u, v, c=colors, s=4, linewidths=0, rasterized=True, zorder=1)
    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        uu = {"x": xi, "y": yi, "theta": thi}[u_key]
        vv = {"x": xi, "y": yi, "theta": thi}[v_key]
        ax.scatter([uu], [vv], c=[risk_color(Ri)], s=70, edgecolors="k", linewidths=0.6, zorder=3)
        ax.text(uu, vv, f"  {name}", fontsize=8, zorder=4)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title)
    ax.set_aspect("auto")
    ax.grid(True, alpha=0.25, linewidth=0.5)


def save_fig(fig, stem: str):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    pdf = OUT_DIR / f"{stem}.pdf"
    png = OUT_DIR / f"{stem}.png"
    fig.savefig(pdf, bbox_inches="tight")
    fig.savefig(png, dpi=200, bbox_inches="tight")
    print(f"wrote {pdf}")
    print(f"wrote {png}")


def main():
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

    rng = np.random.default_rng(42)
    x, y, theta, R = build_cloud(rng)
    colors = np.array([risk_color(r) for r in R])

    # ---- 3D ----
    fig3d = plt.figure(figsize=(8.5, 6.5), dpi=150)
    ax3d = fig3d.add_subplot(111, projection="3d")
    ax3d.scatter(x, y, theta, c=colors, s=4, linewidths=0, depthshade=False, rasterized=True)
    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax3d.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=80, edgecolors="k", linewidths=0.6)
        ax3d.text(xi, yi, thi, f"  {name}", fontsize=8)
    ax3d.set_xlabel("x (m)  lateral")
    ax3d.set_ylabel("y (m)  forward")
    ax3d.set_zlabel("θ (deg) heading")
    ax3d.set_title("3D: AGV pose cloud colored by display risk R")
    ax3d.view_init(elev=22, azim=-58)
    fig3d.tight_layout()
    save_fig(fig3d, "risk_particles_3d")

    # ---- three orthographic projections (separate files) ----
    projections = [
        ("risk_particles_xy", x, y, "x", "y", "x (m) lateral", "y (m) forward", "Projection: xy plane (top view)"),
        ("risk_particles_yz", y, theta, "y", "theta", "y (m) forward", "θ (deg) heading", "Projection: yz plane"),
        ("risk_particles_xz", x, theta, "x", "theta", "x (m) lateral", "θ (deg) heading", "Projection: xz plane"),
    ]
    for stem, u, v, uk, vk, xlabel, ylabel, title in projections:
        fig, ax = plt.subplots(figsize=(7.5, 6.0), dpi=150)
        plot_projection(ax, u, v, colors, examples, uk, vk, xlabel, ylabel, title)
        fig.tight_layout()
        save_fig(fig, stem)
        plt.close(fig)

    # ---- 2×2 panel (3D + xy / yz / xz) for slides ----
    figp = plt.figure(figsize=(11, 9), dpi=150)
    ax0 = figp.add_subplot(2, 2, 1, projection="3d")
    ax0.scatter(x, y, theta, c=colors, s=3, linewidths=0, depthshade=False, rasterized=True)
    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax0.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=55, edgecolors="k", linewidths=0.5)
        ax0.text(xi, yi, thi, f" {name}", fontsize=7)
    ax0.set_xlabel("x (m)")
    ax0.set_ylabel("y (m)")
    ax0.set_zlabel("θ (°)")
    ax0.set_title("3D")
    ax0.view_init(elev=22, azim=-58)

    ax_xy = figp.add_subplot(2, 2, 2)
    plot_projection(ax_xy, x, y, colors, examples, "x", "y", "x (m)", "y (m)", "xy")
    ax_yz = figp.add_subplot(2, 2, 3)
    plot_projection(ax_yz, y, theta, colors, examples, "y", "theta", "y (m)", "θ (°)", "yz")
    ax_xz = figp.add_subplot(2, 2, 4)
    plot_projection(ax_xz, x, theta, colors, examples, "x", "theta", "x (m)", "θ (°)", "xz")

    figp.suptitle("AGV pose cloud by display risk R — 3D and orthographic projections", fontsize=12)
    figp.tight_layout()
    save_fig(figp, "risk_particles_projections_panel")
    plt.close(fig3d)
    plt.close(figp)


if __name__ == "__main__":
    main()
