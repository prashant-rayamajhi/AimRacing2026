# AimRacing2026

### GR Yaris 4WD — 車両挙動の改善

UnityのWheelColliderを使用した、レースゲーム向けの車両挙動コードです。実車らしさと展示での遊びやすさを目標に、トルク・サスペンションを起点として、加減速、変速、旋回、衝突後の復帰、走行音との連携を改善しています。

学校で継承・共同制作したコードを基にした改善資料です。共有ファイルには既存の基盤コードも含まれ、各ファイル全体を一人で新規制作したという意味ではありません。

## 主な収録内容

| 分野 | 主なスクリプト |
| --- | --- |
| エンジントルク・過給・回転制限 | Engine.cs |
| クラッチ・AT／MT・発進補助 | Clutch.cs、Transmission.cs、VehicleController.ManualLever.cs、VehicleController.Launch.cs |
| 四輪駆動・前後トルク配分 | Differential.cs、Differential.GRFour.cs |
| 接地・サスペンション・タイヤ | WheelController2026.cs、WheelController2026.PoweredGrip.cs |
| 操舵・制動・横滑り制御 | Steering.cs、Brake.cs、VehicleController.ESC.cs |
| 衝突時の補正・復帰 | VehicleController.Collision.cs |
| 入力・走行音 | VehicleUnityEvent.cs、DriveSound.cs、TireSound.cs、WindSound.cs |

メーター、FFB、カメラ、画面遷移などへの関連修正も含みます。RacingState.csは提出用にデバッグ操作の呼び出し元を整理したファイルで、主担当の制作実績とは区別しています。

## 構成

```text
Assets/#Scripts/
├─ CarScript/       車両挙動と共通処理
├─ Input/           操作入力
├─ Sound/2024/      車両サウンド
├─ Integration/    メーター・FFB・コース復帰との連携
├─ Camera/         カメラ切り替え
├─ GameManager/    ゲーム進行との連携
├─ UI/             逆走表示
├─ UI_Others/      カウントダウン
└─ Others/         関連修正
```

C#スクリプト50件を収録しています。整理済みの現行ファイル47件と、従来からの共通スクリプト3件です。旧WheelController2024と空のFrontColliderは互換・旧実装の参照として残しており、現在の車輪制御はWheelController2026を中心に読んでください。

## 利用環境と検証

- 元プロジェクトの環境：Unity 6000.3.11f1。
- Unity Input System、Cinemachine、Splines、TextMeshPro、FMOD、Logitech SDK、WIZMO、およびプロジェクト固有のゲーム進行・UIクラスなどに依存します。
- ソース閲覧用のリポジトリです。シーン、モデル、音源、設定アセット、外部ライブラリ、metaファイルは含まず、このリポジトリ単独ではゲームを実行できません。
- 2026年9月21日の整理時は、元プロジェクトの依存関係を使ったC#コンパイル診断でエラー0件でした。このリポジトリ単独のビルド成功や、実プレイ・音・G923・椅子の実機検証を示すものではありません。

コメントアウト済みコード、個人名を含む履歴コメント、不要な確認ログ・デバッグ操作を整理しています。ゲーム向けの調整値を含むため、すべての値を実車の公称値として扱っていません。

既存Unityプロジェクトへ再利用する場合は、移行先のmetaを保持し、関連クラス・Inspector値・シーン参照を照合してください。旧版用の自動移行ツールは収録していません。
