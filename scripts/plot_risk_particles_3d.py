#!/usr/bin/env python3
"""Research-style particle scatter: (x, y, θ) with projections and sliced planes.
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


def estimate_ttc(x, y, theta, speed=1.2):
    """Deterministic TTC proxy from pose (same idea as before, no randomness)."""
    move_y = -np.cos(np.deg2rad(theta))  # θ=0 → toward worker (−y from +y)
    move_x = np.sin(np.deg2rad(theta))
    t = np.full(np.shape(x), np.inf, dtype=float)
    approach = (y > 0.05) & (move_y < -0.05)
    t[approach] = y[approach] / (np.abs(move_y[approach]) * speed)
    cross = (~approach) & (np.abs(y) < 3.0) & (np.abs(move_x) > 0.35) & (x * move_x < 0)
    t[cross] = np.abs(x[cross]) / (np.abs(move_x[cross]) * speed)
    return np.clip(t, 0.0, 8.0)


def build_grid(dx=1.0, dy=1.0, dtheta=30.0):
    """Evenly spaced lattice in (x, y, θ)."""
    xs = np.arange(-14.0, 14.0 + 1e-9, dx)
    ys = np.arange(0.0, 14.0 + 1e-9, dy)
    thetas = np.arange(-180.0, 180.0 + 1e-9, dtheta)
    xx, yy, tt = np.meshgrid(xs, ys, thetas, indexing="xy")
    x = xx.ravel()
    y = yy.ravel()
    theta = tt.ravel()
    ttc = estimate_ttc(x, y, theta)
    d = np.hypot(x, y)
    R = np.array([score(di, ti) for di, ti in zip(d, ttc)])
    keep = R > 0
    return x[keep], y[keep], theta[keep], R[keep]


def plot_projection(ax, u, v, colors, examples, u_key, v_key, xlabel, ylabel, title):
    ax.scatter(u, v, c=colors, s=10, linewidths=0, rasterized=True, zorder=1)
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


def plot_slice_scatter(ax, u, v, colors, xlabel, ylabel, title, examples_uv=None):
    ax.scatter(u, v, c=colors, s=18, linewidths=0, rasterized=True, zorder=1)
    if examples_uv:
        for name, uu, vv, rgba in examples_uv:
            ax.scatter([uu], [vv], c=[rgba], s=70, edgecolors="k", linewidths=0.6, zorder=3)
            ax.text(uu, vv, f"  {name}", fontsize=8, zorder=4)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    ax.set_title(title, fontsize=10)
    ax.grid(True, alpha=0.25, linewidth=0.5)


def examples_on_theta_slice(examples, theta0, atol=1.0):
    out = []
    for name, (xi, yi, thi, ti) in examples.items():
        if abs(thi - theta0) <= atol or abs(abs(thi - theta0) - 360) <= atol:
            Ri = score(np.hypot(xi, yi), ti)
            out.append((name, xi, yi, risk_color(Ri)))
    return out


def examples_on_y_slice(examples, y0, atol=0.6):
    out = []
    for name, (xi, yi, thi, ti) in examples.items():
        if abs(yi - y0) <= atol:
            Ri = score(np.hypot(xi, yi), ti)
            out.append((name, xi, thi, risk_color(Ri)))
    return out


def examples_on_x_slice(examples, x0, atol=0.6):
    out = []
    for name, (xi, yi, thi, ti) in examples.items():
        if abs(xi - x0) <= atol:
            Ri = score(np.hypot(xi, yi), ti)
            out.append((name, yi, thi, risk_color(Ri)))
    return out


def save_fig(fig, stem: str):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    pdf = OUT_DIR / f"{stem}.pdf"
    png = OUT_DIR / f"{stem}.png"
    fig.savefig(pdf, bbox_inches="tight")
    fig.savefig(png, dpi=200, bbox_inches="tight")
    print(f"wrote {pdf}")
    print(f"wrote {png}")


def save_theta_slices(x, y, theta, colors, examples):
    """xy planes at representative headings."""
    slice_thetas = [-90, -60, 0, 60, 90, 180]
    fig, axes = plt.subplots(2, 3, figsize=(12, 8), dpi=150, sharex=True, sharey=True)
    for ax, th0 in zip(axes.ravel(), slice_thetas):
        m = np.isclose(theta, th0)
        plot_slice_scatter(
            ax,
            x[m],
            y[m],
            colors[m],
            "x (m)",
            "y (m)",
            f"θ = {th0}°  (xy slice)",
            examples_on_theta_slice(examples, th0),
        )
        ax.set_xlim(-15, 15)
        ax.set_ylim(-0.5, 14.5)
        ax.set_aspect("equal", adjustable="box")
    fig.suptitle("Pose space sliced by heading θ — each panel is one xy plane", fontsize=12)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_theta")
    plt.close(fig)


def save_y_slices(x, y, theta, colors, examples):
    """xz planes at representative forward distances."""
    slice_ys = [1, 3, 6, 10]
    fig, axes = plt.subplots(2, 2, figsize=(10, 8), dpi=150, sharex=True, sharey=True)
    for ax, y0 in zip(axes.ravel(), slice_ys):
        m = np.isclose(y, float(y0))
        plot_slice_scatter(
            ax,
            x[m],
            theta[m],
            colors[m],
            "x (m)",
            "θ (°)",
            f"y = {y0} m  (xz slice)",
            examples_on_y_slice(examples, y0),
        )
        ax.set_xlim(-15, 15)
        ax.set_ylim(-190, 190)
    fig.suptitle("Pose space sliced by forward distance y — each panel is one xz plane", fontsize=12)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_y")
    plt.close(fig)


def save_x_slices(x, y, theta, colors, examples):
    """yz planes at representative lateral positions."""
    slice_xs = [-6, 0, 6]
    fig, axes = plt.subplots(1, 3, figsize=(12, 4.5), dpi=150, sharex=True, sharey=True)
    for ax, x0 in zip(axes.ravel(), slice_xs):
        m = np.isclose(x, float(x0))
        plot_slice_scatter(
            ax,
            y[m],
            theta[m],
            colors[m],
            "y (m)",
            "θ (°)",
            f"x = {x0} m  (yz slice)",
            examples_on_x_slice(examples, x0),
        )
        ax.set_xlim(-0.5, 14.5)
        ax.set_ylim(-190, 190)
    fig.suptitle("Pose space sliced by lateral position x — each panel is one yz plane", fontsize=12)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_x")
    plt.close(fig)


def save_slice_overview_3d(x, y, theta, colors, examples):
    """3D view with a few highlighted cutting planes (θ)."""
    fig = plt.figure(figsize=(9, 7), dpi=150)
    ax = fig.add_subplot(111, projection="3d")
    ax.scatter(x, y, theta, c=colors, s=3, linewidths=0, depthshade=False, rasterized=True, alpha=0.25)

    for th0 in (-90, 0, 90):
        m = np.isclose(theta, th0)
        ax.scatter(x[m], y[m], theta[m], c=colors[m], s=14, linewidths=0, depthshade=False)

    xx = np.linspace(-14, 14, 8)
    yy = np.linspace(0, 14, 8)
    XX, YY = np.meshgrid(xx, yy)
    for th0, alpha in [(0, 0.12), (90, 0.08), (-90, 0.08)]:
        ZZ = np.full_like(XX, th0)
        ax.plot_surface(XX, YY, ZZ, color="gray", alpha=alpha, linewidth=0, antialiased=False)

    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=70, edgecolors="k", linewidths=0.5)
        ax.text(xi, yi, thi, f" {name}", fontsize=7)

    ax.set_xlabel("x (m)")
    ax.set_ylabel("y (m)")
    ax.set_zlabel("θ (°)")
    ax.set_title("3D with slice planes at θ = −90°, 0°, 90°")
    ax.view_init(elev=22, azim=-58)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_3d_planes")
    plt.close(fig)


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

    x, y, theta, R = build_grid(dx=1.0, dy=1.0, dtheta=30.0)
    colors = np.array([risk_color(r) for r in R])
    print(f"grid points kept: {len(x)} (Δx=1m, Δy=1m, Δθ=30°)")

    # ---- 3D ----
    fig3d = plt.figure(figsize=(8.5, 6.5), dpi=150)
    ax3d = fig3d.add_subplot(111, projection="3d")
    ax3d.scatter(x, y, theta, c=colors, s=8, linewidths=0, depthshade=False, rasterized=True)
    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax3d.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=80, edgecolors="k", linewidths=0.6)
        ax3d.text(xi, yi, thi, f"  {name}", fontsize=8)
    ax3d.set_xlabel("x (m)  lateral")
    ax3d.set_ylabel("y (m)  forward")
    ax3d.set_zlabel("θ (deg) heading")
    ax3d.set_title("3D: regular grid (Δx=Δy=1 m, Δθ=30°) colored by R")
    ax3d.view_init(elev=22, azim=-58)
    fig3d.tight_layout()
    save_fig(fig3d, "risk_particles_3d")
    plt.close(fig3d)

    # ---- orthographic projections ----
    projections = [
        ("risk_particles_xy", x, y, "x", "y", "x (m) lateral", "y (m) forward", "Projection: xy plane (uniform grid)"),
        ("risk_particles_yz", y, theta, "y", "theta", "y (m) forward", "θ (deg) heading", "Projection: yz plane (uniform grid)"),
        ("risk_particles_xz", x, theta, "x", "theta", "x (m) lateral", "θ (deg) heading", "Projection: xz plane (uniform grid)"),
    ]
    for stem, u, v, uk, vk, xlabel, ylabel, title in projections:
        fig, ax = plt.subplots(figsize=(7.5, 6.0), dpi=150)
        plot_projection(ax, u, v, colors, examples, uk, vk, xlabel, ylabel, title)
        fig.tight_layout()
        save_fig(fig, stem)
        plt.close(fig)

    # ---- 2×2 panel ----
    figp = plt.figure(figsize=(11, 9), dpi=150)
    ax0 = figp.add_subplot(2, 2, 1, projection="3d")
    ax0.scatter(x, y, theta, c=colors, s=6, linewidths=0, depthshade=False, rasterized=True)
    for name, (xi, yi, thi, ti) in examples.items():
        Ri = score(np.hypot(xi, yi), ti)
        ax0.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=55, edgecolors="k", linewidths=0.5)
        ax0.text(xi, yi, thi, f" {name}", fontsize=7)
    ax0.set_xlabel("x (m)")
    ax0.set_ylabel("y (m)")
    ax0.set_zlabel("θ (°)")
    ax0.set_title("3D grid")
    ax0.view_init(elev=22, azim=-58)

    ax_xy = figp.add_subplot(2, 2, 2)
    plot_projection(ax_xy, x, y, colors, examples, "x", "y", "x (m)", "y (m)", "xy")
    ax_yz = figp.add_subplot(2, 2, 3)
    plot_projection(ax_yz, y, theta, colors, examples, "y", "theta", "y (m)", "θ (°)", "yz")
    ax_xz = figp.add_subplot(2, 2, 4)
    plot_projection(ax_xz, x, theta, colors, examples, "x", "theta", "x (m)", "θ (°)", "xz")

    figp.suptitle(
        "AGV pose regular grid by display risk R — 3D and orthographic projections",
        fontsize=12,
    )
    figp.tight_layout()
    save_fig(figp, "risk_particles_projections_panel")
    plt.close(figp)

    # ---- sliced planes at representative positions ----
    save_theta_slices(x, y, theta, colors, examples)
    save_y_slices(x, y, theta, colors, examples)
    save_x_slices(x, y, theta, colors, examples)
    save_slice_overview_3d(x, y, theta, colors, examples)


if __name__ == "__main__":
    main()
