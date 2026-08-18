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

- [ ] Unity Factory アセットをAsset Storeからインポート（HDRP専用・URP変換が必要）
- [ ] `URPMaterialFixer.cs` の実体を探す／なければ新規作成し、HDRP→URPマテリアル変換を実行
- [ ] インポート後、通路認識・レーン自動検出が機能しそうか目視確認
- [ ] Station A/B、ピックアップ／ドロップゾーン用の空間が確保できるか確認
- [ ] AGVの3Dモデルが同梱されているか確認、なければ調達
- [ ] `HRI-DroneExperiment`の実体（別リポジトリ？未マージ？）を確認 — 存在しないなら仕様書の「流用」前提が崩れるため、AGV関連ロジックはゼロから設計する

## Phase 1: シーン構築

- [ ] `FactoryLayout.cs` 新規作成（Station A/B座標、ワークベンチ、ピックアップ／ドロップゾーンの定数）
- [ ] `FactoryHRISetup.cs` 新規作成（`HRI > Setup Factory Scene` メニュー、Unity Factoryプレハブ配置）
- [ ] NavMesh（`FactoryHRI_NavMesh.asset`）をベイク
- [ ] `DroneVisualizationLayers.cs` / `UiEventSystemBootstrap.cs` の実体確認 → なければ新規実装

## Phase 2: AGVエージェント実装

- [ ] `AGVMissionPlan.cs` 新規作成（`AGVPhase` enum: MovingToPickup / DwellAtPickup / MovingToDrop / DwellAtDrop）
- [ ] `AGVAgent.cs` 新規作成（`IsStopped`, `currentSpeed`, `plannedPath` を公開）
- [ ] `AGVFleetOrchestrator.cs` 新規作成（0.1秒固定クロックで一括更新）
- [ ] `AGVSpawner.cs` 新規作成（シードから台数・速度を決定論的に算出、10〜20台）
- [ ] `AGVMotionSimulator.cs` 新規作成（地上移動＋停止、Y座標固定＝底面高さ0.1〜0.2m）
- [ ] `DroneRoutePlanner.cs`/`PassableZonePathfinder.cs`/`WarehousePassableAnalyzer.cs` 相当のA*経路計画を新規実装（FactoryLayout向け）
- [ ] `BoxPickupPool.cs` の実体確認 → なければ新規実装

## Phase 3: 危険度算出（動的基準線）

- [ ] `DynamicCrossingLineTracker.cs` 新規実装（仕様書4.1節のコードをそのまま導入、参加者にアタッチ）
- [ ] `VehicleRiskCalculator.cs` を単一`crossingLine`参照版に書き換え（現行の`crossingLines[]`配列版から移行）
  - 既存の`CrossingLine`ネストクラス・複数ライン処理ロジックを削除し、`AGVAgent`参照＋単一スコア/isVisibleに簡素化
- [ ] `PathDeviationTracker.cs`（新規）とのNavMesh経路計算の重複排除を検討（仕様書9節②、優先度は既存コード確認後）

## Phase 4: 視覚化（既存2スクリプトの検証・接続）

- [ ] `RiskToVisualMapper.cs` の中身を確認し、仕様書4.3節と一致しているか検証
- [ ] `PathRenderer.cs` の中身を確認し、4.4〜4.6節（三角先端形状・停止線・重なり時sortingOrder）を満たしているか検証・不足分を実装
- [ ] `risk.crossingLines[crossingLineIndex].currentScore/isVisible` への参照箇所を `risk.currentScore/isVisible` に置換（単一基準線化対応）

## Phase 5: 実験条件・計測

- [ ] `AGVPathVisualizer.cs` 新規作成（`DronePathVisualizer.cs`相当が存在しないため一から実装）— Baseline/No-AR/Proposedの3条件切替
- [ ] `ExperimentConditionManager.cs` を3条件版に改修（`BeginExperiment(mode)` / `BeginFullSession()`のAGV版）
- [ ] `MeasurementHub.cs` `TaskTimer.cs` `CollisionCounter.cs` `PathDeviationTracker.cs` 新規実装
  - CSV列: `SessionID, Timestamp, Condition, CaseIndex, CompletionTime_s, Collisions, PathDeviation_m`
- [ ] タグ・型名を `Drone` → `AGV` に統一（新規実装なので命名の一貫性を最初から担保）

## Phase 6: 未決定事項の解消（仕様書9節、優先度順）

- [ ] AGVのdwell時間（現在仮値2.0秒）を予備実験で決定
- [ ] ニアミス判定距離0.6mの妥当性を地上AGV基準で再検討
- [ ] 基準線の再計算間隔（1.0秒）・半幅（1.0m）を参加者の歩行速度・通路幅に対して調整
