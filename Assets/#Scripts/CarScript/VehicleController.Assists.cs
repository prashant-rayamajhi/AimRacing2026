using UnityEngine;

// 車両の駆動モードとは別に展示用補助の強さと加速上限を管理するクラス
public partial class VehicleController
{
    // エンジン由来の駆動力に許可する加速度上限で、実際の加速は摩擦と抵抗にも左右される
    [Header("Assist Balance")]
    [SerializeField, Min(0.1f)]
    float m_maximumDriveAcceleration = 9.4f;
    // GR-FOURの配分を変えずにESCとコーナー補助を弱めるための共通倍率
    [SerializeField, Range(0f, 1f)]
    float m_drivingAssistStrength = 1f;
    // サスペンションの自然な動きを残し、追加のノーズ演出だけを弱める倍率
    [SerializeField, Range(0f, 1f)]
    float m_additionalPitchScale = 0.5f;
    // 同じ物理更新で複数の壁補助が使える残りの速度変化量
    float m_wallAssistRemainingDeltaSpeed;
    // 二つの旋回補助を掛け合わせず、より強い制限を一度だけ適用する関数
    static float CombineDriveLimits(float cornerRatio, float escIntervention, float maximumESCReduction)
    {
        // 両方が最大でも意図した制限量以上に駆動力を落とさない
        float escRatio = 1f - Mathf.Clamp01(escIntervention) * Mathf.Clamp01(maximumESCReduction);
        return Mathf.Min(Mathf.Clamp01(cornerRatio), escRatio);
    }

    // 接線方向を変えず、接触面から離れる法線速度だけを減らす関数
    static Vector3 LimitSurfaceRebound(Vector3 velocity, Vector3 normal, float maximumOutwardSpeed)
    {
        if (normal.sqrMagnitude < 0.0001f)
        {
            return velocity;
        }

        // 単位法線で速度を分解し、下り坂も上り坂も同じ基準で扱う
        normal.Normalize();
        float excess = Vector3.Dot(velocity, normal) - Mathf.Max(0f, maximumOutwardSpeed);
        return excess > 0f ? velocity - normal * excess : velocity;
    }

    float LimitAssistedDriveTorque(float torque)
    {
        // ニュートラルと逆向きの制動トルクには加速上限を適用しない
        if (m_rigidbody == null || m_wheelController == null || m_mission == null)
        {
            return torque;
        }

        return CalculateDriveTorqueLimit(torque, m_mission.CurrentGearRatio, m_rigidbody.mass, m_maximumDriveAcceleration, m_wheelController.WheelRadius, m_mission.FinalDriveRatio);
    }

    // 前後進を対称に制限し、逆向きの制動トルクはそのまま返す関数
    static float CalculateDriveTorqueLimit(float torque, float gearRatio, float mass, float acceleration, float radius, float finalRatio)
    {
        if (torque * gearRatio <= 0f)
        {
            return torque;
        }

        // 車重と車輪半径に合う合計駆動トルクをデフ入力側へ換算する
        float limit = Mathf.Max(0f, mass) * Mathf.Max(0.1f, acceleration) * Mathf.Max(0f, radius);
        limit /= Mathf.Max(0.01f, finalRatio);
        return Mathf.Sign(torque) * Mathf.Min(Mathf.Abs(torque), limit);
    }

    // 接地中の壁離脱だけに共有加速度上限内の補助を一度ずつ割り当てる関数
    void ApplyBudgetedWallAssist(Vector3 direction, float requestedDeltaSpeed)
    {
        // 空中や固定中の車体を壁沿いに加速しない
        if (m_rigidbody == null || m_IsPullUp || GroundedWheelCount < 2)
        {
            return;
        }

        if (direction.sqrMagnitude < 0.0001f || requestedDeltaSpeed <= 0f)
        {
            return;
        }

        // 先に適用した復帰補助の分を差し引き、合計加速度が倍にならないようにする
        float deltaSpeed = Mathf.Min(requestedDeltaSpeed, Mathf.Max(0f, m_wallAssistRemainingDeltaSpeed));
        m_wallAssistRemainingDeltaSpeed -= deltaSpeed;
        m_rigidbody.linearVelocity += direction.normalized * deltaSpeed;
    }
}
