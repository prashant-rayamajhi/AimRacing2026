<div align="center">

# AimRacing2026

### GR Yaris Rally 4WD Vehicle Behavior System

Unityの`WheelCollider`を使用し、リアルさと遊びやすさを両立させたレースゲーム向け車両挙動システムです。

![Unity](https://img.shields.io/badge/Engine-Unity-000000?logo=unity&logoColor=white)
![Language](https://img.shields.io/badge/Language-C%23-512BD4?logo=csharp&logoColor=white)
![Vehicle](https://img.shields.io/badge/Vehicle-GR%20Yaris%20Rally%204WD-EB0A1E)
![Status](https://img.shields.io/badge/Status-In%20Development-F5A623)

</div>

---

## Overview

`AimRacing2026`は、GRヤリス Rally 4WDを題材に制作した車両挙動コードです。
実車らしい駆動・荷重移動・タイヤ挙動を基礎としながら、TGS展示で子供から大人まで直感的に楽しめる操作感を目標に調整しています。

このリポジトリには、担当した車両挙動スクリプトと導入補助ツールのみを収録しています。

## Features

| 分野 | 実装内容 |
| --- | --- |
| エンジン | トルクカーブ、ターボ応答、アイドル回転、レブリミット |
| トランスミッション | AT・MT変速、ATクリープ、停止後の再発進補助 |
| クラッチ | 接続率の制御、エンジンと駆動系の回転同期 |
| 4WD・デフ | 前後トルク配分、LSD、空転時のトルク移動 |
| タイヤ | 4輪の駆動・制動・接地判定、表示タイヤの回転同期 |
| 車体制御 | ABS、ESC、速度感応ステアリング、Ackermann操舵 |
| サスペンション | スプリング、ダンパー、アンチロール、バンプストップ |
| 荷重移動 | 加速時のノーズアップ、制動時のノーズダウン |
| 安全処理 | コース復帰時の状態初期化、衝突時の跳ね返り・横転抑制 |

## System Flow

```text
Player Input
    ↓
Engine → Clutch → Transmission → Differential
                                      ↓
                              WheelController2026
                                      ↓
                         WheelCollider / Vehicle Body
                                      ↓
                   Steering / Brake / ABS / ESC / Suspension
```

`VehicleController`が各機構をまとめ、未設定の車両コンポーネントを自動で取得または追加します。

## Directory

```text
Assets/#Scripts/CarScript
├─VehicleController.cs
├─WheelController2026.cs
├─Engine.cs
├─Clutch.cs
├─Transmission.cs
├─Differential.cs
├─Brake.cs
├─Steering.cs
├─CarPhysics.cs
├─ShowInInspectorAttribute.cs
└─ShowInInspectorDrawer.cs

Assets/#Scripts/Editor
└─AimRacingVehicleMergeInstaller.cs
```

## Setup

1. `Assets/#Scripts`をUnityプロジェクトの同じ場所へコピーします。
2. `Assets/!Scenes/Map.unity`を開きます。
3. 車両ルートGameObjectへ`VehicleController`と`Rigidbody`を設定します。
4. Unity上部メニューの`Tools/AimRacing/Apply Vehicle Behavior Merge`を実行します。
5. 作成された`WheelController`の子に4輪の`WheelCollider`があることを確認します。
6. 表示タイヤを前右、前左、後右、後左の順で設定します。
7. シーンを保存し、Play Modeで確認します。

### Hierarchy

```text
###Vehicle###
└─WheelController
  ├─WheelCollider_FR
  ├─WheelCollider_FL
  ├─WheelCollider_RR
  └─WheelCollider_RL
```

`Engine`、`Clutch`、`Transmission`、`Differential`、`Brake`、`Steering`は、参照がない場合に`VehicleController`が車両ルートから自動取得します。必要なコンポーネントが存在しない場合は自動追加します。

## Main Parameters

| 項目 | 設定値 |
| --- | ---: |
| 車両質量 | 1200 kg |
| タイヤ半径 | 0.334 m |
| サスペンション可動距離 | 0.13 m |
| ATクリープトルク | 180 N m |
| ATクリープ目標速度 | 15 km/h |
| アイドル回転数 | 900 rpm |
| レッドゾーン | 7000 rpm |
| 最大回転数 | 7200 rpm |
| 低速最大舵角 | 35° |
| 高速最大舵角 | 10° |

実車値をそのまま入力するのではなく、`WheelCollider`の特性と展示時の操作性を考慮してゲーム向けに調整しています。

## Design Goals

- アクセルを踏んだ瞬間に加速感が伝わる
- ブレーキ操作に対して分かりやすく減速する
- 速度に応じた自然な操舵でコーナーを曲がれる
- 限界を超えたときだけ滑りを感じられる
- 壁へ浅く接触しても急停止や不自然な逆走をしない
- 衝突時の揺れや横転を抑え、幅広いプレイヤーが遊びやすい

## Validation Checklist

- [ ] 4輪が路面へ接地し、表示タイヤが速度に合わせて回転する
- [ ] ATが走行状態に合わせて自動変速する
- [ ] ATでアクセルを離したときにクリープ走行する
- [ ] ブレーキ後やコース復帰後に遅延なく再発進する
- [ ] 高速時に最大操舵角が小さくなり、急旋回を防止する
- [ ] 強い制動と横滑りに対してABS・ESCが作動する
- [ ] 加速・減速時の車体姿勢が過剰にならない
- [ ] 壁接触時に急停止・過剰な跳ね返り・横転が発生しない

## Migration Notes

既存プロジェクトへ移行する場合は、次の設定を維持してください。

- 既存の`.cs`を更新するときは、移行先にある`.meta`を残す
- 新規ファイルは`.cs`と`.meta`を一緒にコピーする
- `Map.unity`全体を上書きせず、車両HierarchyとInspector設定だけを移行する
- 入力、FFB、G923、カメラ、UI、ゲーム進行は移行先の設定を優先する

---

<div align="center">

**Realistic foundation. Enjoyable control. Clear feedback.**

</div>
