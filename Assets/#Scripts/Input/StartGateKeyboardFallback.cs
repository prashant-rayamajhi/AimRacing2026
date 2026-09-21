using UnityEngine;

// キーボード検証時の開始アクセルを車両へ確実に渡すクラス
public class StartGateKeyboardFallback : MonoBehaviour
{
    // 開始待ちを管理するカウントダウン
    UI_StarCountDown m_startAction;
    // アクセル入力を受け取る車両
    VehicleController m_vehicle;
    // キーボード補助がアクセル値を設定したか判定する変数
    bool m_appliedKeyboardInput;
    // 開始待ちオブジェクトへキーボード補助を一度だけ追加する関数
    public static void Attach(UI_StarCountDown _startAction, VehicleController _vehicle)
    {
        if (_startAction == null || _vehicle == null)
        {
            return;
        }

        StartGateKeyboardFallback fallback = _startAction.GetComponent<StartGateKeyboardFallback>();
        if (fallback == null)
        {
            fallback = _startAction.gameObject.AddComponent<StartGateKeyboardFallback>();
        }

        fallback.m_startAction = _startAction;
        fallback.m_vehicle = _vehicle;
        fallback.enabled = true;
    }

    // 上矢印の保持状態を開始判定用アクセルへ反映する関数
    void Update()
    {
        if (m_startAction == null || m_vehicle == null || !m_startAction.enabled)
        {
            ReleaseKeyboardInput();
            return;
        }

        if (Input.GetKey(KeyCode.UpArrow))
        {
            m_vehicle.Accel = 1f;
            m_appliedKeyboardInput = true;
            return;
        }

        ReleaseKeyboardInput();
    }

    // 補助入力を使用した場合だけアクセルを元へ戻す関数
    void ReleaseKeyboardInput()
    {
        if (!m_appliedKeyboardInput || m_vehicle == null)
        {
            return;
        }

        m_vehicle.Accel = 0f;
        m_appliedKeyboardInput = false;
    }

    // 補助処理の停止時にキーボード入力を残さない関数
    void OnDisable()
    {
        ReleaseKeyboardInput();
    }
}
