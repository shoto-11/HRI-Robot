# 工場内AGV実験 実装TODO

`claude.md`（工場内AGV実験 実装仕様書）に基づくTODOリスト。作成日: 2026-08-18

## 前提の注意

このリポジトリには「既存プロジェクト `HRI-DroneExperiment`」の実体が存在しない。
あるのは前スペック（歩行者横断歩道版eHMI）由来の4本のみ：

- `Assets/Scripts/Runtime/VehicleRiskCalculator.cs`（`crossingLines[]`の複数横断ライン版 — 新仕様の単一`crossingLine`版への書き換えが必要）
- `Assets/Scripts/Runtime/PathRenderer.cs`（流用予定だが中身の仕様適合を要確認）
- `Assets/Scripts/Runtime/RiskToVisualMapper.cs`（流用予定だが中身の仕様適合を要確認）
- `Assets/Scripts/Runtime/ExperimentConditionManager.cs`（3条件版への改修対象）

新仕様書が「変更なし・流用」と書いている以下のスクリプトはこのリポジトリのどこにも存在しない。
「流用」ではなく「新規実装」として扱う：

`DroneAgent.cs` `DroneFlightOrchestrator.cs` `DroneSpawner.cs` `DroneRoutePlanner.cs`
`PassableZonePathfinder.cs` `WarehousePassableAnalyzer.cs` `DroneMotionSimulator.cs`
`DroneMissionPlan.cs` `BoxPickupPool.cs` `WarehouseHRISetup.cs` `WarehouseLayout.cs`
`DroneVisualizationLayers.cs` `UiEventSystemBootstrap.cs` `URPMaterialFixer.cs`
`MeasurementHub.cs` `TaskTimer.cs` `CollisionCounter.cs` `PathDeviationTracker.cs`

---

## Phase 0: 前提確認（最優先・仕様書9節①）

- [ ] Unity Factory アセットを Asset Store からインポート（HDRP専用・URP変換が必要）— 未導入。暫定として `FactoryHRISetup` がホワイトボックス工場を生成する
- [x] `URPMaterialFixer.cs` を新規作成（`HRI > Fix URP Materials`）
- [ ] インポート後、通路認識・レーン自動検出が機能しそうか目視確認
- [x] Station A/B、ピックアップ／ドロップゾーン用の空間を `FactoryLayout` に確保
- [x] AGV 3Dモデル未同梱のため、手続き生成の簡易ボディで代替（プレハブ差し替え可）
- [x] `HRI-DroneExperiment` は別リポジトリとして存在。本リポジトリでは AGV 向けに新規実装

## Phase 1: シーン構築

- [x] `FactoryLayout.cs` 新規作成
- [x] `FactoryHRISetup.cs` 新規作成（`HRI > Setup Factory Scene`）
- [ ] NavMesh（`FactoryHRI_NavMesh.asset`）をベイク — メニュー実行時に自動ベイク
- [x] `VisualizationLayers.cs` / `UiEventSystemBootstrap.cs` 新規実装

## Phase 2: AGVエージェント実装

- [x] `AGVMissionPlan.cs`
- [x] `AGVAgent.cs`（`IsStopped`, `currentSpeed`, `plannedPath`）
- [x] `AGVFleetOrchestrator.cs`（0.1秒固定クロック）
- [x] `AGVSpawner.cs`（シードから 10〜20 台・速度を決定論的に算出）
- [x] `AGVMotionSimulator.cs`（地上移動、Y=0.15m 固定）
- [x] `AGVRoutePlanner.cs`（NavMesh 経路、FactoryLayout 向け）
- [x] `BoxPickupPool.cs` / `CargoZoneMarker.cs`

## Phase 3: 危険度算出（動的基準線）

- [x] `DynamicCrossingLineTracker.cs`
- [x] `VehicleRiskCalculator.cs` を単一 `crossingLine` 参照版に書き換え
- [x] `PathDeviationTracker` は `DynamicCrossingLineTracker.CachedPath` を優先利用

## Phase 4: 視覚化

- [x] `RiskToVisualMapper.cs` を仕様 4.3 に合わせて実装
- [x] `PathRenderer.cs`（三角先端・停止線・sortingOrder）
- [x] `risk.currentScore` / `isVisible` の単一基準線参照

## Phase 5: 実験条件・計測

- [x] `AGVPathVisualizer.cs`（Baseline / No-AR / Proposed）
- [x] `ExperimentManager.cs`（`BeginExperiment` / `BeginFullSession`）
- [x] `MeasurementHub.cs` `TaskTimer.cs` `CollisionCounter.cs` `PathDeviationTracker.cs`
- [x] タグ・型名を `AGV` に統一

## Phase 6: 未決定事項の解消（仕様書9節、優先度順）

- [ ] AGVのdwell時間（現在仮値2.0秒）を予備実験で決定
- [ ] ニアミス判定距離0.6mの妥当性を地上AGV基準で再検討
- [ ] 基準線の再計算間隔（1.0秒）・半幅（1.0m）を参加者の歩行速度・通路幅に対して調整
