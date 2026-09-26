using UnityEngine;
using UnityEngine.InputSystem;

// 共通管理とメニューが同じ機器を使用してもメニューの入力が空にならないようにするクラス
public static class MenuInputDeviceSharing
{
    // 既存のメニュー割り当てに対応する機器を他の受付から奪わず共有する関数
    public static void Ensure(PlayerInput _input)
    {
        if (_input == null || !_input.isActiveAndEnabled || _input.actions == null)
        {
            return;
        }

        // 走行用の設定や機器名を変更せずメニューの既存マップだけを使う
        var map = _input.actions.FindActionMap("MainSystem", false);
        if (map == null)
        {
            return;
        }

        _input.neverAutoSwitchControlSchemes = true;
        // 全機器を他の受付が取得済みの場合はInputUser自体が作られないため機器フィルターだけ解除する
        if (_input.actions.devices.HasValue)
        {
            _input.actions.devices = null;
        }

        // 再接続後もメニュー以外のマップへ取り残されないようにする
        if (!_input.inputIsActive)
        {
            _input.ActivateInput();
        }

        if (_input.currentActionMap != map)
        {
            _input.SwitchCurrentActionMap(map.name);
        }
    }

    // 変化通知が来ない静止ペダルでも離されている状態だけを検出器に渡す関数
    public static void ObserveReleasedPedal(PlayerInput _input, AxisPressdOnce _detector, ref float _previousValue)
    {
        var pedal = _input != null && _input.actions != null ? _input.actions.FindAction("MainSystem/AccelPedal", false) : null;
        if (pedal == null || !pedal.enabled || pedal.controls.Count == 0)
        {
            return;
        }

        float value = Mathf.Clamp01((1f - pedal.ReadValue<float>()) * 0.5f);
        // 静止中に同じ通知とログを毎フレーム繰り返さない
        if (value == _previousValue)
        {
            return;
        }

        _previousValue = value;
        if (value <= 0.1f && !_detector.PressedOnce)
        {
            _detector.AxisCheck(value);
        }
    }
}
