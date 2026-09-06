#!/usr/bin/env python3
"""Interactive 3D risk particle viewer (drag to rotate, scroll to zoom).

Writes a self-contained HTML (Plotly CDN) — open in any browser.
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
    return f"rgba({int(r * 255)},{int(g * 255)},{int(b * 255)},{a:.3f})"


def main(open_browser: bool = True):
    x, y, theta, R = build_grid(dx=1.0, dy=1.0, dtheta=15.0)
    colors = [rgba_css(risk_color(r)) for r in R]

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
    ex_x, ex_y, ex_th, ex_R, ex_c, ex_t = [], [], [], [], [], []
    for name, (xi, yi, thi) in examples.items():
        Ri = score_pose(xi, yi, thi)
        ex_x.append(xi)
        ex_y.append(yi)
        ex_th.append(thi)
        ex_R.append(Ri)
        ex_c.append(rgba_css(risk_color(Ri)))
        ex_t.append(f"{name}<br>R={Ri:.2f}<br>(x,y,θ)=({xi:g},{yi:g},{thi:g})")

    hover = [
        f"R={ri:.2f}<br>x={xi:.0f} m<br>y={yi:.0f} m<br>θ={thi:.0f}°"
        for xi, yi, thi, ri in zip(x, y, theta, R)
    ]

    payload = {
        "x": x.tolist(),
        "y": y.tolist(),
        "theta": theta.tolist(),
        "R": R.tolist(),
        "colors": colors,
        "hover": hover,
        "ex": {
            "x": ex_x,
            "y": ex_y,
            "theta": ex_th,
            "R": ex_R,
            "colors": ex_c,
            "text": ex_t,
            "names": list(examples.keys()),
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
    html, body {{ margin: 0; height: 100%; background: #111; color: #eee;
      font-family: "Segoe UI", Meiryo, sans-serif; }}
    #bar {{ padding: 10px 16px; background: #1b1b1f; border-bottom: 1px solid #333;
      display: flex; flex-wrap: wrap; gap: 12px; align-items: baseline; }}
    #bar h1 {{ margin: 0; font-size: 16px; font-weight: 600; }}
    #bar span {{ color: #aaa; font-size: 13px; }}
    #plot {{ width: 100%; height: calc(100% - 48px); }}
  </style>
</head>
<body>
  <div id="bar">
    <h1>危険度 R の 3D 粒子（ドラッグで回転 / スクロールでズーム）</h1>
    <span>R = {W_P}·Rp + {W_C}·Rc + {W_D}·Rd　／　表示 d≤{D_MAX} m　／　θ=0 = +Y　／　N=<span id="n"></span></span>
  </div>
  <div id="plot"></div>
  <script>
    const DATA = {json.dumps(payload, ensure_ascii=False)};
    document.getElementById('n').textContent = DATA.meta.n;

    const cloud = {{
      type: 'scatter3d',
      mode: 'markers',
      name: 'grid',
      x: DATA.x,
      y: DATA.y,
      z: DATA.theta,
      text: DATA.hover,
      hoverinfo: 'text',
      marker: {{
        size: 2.5,
        color: DATA.colors,
        opacity: 0.85,
      }},
    }};

    const examples = {{
      type: 'scatter3d',
      mode: 'markers+text',
      name: 'examples',
      x: DATA.ex.x,
      y: DATA.ex.y,
      z: DATA.ex.theta,
      text: DATA.ex.names,
      textposition: 'top center',
      textfont: {{ size: 12, color: '#fff' }},
      hovertext: DATA.ex.text,
      hoverinfo: 'text',
      marker: {{
        size: 8,
        color: DATA.ex.colors,
        line: {{ width: 1, color: '#111' }},
      }},
    }};

    const layout = {{
      paper_bgcolor: '#111',
      plot_bgcolor: '#111',
      margin: {{ l: 0, r: 0, t: 10, b: 0 }},
      showlegend: false,
      scene: {{
        xaxis: {{ title: 'x (m) lateral', color: '#ccc', gridcolor: '#333', range: [-15, 15] }},
        yaxis: {{ title: 'y (m) forward', color: '#ccc', gridcolor: '#333', range: [0, 14] }},
        zaxis: {{ title: 'θ (deg)', color: '#ccc', gridcolor: '#333', range: [-180, 180] }},
        aspectmode: 'manual',
        aspectratio: {{ x: 1.2, y: 1.0, z: 1.0 }},
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

    Plotly.newPlot('plot', [cloud, examples], layout, config);
  </script>
</body>
</html>
"""

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    OUT_HTML.write_text(html, encoding="utf-8")
    print(f"wrote {OUT_HTML}")
    print(f"points: {len(x)}")
    if open_browser:
        webbrowser.open(OUT_HTML.resolve().as_uri())


if __name__ == "__main__":
    main()
