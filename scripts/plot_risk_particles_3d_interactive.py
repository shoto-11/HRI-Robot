#!/usr/bin/env python3
"""Interactive 3D risk particle viewer (drag to rotate, scroll to zoom).

Writes a self-contained HTML (Plotly CDN).
Color and opacity are continuous via per-point rgba (RiskToVisualMapper).
Note: scatter3d rejects opacity arrays; alpha must live in the rgba color.

Requires: pip install numpy
"""
from __future__ import annotations

import json
import webbrowser
from pathlib import Path

import numpy as np

from plot_risk_particles_3d import (
    DEFAULT_SPEED,
    D_MAX,
    W_C,
    W_D,
    W_P,
    build_grid,
    risk_color,
    score_pose,
)

OUT_DIR = Path(__file__).resolve().parent.parent / "paper" / "google-slides"
OUT_HTML = OUT_DIR / "risk_particles_3d_interactive.html"


def rgba_css(rgba) -> str:
    r, g, b, a = rgba
    return f"rgba({int(r * 255)},{int(g * 255)},{int(b * 255)},{a:.4f})"


def main(open_browser: bool = True):
    x, y, theta, R = build_grid(dx=1.0, dy=1.0, dtheta=15.0)
    x = np.asarray(x)
    y = np.asarray(y)
    theta = np.asarray(theta)
    R = np.asarray(R)

    colors = [rgba_css(risk_color(float(r))) for r in R]
    sizes = (1.8 + 4.5 * R).tolist()
    alphas = (0.35 + 0.65 * R).tolist()
    hover = [
        f"R={rj:.3f}<br>α={aj:.3f}<br>x={xj:.0f} m<br>y={yj:.0f} m<br>θ={tj:.0f}°"
        for xj, yj, tj, rj, aj in zip(x, y, theta, R, alphas)
    ]

    # continuous legend swatches (R=0..1)
    legend_R = [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]
    legend_colors = [rgba_css(risk_color(r)) for r in legend_R]

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
    ex_x, ex_y, ex_th, ex_c, ex_s, ex_t, ex_names = [], [], [], [], [], [], []
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
        col = risk_color(Ri)
        ex_x.append(xi)
        ex_y.append(yi)
        ex_th.append(thi)
        ex_c.append(rgba_css(col))
        ex_s.append(8.0 + 5.0 * Ri)
        ex_names.append(name)
        ex_t.append(
            f"{name}<br>R={Ri:.3f}<br>α={col[3]:.3f}<br>(x,y,θ)=({xi:g},{yi:g},{thi:g})"
        )

    payload = {
        "x": x.tolist(),
        "y": y.tolist(),
        "theta": theta.tolist(),
        "R": R.tolist(),
        "colors": colors,
        "sizes": sizes,
        "hover": hover,
        "legendR": legend_R,
        "legendColors": legend_colors,
        "ex": {
            "x": ex_x,
            "y": ex_y,
            "theta": ex_th,
            "colors": ex_c,
            "sizes": ex_s,
            "text": ex_t,
            "names": ex_names,
        },
        "meta": {
            "wp": W_P,
            "wc": W_C,
            "wd": W_D,
            "dmax": D_MAX,
            "speed": DEFAULT_SPEED,
            "n": int(len(x)),
        },
    }

    html = f"""<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>Risk particles 3D (interactive)</title>
  <script src="https://cdn.plot.ly/plotly-2.35.2.min.js"></script>
  <style>
    html, body {{ margin: 0; height: 100%; background: #e8e8ec; color: #222;
      font-family: "Segoe UI", Meiryo, sans-serif; }}
    #bar {{ padding: 10px 16px; background: #f7f7fa; border-bottom: 1px solid #ccc;
      display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }}
    #bar h1 {{ margin: 0; font-size: 16px; font-weight: 600; }}
    #bar span {{ color: #555; font-size: 13px; }}
    #bar button {{
      font: inherit; font-size: 13px; padding: 6px 14px; border-radius: 6px;
      border: 1px solid #888; background: #fff; color: #222; cursor: pointer;
    }}
    #bar button.active {{ background: #1f4e79; color: #fff; border-color: #1f4e79; }}
    #legend {{
      display: flex; align-items: center; gap: 4px; margin-left: 8px;
      font-size: 12px; color: #444;
    }}
    #legend .swatch {{
      width: 22px; height: 14px; border-radius: 3px; border: 1px solid #999;
    }}
    #plot {{ width: 100%; height: calc(100% - 52px); }}
  </style>
</head>
<body>
  <div id="bar">
    <h1>危険度 R の 3D 粒子</h1>
    <button id="btnRotate" type="button" title="カメラを自動回転">▶ 自動回転</button>
    <div id="legend">
      <span>R低</span>
      <span id="swatches"></span>
      <span>R高</span>
      <span style="margin-left:8px">連続色・α=0.35+0.65R　／　N=<span id="n"></span></span>
    </div>
  </div>
  <div id="plot"></div>
  <script>
    const DATA = {json.dumps(payload, ensure_ascii=False)};
    document.getElementById('n').textContent = DATA.meta.n;
    const sw = document.getElementById('swatches');
    DATA.legendColors.forEach(c => {{
      const el = document.createElement('span');
      el.className = 'swatch';
      // checker under translucent swatch
      el.style.background =
        'linear-gradient(' + c + ',' + c + '), repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%) 0 0 / 8px 8px';
      sw.appendChild(el);
    }});

    // One cloud: continuous rgba color (α in the color string).
    // scatter3d does not support opacity arrays.
    const traces = [{{
      type: 'scatter3d',
      mode: 'markers',
      name: 'R continuous',
      x: DATA.x,
      y: DATA.y,
      z: DATA.theta,
      text: DATA.hover,
      hoverinfo: 'text',
      marker: {{
        size: DATA.sizes,
        color: DATA.colors,
        opacity: 1,
        line: {{ width: 0 }},
      }},
      showlegend: false,
    }}];

    DATA.ex.names.forEach((name, i) => {{
      traces.push({{
        type: 'scatter3d',
        mode: 'markers+text',
        name: name,
        x: [DATA.ex.x[i]],
        y: [DATA.ex.y[i]],
        z: [DATA.ex.theta[i]],
        text: [name],
        textposition: 'top center',
        textfont: {{ size: 12, color: '#111' }},
        hovertext: [DATA.ex.text[i]],
        hoverinfo: 'text',
        marker: {{
          size: DATA.ex.sizes[i],
          color: DATA.ex.colors[i],
          opacity: 1,
          line: {{ width: 1, color: '#111' }},
        }},
        showlegend: false,
      }});
    }});

    const layout = {{
      paper_bgcolor: '#e8e8ec',
      plot_bgcolor: '#e8e8ec',
      margin: {{ l: 0, r: 0, t: 10, b: 0 }},
      showlegend: false,
      scene: {{
        bgcolor: '#f4f4f8',
        xaxis: {{ title: 'x (m) lateral', color: '#333', gridcolor: '#ccc', range: [-15, 15] }},
        yaxis: {{ title: 'y (m) forward', color: '#333', gridcolor: '#ccc', range: [-15, 15] }},
        zaxis: {{ title: 'θ (deg)', color: '#333', gridcolor: '#ccc', range: [-180, 180] }},
        aspectmode: 'manual',
        aspectratio: {{ x: 1.0, y: 1.0, z: 1.0 }},
        camera: {{
          eye: {{ x: 1.6, y: -1.4, z: 0.9 }},
          up: {{ x: 0, y: 0, z: 1 }},
        }},
      }},
    }};

    const config = {{
      responsive: true,
      displayModeBar: true,
      displaylogo: false,
      modeBarButtonsToRemove: ['toImage', 'lasso2d', 'select2d'],
    }};

    const plot = document.getElementById('plot');
    const btn = document.getElementById('btnRotate');
    let rotating = false;
    let raf = null;
    let programmatic = false;
    let angle = Math.atan2(-1.4, 1.6);
    let radius = Math.hypot(1.6, -1.4);
    let eyeZ = 0.9;
    const DEG_PER_SEC = 28;

    function syncEyeFromCamera() {{
      const cam = plot.layout && plot.layout.scene && plot.layout.scene.camera;
      if (!cam || !cam.eye) return;
      const ex = cam.eye.x, ey = cam.eye.y;
      const r = Math.hypot(ex, ey);
      if (r > 0.05) {{
        angle = Math.atan2(ey, ex);
        radius = r;
      }}
      if (typeof cam.eye.z === 'number') eyeZ = cam.eye.z;
    }}

    let lastTs = 0;
    function tick(ts) {{
      if (!rotating) return;
      if (!lastTs) lastTs = ts;
      const dt = Math.min(0.05, (ts - lastTs) / 1000);
      lastTs = ts;
      angle += (DEG_PER_SEC * Math.PI / 180) * dt;
      const eye = {{
        x: radius * Math.cos(angle),
        y: radius * Math.sin(angle),
        z: eyeZ,
      }};
      programmatic = true;
      Plotly.relayout(plot, {{ 'scene.camera.eye': eye }}).finally(() => {{
        programmatic = false;
        if (rotating) raf = requestAnimationFrame(tick);
      }});
    }}

    function setRotating(on) {{
      rotating = on;
      btn.classList.toggle('active', on);
      btn.textContent = on ? '⏸ 停止' : '▶ 自動回転';
      if (raf) {{
        cancelAnimationFrame(raf);
        raf = null;
      }}
      lastTs = 0;
      if (on) {{
        syncEyeFromCamera();
        raf = requestAnimationFrame(tick);
      }}
    }}

    btn.addEventListener('click', () => setRotating(!rotating));

    Plotly.newPlot(plot, traces, layout, config).then(() => {{
      plot.on('plotly_relayout', () => {{
        if (programmatic || !rotating) return;
        setRotating(false);
      }});
      plot.addEventListener('pointerdown', (e) => {{
        if (!rotating) return;
        if (e.target && e.target.closest && e.target.closest('.modebar')) return;
        if (e.target === btn) return;
        setRotating(false);
      }}, true);
    }});
  </script>
</body>
</html>
"""

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    OUT_HTML.write_text(html, encoding="utf-8")
    print(f"wrote {OUT_HTML}")
    print(f"points: {len(x)} continuous rgba")
    if open_browser:
        webbrowser.open(OUT_HTML.resolve().as_uri())


if __name__ == "__main__":
    main()
