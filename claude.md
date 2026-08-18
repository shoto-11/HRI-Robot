# 工場内AGV実験 実装仕様書

対象：Unity（URP）+ Meta Quest 3、既存プロジェクト `HRI-DroneExperiment` の改修

本書は `HRI-DroneExperiment 仕様書`（既存実装の調査結果）、`eHMI危険度連動経路可視化_実装仕様書.md`（危険度可視化仕様）、`工場AGV実験_移行計画書.md`（移行方針）の3点を統合し、実装可能な単位まで具体化したもの。

---

## 1. 概要

工場内を走行する複数のAGV（10〜20台、荷物のピックアップ→ドロップを反復）が存在する環境で、作業員（実験参加者）がStation AからStation Bへ移動する際、AGVの走行経路をどう可視化するかで、移動時間・ニアミス回数・経路逸脱量にどのような影響が出るかを調べるVR実験。可視化方式は3条件（Baseline / No-AR / Proposed）を被験者内で比較する。

---

## 2. 環境構築

### 2.1 アセット導入

1. Unity Factory（Unity Technologies Japan, 無料, HDRP専用）をAsset Storeからインポート
2. 既存の`URPMaterialFixer.cs`をそのまま実行し、HDRPマテリアルをURP用に一括変換
3. インポート後、以下を目視確認する（未確認事項）：
  - 通路として認識できる空間配置になっているか（`WarehousePassableAnalyzer.cs`のレーン自動検出が機能する見込みか）
  - Station A/B、ピックアップ／ドロップゾーンとして使える空間が確保できるか
  - AGVらしい3Dモデルが同梱されているか（無ければ別途調達）

### 2.2 シーン構築

既存の`WarehouseHRISetup.cs`（エディタ拡張）を複製し、`FactoryHRISetup.cs`として以下を行うメニュー（`HRI > Setup Factory Scene`）を実装する。

- Unity Factoryのプレハブ群を配置してベースシーンを構築
- `WarehouseLayout.cs`相当の`FactoryLayout.cs`に、Station A/B座標、ワークベンチ、ピックアップ／ドロップゾーンの定数を設定
- NavMesh（`FactoryHRI_NavMesh.asset`）をベイクし、`PathDeviationTracker.cs`および後述のAGV経路計画で使用する

### 2.3 レイヤー・インフラ補助

`DroneVisualizationLayers.cs`、`UiEventSystemBootstrap.cs`は変更なしでそのまま流用する。

---

## 3. AGVエージェントの実装

### 3.1 スクリプト対応表（再掲）


| 既存（ドローン）                                                                          | 新規（AGV）                                     | 変更内容                      |
| --------------------------------------------------------------------------------- | ------------------------------------------- | ------------------------- |
| `DroneAgent.cs`                                                                   | `AGVAgent.cs`                               | 挙動ロジック踏襲、高度変化を地上移動＋停止に変更  |
| `DroneFlightOrchestrator.cs`                                                      | `AGVFleetOrchestrator.cs`                   | 変更なし（0.1秒固定クロック一括更新）      |
| `DroneSpawner.cs`                                                                 | `AGVSpawner.cs`                             | 変更なし（シードから台数・速度を決定論的に算出）  |
| `DroneRoutePlanner.cs`/`PassableZonePathfinder.cs`/`WarehousePassableAnalyzer.cs` | 同名で流用                                       | 解析対象をFactoryLayoutに向け直すのみ |
| `DroneMotionSimulator.cs`/`DroneMissionPlan.cs`                                   | `AGVMotionSimulator.cs`/`AGVMissionPlan.cs` | 高度成分を削除、停止フェーズを明示化        |
| `BoxPickupPool.cs`                                                                | 変更なし                                        | そのまま流用                    |


### 3.2 AGVMissionPlan：ミッションのフェーズ定義

```csharp
public enum AGVPhase
{
    MovingToPickup,
    DwellAtPickup,   // 旧: 降下・取得
    MovingToDrop,
    DwellAtDrop,     // 旧: 設置
}

[System.Serializable]
public class AGVMissionPlan
{
    public Vector3[] pathToPickup;   // A*で求めたウェイポイント列（Y座標は固定）
    public Vector3[] pathToDrop;
    public float dwellDurationAtPickup = 2.0f; // 秒、要調整（6.4項参照）
    public float dwellDurationAtDrop = 2.0f;
}

```

Y座標はロボット底面高さ（例：0.1〜0.2m）に固定する。既存の`DroneMotionSimulator`が持っていた「巡航高度1.40〜2.80mのジッタ」ロジックは削除する。

### 3.3 AGVAgent：停止状態の公開

`eHMI危険度連動経路可視化_実装仕様書.md` 4.6節の`IsStopped`判定と接続するため、`AGVAgent`は現在のフェーズが`DwellAtPickup`/`DwellAtDrop`かどうかを公開する。

```csharp
public class AGVAgent : MonoBehaviour
{
    public AGVPhase currentPhase;
    public bool IsStopped => currentPhase == AGVPhase.DwellAtPickup || currentPhase == AGVPhase.DwellAtDrop;
    public float currentSpeed; // VehicleRiskCalculatorが参照
    public Vector3[] plannedPath; // 現在のフェーズに応じた残り経路（VehicleRiskCalculatorに渡す）
}

```

---

## 4. 危険度算出：動的な基準線への対応

### 4.1 DynamicCrossingLineTracker（新規・完全実装）

参加者（プレイヤー）にアタッチする。NavMesh最短経路上で現在位置に最も近い区間を求め、その方向を「進行方向」として基準線を毎フレーム更新する。

```csharp
using UnityEngine;
using UnityEngine.AI;

public class DynamicCrossingLineTracker : MonoBehaviour
{
    public Transform lineStart, lineEnd; // 空のTransformを2つ子として用意
    public float lineHalfWidth = 1.0f;
    public Vector3 destination; // Station Bの座標

    NavMeshPath cachedPath;
    float pathRecalcInterval = 1.0f; // 経路の再計算頻度（秒）
    float pathRecalcTimer;

    void Start()
    {
        cachedPath = new NavMeshPath();
        RecalculatePath();
    }

    void Update()
    {
        pathRecalcTimer += Time.deltaTime;
        if (pathRecalcTimer >= pathRecalcInterval)
        {
            RecalculatePath();
            pathRecalcTimer = 0f;
        }

        Vector3 forward = GetCurrentPathTangent();
        Vector3 perpendicular = Vector3.Cross(Vector3.up, forward).normalized;

        Vector3 center = transform.position;
        lineStart.position = center + perpendicular * lineHalfWidth;
        lineEnd.position = center - perpendicular * lineHalfWidth;
    }

    void RecalculatePath()
    {
        NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, cachedPath);
    }

    Vector3 GetCurrentPathTangent()
    {
        Vector3[] corners = cachedPath.corners;
        if (corners.Length < 2) return transform.forward;

        // 現在位置に最も近い区間（corners[i] -> corners[i+1]）を探す
        int bestIdx = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            float d = DistancePointToSegment(transform.position, corners[i], corners[i + 1]);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }

        return (corners[bestIdx + 1] - corners[bestIdx]).normalized;
    }

    float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector3 proj = a + t * ab;
        return Vector3.Distance(p, proj);
    }
}

```

**注記**：`PathDeviationTracker.cs`が既に同種のNavMesh経路計算を保持している場合、`cachedPath`を共有し二重計算を避けることが望ましい（既存コードの詳細確認後に統合を検討）。

### 4.2 VehicleRiskCalculator（動的基準線版）

`eHMI危険度連動経路可視化_実装仕様書.md` 3.5節のコードから、`crossingLines`配列（複数の横断歩道）を、単一の動的基準線への参照に変更する。

```csharp
using UnityEngine;

public class VehicleRiskCalculator : MonoBehaviour
{
    public DynamicCrossingLineTracker crossingLine; // 参加者にアタッチされたトラッカーへの参照
    public AGVAgent agv;

    [Range(0f, 1f)] public float currentScore;
    public bool isVisible;

    const float TTC_MAX = 4f;
    const float DISPLAY_DISTANCE_MAX = 25f;
    const float GAMMA = 0.6f;
    const float SCORE_FLOOR = 0.08f;

    void Update()
    {
        float distToPedestrian = Vector3.Distance(transform.position, crossingLine.transform.position);

        float ttc = ComputeTTCAlongPath(crossingLine.lineStart.position, crossingLine.lineEnd.position);

        bool ttcVisible = !float.IsInfinity(ttc) && ttc <= TTC_MAX;
        bool distanceVisible = distToPedestrian <= DISPLAY_DISTANCE_MAX;
        isVisible = ttcVisible || distanceVisible;

        if (!isVisible)
        {
            currentScore = 0f;
            return;
        }

        float r = float.IsInfinity(ttc)
            ? 0f
            : Mathf.Pow(Mathf.Clamp01(1f - ttc / TTC_MAX), GAMMA);

        currentScore = Mathf.Max(SCORE_FLOOR, r);
    }

    float ComputeTTCAlongPath(Vector3 lineStart, Vector3 lineEnd)
    {
        int startIdx = FindClosestWaypointIndex(transform.position, agv.plannedPath);
        float accumulatedDist = 0f;

        for (int i = startIdx; i < agv.plannedPath.Length - 1; i++)
        {
            Vector3 a = (i == startIdx) ? transform.position : agv.plannedPath[i];
            Vector3 b = agv.plannedPath[i + 1];

            if (SegmentsIntersect(a, b, lineStart, lineEnd, out Vector3 hit))
            {
                accumulatedDist += Vector3.Distance(a, hit);
                return accumulatedDist / Mathf.Max(agv.currentSpeed, 0.1f);
            }

            accumulatedDist += Vector3.Distance(a, b);
            if (accumulatedDist / Mathf.Max(agv.currentSpeed, 0.1f) > TTC_MAX) break;
        }
        return Mathf.Infinity;
    }

    bool SegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 hit)
    {
        hit = Vector3.zero;
        Vector2 a = new Vector2(p1.x, p1.z), b = new Vector2(p2.x, p2.z);
        Vector2 c = new Vector2(p3.x, p3.z), d = new Vector2(p4.x, p4.z);

        Vector2 r = b - a, s = d - c;
        float denom = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(denom) < 0.0001f) return false;

        float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denom;
        float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denom;

        if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
        {
            Vector2 p = a + t * r;
            hit = new Vector3(p.x, p1.y, p.y);
            return true;
        }
        return false;
    }

    int FindClosestWaypointIndex(Vector3 pos, Vector3[] path)
    {
        int best = 0; float bestDist = float.MaxValue;
        for (int i = 0; i < path.Length; i++)
        {
            float d = Vector3.Distance(pos, path[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }
}

```

複数の横断歩道を扱っていた`crossingLines[]`が単一の`crossingLine`参照になった点が、前回の道路実験仕様からの主な変更点。

---

## 5. 視覚化（変更なし・流用）

`RiskToVisualMapper.cs`、`PathRenderer.cs`（走行中の三角形状先端＋停止時の停止線）は`eHMI危険度連動経路可視化_実装仕様書.md` 4.3〜4.6節のコードをそのまま流用する。参照先のみ`risk.crossingLines[crossingLineIndex].currentScore/isVisible`から`risk.currentScore/isVisible`（単一基準線化に伴う簡略化）に置き換える。

---

## 6. 実験条件（3条件、既存構造を流用）

`DronePathVisualizer.cs`をベースに、条件ごとの描画ロジックを以下のように定義する。


| 条件         | AGVごとのコンポーネント動作                                                                 |
| ---------- | ------------------------------------------------------------------------------- |
| ① Baseline | `PathRenderer`常時有効。色は単色固定、不透明度は常に1.0（`VehicleRiskCalculator`は無効化 or スコア計算をスキップ） |
| ② No-AR    | 全AGVの`PathRenderer`を無効化                                                         |
| ③ Proposed | `VehicleRiskCalculator` + `RiskToVisualMapper` + `PathRenderer`をフル稼働（本仕様書4〜5節）  |


`ExperimentManager.cs`の条件切替ロジック（`BeginExperiment(mode)` / `BeginFullSession()`）は変更なし。条件番号に応じて上記のコンポーネント有効/無効を切り替える処理のみ`DronePathVisualizer.cs`改め`AGVPathVisualizer.cs`に実装する。

---

## 7. 計測・データ出力（変更なし）

`MeasurementHub.cs`, `TaskTimer.cs`, `CollisionCounter.cs`, `PathDeviationTracker.cs`はロジック変更なし。参照するオブジェクトの型・タグ名を`Drone`→`AGV`に置換するのみ。CSV列構成（`SessionID, Timestamp, Condition, CaseIndex, CompletionTime_s, Collisions, PathDeviation_m`）も変更なし。

---

## 8. パラメータ一覧


| パラメータ                 | 値                       | 出典         |
| --------------------- | ----------------------- | ---------- |
| $TTC_{max}$           | 4秒                      | eHMI仕様書    |
| $D_{display}$         | 25m                     | eHMI仕様書    |
| $\gamma$              | 0.6                     | eHMI仕様書    |
| $R_{floor}$           | 0.08                    | eHMI仕様書    |
| 不透明度量子化               | 4段階（0.08/0.35/0.65/1.0） | eHMI仕様書    |
| 色相                    | 赤(0°)〜薄青緑(180°)         | eHMI仕様書    |
| 走行中の根元の太さ             | 0.3m                    | eHMI仕様書    |
| 停止線の半幅                | 1.5m                    | eHMI仕様書    |
| 停止判定速度しきい値 $V_{stop}$ | 0.3 m/s                 | eHMI仕様書    |
| 基準線の再計算間隔             | 1.0秒                    | 本書新規（4.1節） |
| 基準線の半幅                | 1.0m                    | 本書新規（4.1節） |
| ケース数/条件               | 10（既存シード列を流用）           | ドローン実験仕様書  |
| ニアミス判定距離              | 0.6m（要再検討、9節）           | ドローン実験仕様書  |


---

## 9. 未決定・要検討事項（優先度順）

1. **Unity Factoryアセットの内部構成確認**：インポート後、通路検出・Station配置・AGVモデルの有無を確認する（着手すべき最初のステップ）
2. `DynamicCrossingLineTracker`**と**`PathDeviationTracker.cs`**のNavMesh経路計算の統合**：二重計算を避けるため、既存コードの詳細確認後にリファクタリングを検討
3. **AGVの停止（dwell）時間の設定値**：2.0秒は仮値。ドローン実験での「降下・取得→上昇」の所要時間を参考に調整するか、予備実験で決定する
4. **ニアミス判定距離0.6mの妥当性**：飛行するドローンと地上走行するAGVでは接触判定の意味合いが異なるため、地上AGV・作業員間の妥当な距離を再検討する
5. **基準線の再計算間隔（1.0秒）・半幅（1.0m）**：予備実験で参加者の歩行速度・工場の通路幅に対して妥当か確認・調整する

