using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;

#endif
// 通常ブレーキと後輪専用のサイドブレーキを別々に計算するクラス
[System.Serializable]
public class Brake : MonoBehaviour
{
    // 通常ブレーキで前輪へ配分する割合
    [SerializeField, Range(0f, 1f)]
    float m_frontBrakeBias = 0.64f;
    // 既存の前後配分計算へ渡す通常ブレーキの最大トルク
    [SerializeField, Range(1000f, 4000f)]
    float m_maxBrakeTorque = 2600f;
    // 旧シーンとの互換性を保ち、現在の押下状態を保持する値
    [SerializeField, HideInInspector]
    bool m_onHandBrake;
    // 共通入力やG923を変更せずHキーのサイドブレーキを有効にする設定
    [Header("Keyboard Handbrake (Hold H)")]
    [SerializeField]
    bool m_keyboardHandbrakeEnabled = true;
    // 後輪一輪へ掛けるサイドブレーキの最大トルクで単位はNm
    [SerializeField, Range(0f, 4000f)]
    float m_handbrakeTorquePerWheel = 1800f;
    // 急な後輪ロックを和らげる一秒当たりの入力増加量
    [SerializeField, Min(0.1f)]
    float m_handbrakeApplyRate = 6f;
    // キーを離した後に制動を残さないための一秒当たりの入力減少量
    [SerializeField, Min(0.1f)]
    float m_handbrakeReleaseRate = 10f;
    // 通常ブレーキの前後配分に使うペダル入力
    float m_breakeInput;
    // 後輪制動の立ち上がりと解除を滑らかにする補間済み入力
    float m_handbrakeAmount;
    // 通常ブレーキ用のペダル入力を受け渡すプロパティ
    public float BrakeInput { get => m_breakeInput; set => m_breakeInput = Mathf.Clamp01(value); }
    // 制動中にクリープや発進補助が車体を押さないよう通知するプロパティ
    public bool HandbrakeActive => m_onHandBrake || m_handbrakeAmount > 0f;
    // 操作確認用に補間済みのサイドブレーキ入力を返すプロパティ
    public float HandbrakeAmount => m_handbrakeAmount;

    // 走行可能な間だけHキーを読み取り後輪制動を更新する関数
    public void UpdateHandbrake(bool _canDrive)
    {
        // イントロや非アクティブ画面では押下状態を持ち越さない
        if (!_canDrive || !isActiveAndEnabled || !Application.isFocused || Time.timeScale <= 0f)
        {
            ResetHandbrake();
            return;
        }

        AdvanceHandbrake(m_keyboardHandbrakeEnabled && ReadHandbrakeKey(), Time.fixedDeltaTime);
    }

    // 既存プロジェクトの入力方式に合わせHキーの押下中状態を取得する関数
    static bool ReadHandbrakeKey()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.H);
#elif ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.hKey.isPressed;
#else
        return false;
#endif
    }

    // 後輪制動の開始と解除を時間刻みに依存しない速さで補間する関数
    void AdvanceHandbrake(bool _pressed, float _deltaTime)
    {
        m_onHandBrake = _pressed;
        // 押下中はゆっくり制動を増やし、解除は素早く戻す
        float rate = _pressed ? m_handbrakeApplyRate : m_handbrakeReleaseRate;
        m_handbrakeAmount = Mathf.MoveTowards(m_handbrakeAmount, _pressed ? 1f : 0f, Mathf.Max(0f, rate * _deltaTime));
    }

    // 前輪制動を消さず通常ペダルの前後配分を返す関数
    public float GetBrakeTorque(bool _isFront)
    {
        return m_maxBrakeTorque * m_breakeInput * (_isFront ? m_frontBrakeBias : 1f - m_frontBrakeBias);
    }

    // 後輪だけへサイドブレーキの制動トルクを返す関数
    public float GetHandbrakeTorque(bool _isFront)
    {
        return _isFront ? 0f : Mathf.Max(0f, m_handbrakeTorquePerWheel) * m_handbrakeAmount;
    }

    // 復帰や操作停止時にサイドブレーキの制動を解除する関数
    public void ResetHandbrake()
    {
        m_onHandBrake = false;
        m_handbrakeAmount = 0f;
    }

    // 無効化された入力が後輪へ残らないよう解除する関数
    void OnDisable()
    {
        ResetHandbrake();
    }

    // 別画面へ移った際にキーを離した通知が届かなくても解除する関数
    void OnApplicationFocus(bool _focused)
    {
        if (!_focused)
        {
            ResetHandbrake();
        }
    }
}
