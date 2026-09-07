#!/usr/bin/env python3
"""Interactive 3D risk particle viewer (drag to rotate, scroll to zoom).

Writes a self-contained HTML (Plotly CDN) — open in any browser.
Per-point transparency: Plotly scatter3d rejects opacity arrays, so points are
split into α-bins (each trace has a scalar opacity = RiskToVisualMapper α).

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

# RiskToVisualMapper α bins (scalar opacity per trace — required for scatter3d)
ALPHA_BINS = [
    (0.00, 0.25, 0.35 + 0.65 * 0.125),  # low R
    (0.25, 0.50, 0.35 + 0.65 * 0.375),
    (0.50, 0.75, 0.35 + 0.65 * 0.625),
    (0.75, 1.01, 0.35 + 0.65 * 0.875),  # high R → nearly opaque
]


def rgb_css(rgba) -> str:
    r, g, b, _a = rgba
    return f"rgb({int(r * 255)},{int(g * 255)},{int(b * 255)})"


def main(open_browser: bool = True):
    x, y, theta, R = build_grid(dx=1.0, dy=1.0, dtheta=15.0)
    x = np.asarray(x)
    y = np.asarray(y)
    theta = np.asarray(theta)
    R = np.asarray(R)

    bins = []
    for r0, r1, alpha in ALPHA_BINS:
        m = (R >= r0) & (R < r1)
        if not np.any(m):
            continue
        xi, yi, thi, ri = x[m], y[m], theta[m], R[m]
        colors = [rgb_css(risk_color(float(r))) for r in ri]
        sizes = (2.0 + 4.0 * ri).tolist()
        hover = [
            f"R={rj:.2f}<br>α≈{alpha:.2f}<br>x={xj:.0f} m<br>y={yj:.0f} m<br>θ={tj:.0f}°"
            for xj, yj, tj, rj in zip(xi, yi, thi, ri)
        ]
        bins.append(
            {
                "name": f"R {r0:.2f}–{min(r1, 1):.2f} (α≈{alpha:.2f})",
                "x": xi.tolist(),
                "y": yi.tolist(),
                "theta": thi.tolist(),
                "colors": colors,
                "sizes": sizes,
                "opacity": float(alpha),
                "hover": hover,
            }
        )

    examples = dict(
        A=(0.0, 3.0, 180.0),    # toward worker
        B=(3.0, 2.5, -90.0),    # cross in front, pure lateral
        C=(1.0, 0.3, 0.0),      # nearby
    )
    ex_x, ex_y, ex_th, ex_c, ex_s, ex_a, ex_t, ex_names = [], [], [], [], [], [], [], []
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
        col = risk_color(Ri)
        alpha = float(col[3])
        ex_x.append(xi)
        ex_y.append(yi)
        ex_th.append(thi)
        ex_c.append(rgb_css(col))
        ex_s.append(8.0 + 4.0 * Ri)
        ex_a.append(alpha)
        ex_names.append(name)
        ex_t.append(
            f"{name}<br>R={Ri:.2f}<br>α={alpha:.2f}<br>(x,y,θ)=({xi:g},{yi:g},{thi:g})"
        )

    payload = {
        "bins": bins,
        "ex": {
            "x": ex_x,
            "y": ex_y,
            "theta": ex_th,
            "colors": ex_c,
            "sizes": ex_s,
            "alphas": ex_a,
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
    #bar button:hover {{ filter: brightness(0.97); }}
    #plot {{ width: 100%; height: calc(100% - 52px); }}
  </style>
</head>
<body>
  <div id="bar">
    <h1>危険度 R の 3D 粒子</h1>
    <button id="btnRotate" type="button" title="カメラを自動回転">▶ 自動回転</button>
    <span>R=0.55·Rp+0.30·Rc+0.15·Rd（相対PTTC）　／　ドラッグで回転・ズーム　／　α=0.35+0.65R　／　N=<span id="n"></span></span>
  </div>
  <div id="plot"></div>
  <script>
    const DATA = {json.dumps(payload, ensure_ascii=False)};
    document.getElementById('n').textContent = DATA.meta.n;

    // scatter3d: opacity must be scalar per trace → α-binned traces
    const traces = DATA.bins.map(b => ({{
      type: 'scatter3d',
      mode: 'markers',
      name: b.name,
      x: b.x,
      y: b.y,
      z: b.theta,
      text: b.hover,
      hoverinfo: 'text',
      marker: {{
        size: b.sizes,
        color: b.colors,
        opacity: b.opacity,
        line: {{ width: 0 }},
      }},
    }}));

    // examples: one point per trace so each keeps its own α
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
          opacity: DATA.ex.alphas[i],
          line: {{ width: 1, color: '#111' }},
        }},
        showlegend: false,
      }});
    }});

    const layout = {{
      paper_bgcolor: '#e8e8ec',
      plot_bgcolor: '#e8e8ec',
      margin: {{ l: 0, r: 0, t: 10, b: 0 }},
      showlegend: true,
      legend: {{
        bgcolor: 'rgba(255,255,255,0.85)',
        bordercolor: '#ccc',
        borderwidth: 1,
        font: {{ size: 11 }},
        x: 0, y: 1,
      }},
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
    const DEG_PER_SEC = 28; // continuous orbit speed

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
      // 自分の自動回転による relayout は無視。ユーザー操作だけ停止。
      plot.on('plotly_relayout', () => {{
        if (programmatic || !rotating) return;
        setRotating(false);
      }});
      // WebGL drag 開始でも確実に止める
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
    print(f"points: {len(x)} in {len(bins)} opacity bins")
    if open_browser:
        webbrowser.open(OUT_HTML.resolve().as_uri())


if __name__ == "__main__":
    main()
