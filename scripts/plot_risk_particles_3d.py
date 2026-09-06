#!/usr/bin/env python3
"""Research-style particle scatter: (x, y, θ) with projections and sliced planes.
3D graphs use PTTC + lateral-crossing path TTC (s≈|x|/|sinθ|) + proximity.
Front-approach path proxy s≈y/|cosθ| is NOT used (Unity still uses full path TTC).
Point color and opacity match RiskToVisualMapper (HSV + α=0.35+0.65R).
Requires: pip install numpy matplotlib
"""
import colorsys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

T_MAX, D_MAX, GAMMA = 4.0, 13.6, 0.6
W_P, W_C, W_D = 0.55, 0.30, 0.15
SCORE_FLOOR = 0.08
V_CLOSE_EPS = 0.05
DEFAULT_SPEED = 2.0
OUT_DIR = Path(__file__).resolve().parent.parent / "paper" / "google-slides"


def risk_color(R: float):
    R = float(np.clip(R, 0.0, 1.0))
    h = (1.0 - R) * 180.0 / 360.0
    s = 0.4 + 0.6 * R
    v = 0.5 + 0.3 * R
    a = 0.35 + 0.65 * R
    r, g, b = colorsys.hsv_to_rgb(h, s, v)
    return (r, g, b, a)


def time_to_score(t: float) -> float:
    if not np.isfinite(t) or t > T_MAX:
        return 0.0
    return max(0.0, 1.0 - t / T_MAX) ** GAMMA


def score_from_times(d: float, tp: float, tc: float) -> float:
    """Display gate: d ≤ D_max. R = wp·Rp + wc·Rc + wd·Rd."""
    if d > D_MAX:
        return 0.0
    rp = time_to_score(tp)
    rc = time_to_score(tc)
    rd = 0.0 if d >= D_MAX else max(0.0, 1.0 - d / D_MAX) ** GAMMA
    r = W_P * rp + W_C * rc + W_D * rd
    return max(SCORE_FLOOR, min(1.0, r))


def estimate_pttc(x, y, theta, speed=DEFAULT_SPEED):
    """PTTC to worker at origin. θ=0 is +Y. r = -pos, v_close = max(0, r̂·v)."""
    x = np.asarray(x, dtype=float)
    y = np.asarray(y, dtype=float)
    theta = np.asarray(theta, dtype=float)
    d = np.hypot(x, y)
    t = np.full(np.shape(x), np.inf, dtype=float)
    near = d < 1e-4
    t[near] = 0.0
    ok = ~near
    move_x = np.sin(np.deg2rad(theta)) * speed
    move_y = np.cos(np.deg2rad(theta)) * speed
    # r_hat = (-x, -y) / d  →  r_hat · v = -(x vx + y vy) / d
    v_close = np.zeros_like(d)
    v_close[ok] = -(x[ok] * move_x[ok] + y[ok] * move_y[ok]) / d[ok]
    closing = ok & (v_close >= V_CLOSE_EPS)
    t[closing] = d[closing] / v_close[closing]
    return t


def estimate_path_ttc(x, y, theta, speed=DEFAULT_SPEED):
    """Lateral path-TTC: s≈|x|/|sinθ|, Tc=s/v.
    Keep only headings that hit the y-axis (x·sinθ < 0), for both signs of x·y.
    Omit θ≈0.
    """
    x = np.asarray(x, dtype=float)
    theta = np.asarray(theta, dtype=float)
    th = np.mod(theta + 180.0, 360.0) - 180.0
    omit0 = np.abs(th) < 0.5
    sin_th = np.sin(np.deg2rad(theta))
    toward_y_axis = x * sin_th < 0  # heading toward x=0 (any quadrant)
    t = np.full(np.shape(x), np.inf, dtype=float)
    ok = (~omit0) & toward_y_axis
    t[ok] = np.abs(x[ok]) / (np.maximum(np.abs(sin_th[ok]), 1e-6) * speed)
    return t


def score_pose(x, y, theta, speed=DEFAULT_SPEED) -> float:
    d = float(np.hypot(x, y))
    tp = float(estimate_pttc(x, y, theta, speed))
    tc = float(estimate_path_ttc(x, y, theta, speed))
    return score_from_times(d, tp, tc)


def build_grid(dx=1.0, dy=1.0, dtheta=30.0):
    """Evenly spaced lattice in (x, y, θ)."""
    xs = np.arange(-15.0, 15.0 + 1e-9, dx)
    ys = np.arange(-15.0, 15.0 + 1e-9, dy)
    thetas = np.arange(-180.0, 180.0 + 1e-9, dtheta)
    xx, yy, tt = np.meshgrid(xs, ys, thetas, indexing="xy")
    x = xx.ravel()
    y = yy.ravel()
    theta = tt.ravel()
    d = np.hypot(x, y)
    tp = estimate_pttc(x, y, theta)
    tc = estimate_path_ttc(x, y, theta)
    R = np.array([score_from_times(di, tpi, tci) for di, tpi, tci in zip(d, tp, tc)])
    keep = R > 0
    return x[keep], y[keep], theta[keep], R[keep]


def plot_projection(ax, u, v, colors, examples, u_key, v_key, xlabel, ylabel, title):
    ax.scatter(u, v, c=colors, s=10, linewidths=0, rasterized=True, zorder=1)
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
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
    for name, (xi, yi, thi) in examples.items():
        if abs(thi - theta0) <= atol or abs(abs(thi - theta0) - 360) <= atol:
            Ri = score_pose(xi, yi, thi)
            out.append((name, xi, yi, risk_color(Ri)))
    return out


def examples_on_y_slice(examples, y0, atol=0.6):
    out = []
    for name, (xi, yi, thi) in examples.items():
        if abs(yi - y0) <= atol:
            Ri = score_pose(xi, yi, thi)
            out.append((name, xi, thi, risk_color(Ri)))
    return out


def examples_on_x_slice(examples, x0, atol=0.6):
    out = []
    for name, (xi, yi, thi) in examples.items():
        if abs(xi - x0) <= atol:
            Ri = score_pose(xi, yi, thi)
            out.append((name, yi, thi, risk_color(Ri)))
    return out


def save_fig(fig, stem: str):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    png = OUT_DIR / f"{stem}.png"
    fig.savefig(png, dpi=200, bbox_inches="tight")
    print(f"wrote {png}")


def save_theta_slices(x, y, theta, colors, examples):
    """xy planes at representative headings."""
    slice_thetas = [-90, -135, 180, 135, 90, 0]
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
        ax.set_xlim(-15.5, 15.5)
        ax.set_ylim(-15.5, 15.5)
        ax.set_aspect("equal", adjustable="box")
    fig.suptitle(
        "Pose space sliced by heading θ — R = PTTC + cross pathTTC (s≈|x|/|sinθ|) + prox",
        fontsize=12,
    )
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_theta")
    plt.close(fig)


def save_y_slices(x, y, theta, colors, examples):
    """xz planes at representative forward distances."""
    slice_ys = [-10, -5, 0, 5, 10]
    fig, axes = plt.subplots(2, 3, figsize=(12, 8), dpi=150, sharex=True, sharey=True)
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
        ax.set_xlim(-15.5, 15.5)
        ax.set_ylim(-190, 190)
    # hide unused last axis if any
    if len(slice_ys) < axes.size:
        for ax in axes.ravel()[len(slice_ys):]:
            ax.set_visible(False)
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
        ax.set_xlim(-15.5, 15.5)
        ax.set_ylim(-190, 190)
    fig.suptitle("Pose space sliced by lateral position x — each panel is one yz plane", fontsize=12)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_x")
    plt.close(fig)


def save_slice_overview_3d(x, y, theta, colors, examples):
    """3D view with a few highlighted cutting planes (θ)."""
    fig = plt.figure(figsize=(9, 7), dpi=150)
    ax = fig.add_subplot(111, projection="3d")
    ax.scatter(x, y, theta, c=colors, s=3, linewidths=0, depthshade=False, rasterized=True)

    for th0 in (-90, 0, 180):
        m = np.isclose(theta, th0)
        ax.scatter(x[m], y[m], theta[m], c=colors[m], s=14, linewidths=0, depthshade=False)

    xx = np.linspace(-15, 15, 8)
    yy = np.linspace(-15, 15, 8)
    XX, YY = np.meshgrid(xx, yy)
    for th0, alpha in [(0, 0.12), (180, 0.10), (-90, 0.08)]:
        ZZ = np.full_like(XX, th0)
        ax.plot_surface(XX, YY, ZZ, color="gray", alpha=alpha, linewidth=0, antialiased=False)

    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
        ax.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=70, edgecolors="k", linewidths=0.5)
        ax.text(xi, yi, thi, f" {name}", fontsize=7)

    ax.set_xlabel("x (m)")
    ax.set_ylabel("y (m)")
    ax.set_zlabel("θ (°)")
    ax.set_title("3D slices: θ=0° (+Y), 180° (toward worker), −90°")
    ax.view_init(elev=22, azim=-58)
    fig.tight_layout()
    save_fig(fig, "risk_particles_slices_3d_planes")
    plt.close(fig)


def main():
    # (x, y, θ); θ=0 is +Y (same as worker). Approach from front = 180°.
    examples = dict(
        A=(0.0, 10.0, 180.0),
        B=(1.5, 0.2, 0.0),
        C=(0.0, 3.0, 180.0),
        D=(2.0, 1.0, 90.0),
        E=(6.8, 0.5, 90.0),
        G=(-5.5, 6.0, 135.0),
        G2=(5.5, 6.0, -135.0),
        H=(-11.0, 2.0, 90.0),
    )

    x, y, theta, R = build_grid(dx=1.0, dy=1.0, dtheta=15.0)
    colors = np.array([risk_color(r) for r in R])
    print(f"grid points kept: {len(x)} (Δx=1m, Δy=1m, Δθ=15°; θ=0 is +Y)")
    print(f"weights: wp={W_P}, wc={W_C}, wd={W_D}; path TTC=cross only s~|x|/|sin(theta)|; d<={D_MAX}")

    # ---- 3D ----
    fig3d = plt.figure(figsize=(8.5, 6.5), dpi=150)
    ax3d = fig3d.add_subplot(111, projection="3d")
    ax3d.scatter(x, y, theta, c=colors, s=8, linewidths=0, depthshade=False, rasterized=True)
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
        ax3d.scatter([xi], [yi], [thi], c=[risk_color(Ri)], s=80, edgecolors="k", linewidths=0.6)
        ax3d.text(xi, yi, thi, f"  {name}", fontsize=8)
    ax3d.set_xlabel("x (m)  lateral")
    ax3d.set_ylabel("y (m)  forward")
    ax3d.set_zlabel("θ (deg) heading")
    ax3d.set_title("3D: PTTC + cross pathTTC (s≈|x|/|sinθ|) + proximity")
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
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
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
        "AGV pose grid by R (PTTC + cross pathTTC s≈|x|/|sinθ| + proximity)",
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
