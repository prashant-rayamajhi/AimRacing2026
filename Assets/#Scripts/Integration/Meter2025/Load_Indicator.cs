using UnityEngine;
using UnityEngine.UI;

// 現在の四輪接地荷重をメーターのタイヤ色へ反映するクラス
public class Load_Indicator : MonoBehaviour
{
    // 旧シーンの右前輪参照から同じ車両の新しい車輪制御を特定する変数
    [SerializeField, HideInInspector]
    private WheelController2024 m_wheelFR;
    // 旧シーンの左前輪参照を移行時の予備として保持する変数
    [SerializeField, HideInInspector]
    private WheelController2024 m_wheelFL;
    // 旧シーンの右後輪参照を移行時の予備として保持する変数
    [SerializeField, HideInInspector]
    private WheelController2024 m_wheelRR;
    // 旧シーンの左後輪参照を移行時の予備として保持する変数
    [SerializeField, HideInInspector]
    private WheelController2024 m_wheelRL;
    // 四輪の接地荷重を読み取る現在の車輪制御
    [SerializeField]
    private WheelController2026 m_wheelController;
    // 右前輪の荷重を色で示す画像
    [SerializeField]
    private Image m_FR;
    // 左前輪の荷重を色で示す画像
    [SerializeField]
    private Image m_FL;
    // 右後輪の荷重を色で示す画像
    [SerializeField]
    private Image m_RR;
    // 左後輪の荷重を色で示す画像
    [SerializeField]
    private Image m_RL;
    // 前輪表示を赤へ変える接地荷重のしきい値で単位はN
    [SerializeField, Min(0f)]
    private float m_frontLoadThreshold = 2800f;
    // 後輪表示を赤へ変える接地荷重のしきい値で単位はN
    [SerializeField, Min(0f)]
    private float m_rearLoadThreshold = 4200f;
    // 車両生成待ちの間に参照検索を毎フレーム行わないための時刻
    private float m_nextResolveTime;
    // 車両が後から生成される場合に再検索する間隔
    private const float ResolveIntervalSeconds = 1f;
    // 既存の車輪参照を使って表示対象の新しい車輪制御を取得する関数
    private void OnEnable()
    {
        ResolveReferences();
        RefreshDisplay();
    }

    // 車輪の接地荷重に合わせて四つの画像色を更新する関数
    private void Update()
    {
        RefreshDisplay();
    }

    // 荷重表示を更新し、空中や参照切れでは白色へ戻す関数
    public void RefreshDisplay()
    {
        // 初期化順で車両が見つからなかった場合だけ参照を再取得する
        if (m_wheelController == null && Time.unscaledTime >= m_nextResolveTime)
        {
            ResolveReferences();
        }

        ApplyLoad(m_FR, ReadLoad(0), m_frontLoadThreshold);
        ApplyLoad(m_FL, ReadLoad(1), m_frontLoadThreshold);
        ApplyLoad(m_RR, ReadLoad(2), m_rearLoadThreshold);
        ApplyLoad(m_RL, ReadLoad(3), m_rearLoadThreshold);
    }

    // 停止済みの旧車輪ではなく同じ車両にある新しい車輪制御へ接続する関数
    private void ResolveReferences()
    {
        m_nextResolveTime = Time.unscaledTime + ResolveIntervalSeconds;
        if (m_wheelController != null)
        {
            return;
        }

        // 旧参照は所有車両の特定だけに使い、旧物理を再有効化しない
        WheelController2024 legacy = m_wheelFR != null ? m_wheelFR : m_wheelFL != null ? m_wheelFL : m_wheelRR != null ? m_wheelRR : m_wheelRL;
        VehicleController vehicle = legacy != null ? legacy.GetComponentInParent<VehicleController>() : GetComponentInParent<VehicleController>();
        if (vehicle != null)
        {
            m_wheelController = vehicle.GetComponentInChildren<WheelController2026>(true);
            return;
        }

        // 旧参照のないシーンでは車両が一台の場合だけ自動取得し、別車両への誤接続を防ぐ
        WheelController2026[] candidates = FindObjectsByType<WheelController2026>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (candidates.Length == 1)
        {
            m_wheelController = candidates[0];
        }
    }

    // 表示対象の車輪がない場合は荷重なしとして返す関数
    private float ReadLoad(int index)
    {
        return m_wheelController != null ? m_wheelController.GetContactLoad(index) : 0f;
    }

    // 荷重が従来のしきい値を超えた車輪だけ赤色にする関数
    private static void ApplyLoad(Image target, float load, float threshold)
    {
        if (target == null)
        {
            return;
        }

        target.color = load > threshold ? Color.red : Color.white;
    }
}
