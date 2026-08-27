# AimRacing2026

Unityで制作しているレースゲーム向けの車両挙動コードです。
GRヤリス Rally 4WDを題材に、TGS展示で幅広いプレイヤーが操作しやすい挙動を目指して調整しています。

## 実装内容

- GRヤリスを参考にしたエンジントルクとターボ応答
- ATとMTの変速処理
- ATクリープと停止後の再発進補助
- クラッチ接続と回転同期
- GR-FOURを意識した前後トルク配分
- LSDと空転時のトルク移動
- 4輪WheelColliderの駆動、制動、接地判定
- ABSとESC
- 速度感応ステアリングとAckermann操舵
- サスペンション、アンチロール、バンプストップ
- 加速と減速に応じた車体姿勢
- コース復帰後のエンジン、クラッチ、タイヤ状態の初期化
- 壁接触時の減速、跳ね返り、横転の抑制

## フォルダー構成

```text
Assets/#Scripts/CarScript
  VehicleController.cs
  WheelController2026.cs
  Engine.cs
  Clutch.cs
  Transmission.cs
  Differential.cs
  Brake.cs
  Steering.cs
  CarPhysics.cs
  ShowInInspectorAttribute.cs
  ShowInInspectorDrawer.cs

Assets/#Scripts/Editor
  AimRacingVehicleMergeInstaller.cs
```

## Unityへの導入

1. `Assets/#Scripts`をUnityプロジェクトの同じ場所へコピーします。
2. `Assets/!Scenes/Map.unity`を開きます。
3. 車両のルートGameObjectへ`VehicleController`と`Rigidbody`を設定します。
4. Unity上部メニューの`Tools/AimRacing/Apply Vehicle Behavior Merge`を実行します。
5. 作成された`WheelController`の子に4輪のWheelColliderがあることを確認します。
6. 表示タイヤを前右、前左、後右、後左の順で設定します。
7. シーンを保存してPlay Modeで動作を確認します。

## 基本Hierarchy

```text
###Vehicle###
└─WheelController
  ├─WheelCollider_FR
  ├─WheelCollider_FL
  ├─WheelCollider_RR
  └─WheelCollider_RL
```

`Engine`、`Clutch`、`Transmission`、`Differential`、`Brake`、`Steering`は、未設定の場合に`VehicleController`が車両ルートへ自動追加します。

## 代表設定

- 車両質量: 1200 kg
- タイヤ半径: 0.334 m
- サスペンション可動距離: 0.13 m
- ATクリープトルク: 180 N m
- ATクリープ目標速度: 15 km/h
- エンジンアイドル回転数: 900 rpm
- レッドゾーン: 7000 rpm
- 最大回転数: 7200 rpm
- 低速最大舵角: 35度
- 高速最大舵角: 10度

## 移行時の注意

- 既存プロジェクトへ上書きする場合は、コピー前にバックアップしてください。
- 既存の`.cs`を更新する場合は、移行先の`.meta`を残してください。
- 新規ファイルとして追加する場合は、`.cs`と`.meta`を一緒にコピーしてください。
- `Map.unity`全体を上書きせず、車両HierarchyとInspector設定だけを移行してください。
- 入力、FFB、カメラ、UI、ゲーム進行は移行先の設定を維持してください。

## 確認項目

- 4輪が接地して表示タイヤが回転する
- ATが自動変速する
- アクセルを離したATでクリープする
- ブレーキ後やコース復帰後に遅延なく再発進する
- 高速時に操舵角が適切に小さくなる
- ABSとESCが必要な場面で作動する
- 壁を浅く擦っても急停止や逆走をしない
- 衝突時に車体が過剰に跳ねたり横転したりしない

