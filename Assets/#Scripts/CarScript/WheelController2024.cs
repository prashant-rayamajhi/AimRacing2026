// ホイールから車体に加わる力を計算する
using UnityEngine;

public class WheelController2024 : MonoBehaviour
{
    [SerializeField]
    public VehicleController m_vehicleController;
    [SerializeField]
    bool m_isRight; // 左右どちらのホイールなのか
    [SerializeField]
    bool m_isFront; // 前後どちらのホイールなのか
    [SerializeField]
    bool m_isDrive;
    [SerializeField]
    bool m_isSteer;
    [SerializeField]
    bool m_isIgnoreLoad;
    [SerializeField]
    bool m_trueTraction; // 計算方法切り替え
    [Space]
    [SerializeField]
    float m_mass;
    [SerializeField]
    float m_radius;
    [SerializeField]
    float m_width;
    [SerializeField]
    LayerMask m_layerMask = Physics.IgnoreRaycastLayer;
    [SerializeField]
    Transform m_visual;
    [SerializeField]
    float m_wheelRPM;
    Rigidbody m_vehicleRigidbody;
    RaycastHit m_raycastHit;
    bool m_bOnGround;
    [Header("Property")]
    // 車体に加える力
    [SerializeField, ShowInInspector]
    Vector3 m_totalF; // 最終的にAddforceする力
    [SerializeField, ShowInInspector]
    float m_tractionT; // 牽引力
    [SerializeField, ShowInInspector]
    float m_driveT; // 駆動トルク
    float m_brakeT; // ブレーキトルク
    float m_longF; // 縦力
    float m_latF; // 横力
    float m_load; // 荷重
    // 速度関連
    Vector3 m_wheelVelocity;
    float m_longSpeed;
    float m_latSpeed;
    [SerializeField, ShowInInspector]
    float m_longSlipVelocity; // 縦方向スリップ速度
    [SerializeField]
    float m_angularVelocity; // 角速度
    float m_inertia; // 慣性モーメント
    [Header("MagicFormula")]
    [SerializeField, ShowInInspector]
    float m_slipRatio; // 実際のスリップ率
    float m_diffSlipRatio; // 微分スリップ率
    [SerializeField, ShowInInspector]
    float m_slipAngle; // スリップ角
    // Load sensitive Magic Formula
    [Header("Load Sensitive Tire")]
    [SerializeField]
    float m_baseMu = 1.2f; // 基準摩擦係数（1.0～1.5）
    [SerializeField, Range(0.5f, 1.0f)]
    float m_loadSensitivity = 0.8f; // 荷重感度（小さいほど逓減）
    float m_refLoad; // 基準荷重（1輪あたり）
    // 摩擦
#pragma warning disable CS0414 // 摩擦計算式に未適用のInspector調整値(削除せず保持)

    [SerializeField]
    float m_frictonCoef = 1f; // 摩擦係数
#pragma warning restore CS0414
    [SerializeField, ShowInInspector]
    bool m_isWheelLocked;
    [SerializeField]
    MagicFormula m_longForceCurve;
    [SerializeField]
    MagicFormula m_latForceCurve;
    [SerializeField, Range(0.1f, 1f)]
    float m_relaxationLength;
    float m_steerAngle;
    [SerializeField] // 抵抗値の係数
    float resistanceValue = 0.015f;
    [SerializeField]
    float speedAdjustment = 100.0f; // 速度と係数をそのままの値で返すと値がでかすぎるのでこの変数で調整する
#pragma warning disable CS0414 // 低速時の抵抗緩和用に用意されているが、現状の計算式では未使用のInspector値

    [SerializeField]
    float slipBoostSpeed = 50.0f; // 10.0fなら10km/h以下で抵抗値を弱くする
#pragma warning restore CS0414
    [SerializeField]
    float SpinCoefficient = 1.0f; // ホイールスピンさせる係数
    [Header("Suspension")]
    // サスペンション関連
    [SerializeField]
    float m_suspensionDistance; // サスペンションの最大伸長距離(ローカル座標)
    [SerializeField]
    SimpleSpringJoint m_suspensionSpring; // バネの各種パラメータを設定
    [SerializeField, ShowInInspector]
    float m_suspensionLoad; // サスペンションから計算した上下荷重
    // RayCast
    [Header("MultiRaycast")]
    [SerializeField]
    int m_raysNumber = 36;
    [SerializeField]
    float m_raysMaxAngle = 180;
    float m_orgRadius;
    // 計算したMagicFormulaタイヤ力保存用
    public float m_Fx;
    public float m_Fy;
#region プロパティ
    public bool IsGround => m_bOnGround;
    // 前後どちらのホイールか判定用(ブレーキバイアスで使用)
    public bool IsFrontSide => m_isFront;
    // 左右どちらのホイールか判定用(アッカーマンアングルの計算で使用)
    public bool IsRightSide => m_isRight;
    // 駆動輪か判定用
    public bool IsDrive => m_isDrive;
    // 操舵輪か判定用
    public bool IsSteer => m_isSteer;
    public bool TrueTraction { get => m_trueTraction; set => m_trueTraction = value; }

    public float SteerAngle
    {
        get => m_steerAngle;
        set
        {
            // 操舵輪の場合のみset可能
            if (m_isSteer)
                m_steerAngle = value;
        }
    }

    public float Radius => m_radius;
    public float Inertia => m_inertia;
    public float Load { set => m_load = value; }
    public float LongForce => m_longF * m_load;
    public float SuspensionLoad { get => m_suspensionLoad; }
    // 速度関連
    public Vector3 PatchVelocity => m_wheelVelocity;
    public float LongSpeed => m_longSpeed;
    public float LatSpeed => m_latSpeed;
    public float LongSlip => m_longSlipVelocity;
    public float SlipRatio => m_slipRatio;
    public float SlipAngle => m_slipAngle;
    public float WheelAngularVelocity => m_angularVelocity;
    public float WheelRPM => m_angularVelocity * CarPhysics.Rad2RPM;
    public MagicFormula LongFrictionCurve => m_longForceCurve;
    public MagicFormula LatFrictionCurve => m_latForceCurve;

#endregion
    // Start is called before the first frame update
    void Start()
    {
        // Rigidbodyを取得
        m_vehicleRigidbody = GetComponentInParent<Rigidbody>();
        // 慣性モーメントを計算
        m_inertia = m_mass * Mathf.Pow(m_radius, 2f) / 2f;
        // 初期荷重(四等分)
        m_load = m_vehicleRigidbody.mass / 4f * Physics.gravity.magnitude;
        // 各種初期化
        m_latForceCurve.Initialize();
        m_longForceCurve.Initialize();
        m_driveT = 0f;
        m_angularVelocity = 0f;
        // 追加 -----------------------
        m_refLoad = m_vehicleRigidbody.mass * Physics.gravity.magnitude / 4f;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        UpdateWheelHit();
        // ステアリングの角度に曲げる
        m_visual.localEulerAngles = transform.localEulerAngles = new Vector3(0, m_steerAngle, 0);
        if (m_bOnGround)
        {
            UpdateSuspension();
            UpdateVelocity();
        }
        else
        {
            m_visual.position = transform.position + (-transform.up * m_suspensionDistance);
        }
    // // 急回転を防ぐ
    }

    /// <summary>
    /// 縦横方向にかかる力の計算(VehicleControllerから駆動トルクを渡す)
    /// </summary>
    public void UpdateTotalForce(in float _driveTorque, in float _brakeTorque)
    {
        if (!m_bOnGround)
            return;
        // 駆動トルクは駆動輪のみ加算
        if (m_isDrive)
        {
            m_driveT = _driveTorque;
        }

        m_brakeT = _brakeTorque;
        CalcTotalForce();
        // 車にかかっている力を可視化
        // 複合タイヤ力計算
        CalcCombineForce(out m_longF, out m_latF);
        if (m_trueTraction)
            m_tractionT = m_longF * m_load * m_radius;
        // 駆動トルクとブレーキトルクから角速度を更新
        UpdateAngularVelocity();
        // ホイールロック時にスリップ速度のSign(-1 or 1)を代入するだけで
        // ブレーキ時に坂道でビタっと止まることが出来る
        if (m_isWheelLocked)
            m_longF = Mathf.Sign(m_longSlipVelocity);
        // 総力の計算
        m_totalF += transform.forward * m_longF * m_load;
        m_totalF += transform.right * m_latF * m_load;
        // RigidbodyにAddForceする
        if (!float.IsNaN(m_totalF.magnitude))
            m_vehicleRigidbody.AddForceAtPosition(m_totalF, m_raycastHit.point, ForceMode.Force);
    }

    /// <summary>
    /// サスペンション更新
    /// </summary>
    void UpdateSuspension()
    {
        // ローカルの下方向をワールドに変換
        Vector3 down = transform.TransformDirection(Vector3.down);
        // 車輪の回転を考慮せずに、車輪が地面に対してどのくらいの速さで動いているかを計算する。
        Vector3 velocityAtTouch = m_vehicleRigidbody.GetPointVelocity(m_raycastHit.point);
        // スプリングの圧縮を計算する
        // 位置の差をサスペンションの全範囲で割る
        float compression = m_raycastHit.distance / (m_suspensionDistance + m_radius);
        compression = -compression + 1;
        // 最終的な力
        Vector3 force = -down * compression * m_suspensionSpring.Spring;
        // 接触点の速度をローカル空間に変換したもの
        Vector3 t = transform.InverseTransformDirection(velocityAtTouch);
        // ローカルXおよび、Z方向 = 0
        // ここで、tはショックが収縮/膨張する速度と等しいとする。
        t.z = 0;
        t.x = 0;
        // ワールド空間 * 減衰
        // この力はサスペンションの摩擦による力をシミュレートしています。
        Vector3 shockDrag = transform.TransformDirection(t) * -m_suspensionSpring.Damper;
        m_vehicleRigidbody.AddForceAtPosition(force + shockDrag, transform.position);
        m_suspensionLoad = (force + shockDrag).magnitude;
        m_visual.position = transform.position + (down * (m_raycastHit.distance - m_radius));
    }

    /// <summary>
    /// レイを1本飛ばして地面との当たり判定を取る
    /// </summary>
    void UpdateWheelHit()
    {
        // // 地面との当たり判定の更新
        // // 地面に飛ばすレイを可視化
        ///
        m_bOnGround = false;
        // サスペンション上端からレイを飛ばす
        Vector3 rayOrigin = transform.position + transform.up * m_suspensionDistance;
        float rayLength = m_suspensionDistance + m_radius;
        // 疑似コンタクトパッチ（前後左右＋中央）
        Vector3[] offsets =
        {
            Vector3.zero,
            transform.right * (m_width * 0.35f),
            -transform.right * (m_width * 0.35f),
            transform.forward * (m_radius * 0.3f),
            -transform.forward * (m_radius * 0.3f)
        };
        Vector3 avgPoint = Vector3.zero;
        Vector3 avgNormal = Vector3.zero;
        float avgDistance = 0f;
        int hitCount = 0;
        foreach (var offset in offsets)
        {
            if (Physics.Raycast(rayOrigin + offset, -transform.up, out RaycastHit hit, rayLength, m_layerMask))
            {
                hitCount++;
                avgPoint += hit.point;
                avgNormal += hit.normal;
                avgDistance += hit.distance;
            }
            else
            {
            }
        }

        if (hitCount > 0)
        {
            m_bOnGround = true;
            // 平均化（ここが超重要）
            avgPoint /= hitCount;
            avgNormal.Normalize();
            avgDistance /= hitCount;
            m_raycastHit.point = avgPoint;
            m_raycastHit.normal = avgNormal;
            m_raycastHit.distance = avgDistance;
        }
    }

    /// <summary>
    /// 縦横方向の速度を更新
    /// </summary>
    void UpdateVelocity()
    {
        // レイが当たった場所の速度を取得
        m_wheelVelocity = m_vehicleRigidbody.GetPointVelocity(m_raycastHit.point);
        m_longSpeed = Vector3.Dot(m_wheelVelocity, transform.forward);
        m_latSpeed = Vector3.Dot(m_wheelVelocity, transform.right);
        m_longSlipVelocity = m_radius * m_angularVelocity - m_longSpeed;
    }

    /// <summary>
    /// ホイールの角速度(回転速度)を更新
    /// </summary>
    void UpdateAngularVelocity()
    {
        float fixAngularVelocity = m_angularVelocity; // 一時計算用の変数
        // 加速力
        float tractionAngularAccel = m_longSlipVelocity / m_radius / Time.fixedDeltaTime;
        if (!m_trueTraction)
            m_tractionT = tractionAngularAccel * m_inertia;
        float totalT = m_driveT - m_tractionT;
        float angularAccle = totalT / m_inertia;
        fixAngularVelocity += angularAccle * Time.fixedDeltaTime;
        // 回転方向
        float prevRotationDirection = Mathf.Sign(fixAngularVelocity);
        if (fixAngularVelocity < float.Epsilon)
            prevRotationDirection = 0f;
        // 減速力
        m_brakeT = Mathf.Sign(fixAngularVelocity) * m_brakeT;
        // 転がり抵抗
        float rollresistT = Mathf.Sign(fixAngularVelocity) * GetResistanceValue() * m_load * m_radius;
        // 角速度を減少させる角加速度
        float angularDecele = (m_brakeT + rollresistT) / m_inertia;
        fixAngularVelocity -= angularDecele * Time.fixedDeltaTime;
        float fixRotationDirection = Mathf.Sign(fixAngularVelocity);
        if (fixAngularVelocity < float.Epsilon)
            fixRotationDirection = 0f;
        // 0通過チェック(減速力によってホイールの回転方向が変わったかチェック)
        if (prevRotationDirection != fixRotationDirection)
        {
            // ブレーキを踏んだ時逆方向に回転しないように0を代入
            m_angularVelocity = 0f;
            // 角速度を0に戻すとスリップ率がある程度の値のままな現象を確認
            m_diffSlipRatio = 0f;
            // →ブレーキを踏んだとき前後に動いてしまうので、ここで0を代入する
            m_isWheelLocked = true;
        }
        else
        {
            m_angularVelocity = fixAngularVelocity;
            m_isWheelLocked = false;
        }

        m_wheelRPM = m_angularVelocity * CarPhysics.Rad2RPM;
    }

    public float tau = 0.02f; // 調整の必要がある
    /// <summary>
    /// Addforceする力を計算
    /// </summary>
    void CalcTotalForce()
    {
        m_totalF = Vector3.zero;
        if (m_isIgnoreLoad)
            m_load = m_suspensionLoad;
        // 縦力の計算
        // 低速時での発散を回避するスリップ率計算[参考:SAE950311]
        // スリップ率に代入
        // スリップ速度（接線方向の相対速度） rω - v
        float slipVel = m_radius * m_angularVelocity - m_longSpeed;
        // 目標スリップ率
        // 低速でもゼロ割れしないように +0.1f を加えている
        float targetSlip = slipVel / (Mathf.Abs(m_longSpeed) + 0.1f);
        // 1次遅れ（Relaxation Length）
        float dSlip = (targetSlip - m_slipRatio) * (Mathf.Abs(m_longSpeed) / m_relaxationLength);
        // スリップ率更新
        m_slipRatio += dSlip * Time.fixedDeltaTime;
        // 荷重依存摩擦係数
        float mu = CalcLoadSensitiveMu(m_suspensionLoad);
        // クランプしてピーク近辺の値のみを取得する
        float sr = Mathf.Clamp(m_slipRatio, -m_longForceCurve.PeakSlipRatio, m_longForceCurve.PeakSlipRatio);
        // 荷重をかける前の縦力
        // 荷重を掛ける前の縦力（μを反映）
        m_longF = m_longForceCurve.Evaluate(m_slipRatio);
        // 横力の計算
        // スリップ角
        m_slipAngle = Mathf.Atan(-m_latSpeed / Mathf.Abs(m_longSpeed)) * Mathf.Rad2Deg;
        if (float.IsNaN(m_slipAngle))
            m_slipAngle = 0f;
        // クランプしてピーク近辺の値のみを取得する
        float sa = Mathf.Clamp(m_slipAngle, -m_latForceCurve.PeakSlipAngle, m_latForceCurve.PeakSlipAngle);
        // 荷重を掛ける前の横力
        // 荷重を掛ける前の横力（μを反映）
        m_latF = m_latForceCurve.Evaluate(m_slipAngle);
    }

    /// <summary>
    /// ルンゲクッタ法4次で  <br/>
    /// f(x,t) = x*t
    /// </summary>
    float CalcRK4()
    {
        // ローカル関数
        // それほどパフォーマンスには影響はない
        // f(t,y) = (Vsx - |Vx| * y) / B * t
        float calcDiffSR(float _t, float _y) => ((m_longSlipVelocity - Mathf.Abs(m_longSpeed) * _y) / m_relaxationLength) * _t;
        float dt = Time.deltaTime;
        float h = Time.fixedDeltaTime;
        float k1 = calcDiffSR(dt, m_diffSlipRatio);
        float k2 = calcDiffSR(dt + h / 2, m_diffSlipRatio + h / 2 * k1);
        float k3 = calcDiffSR(dt + h / 2, m_diffSlipRatio + h / 2 * k2);
        float k4 = calcDiffSR(dt + h, m_diffSlipRatio + h * k3);
        return m_diffSlipRatio + (k1 + 2 * k2 + 2 * k3 + k4) * h / 6;
    }

    /// <summary>
    /// 複合力を計算、縦横力を摩擦円に調整する
    /// </summary>
    void CalcCombineForce(out float _outlongF, out float _outLatF)
    {
        // 各スリップのピーク値を取得
        float slipRatio_peak = m_longForceCurve.PeakSlipRatio;
        float slipAngle_peak = m_latForceCurve.PeakSlipAngle;
        // 正規化
        float slipRatio_normalized = m_slipRatio / slipRatio_peak;
        float slipAngle_normalized = m_slipAngle / slipAngle_peak;
        // 三平方の定理です
        float combineSlip = Mathf.Sqrt(Mathf.Pow(slipRatio_normalized, 2f) + Mathf.Pow(slipAngle_normalized, 2f));
        // 0除算回避
        if (float.Epsilon > combineSlip)
            combineSlip = float.Epsilon;
        // 修正されたスリップ率とスリップ角
        float fixSlipRatio = combineSlip * slipRatio_peak;
        float fixSlipAngle = combineSlip * slipAngle_peak;
        _outlongF = m_longForceCurve.Evaluate(fixSlipRatio) * (slipRatio_normalized / combineSlip);
        _outLatF = m_latForceCurve.Evaluate(fixSlipAngle) * (slipAngle_normalized / combineSlip);
        m_Fx = _outlongF;
        m_Fy = _outLatF;
    }

    void CalcFrictionEllipse(ref float Fx, ref float Fy, float muX, float muY, float load)
    {
        float FxMax = muX * load;
        float FyMax = muY * load;
        float nx = Fx / FxMax;
        float ny = Fy / FyMax;
        float r = nx * nx + ny * ny;
        if (r > 1f)
        {
            float scale = 1f / Mathf.Sqrt(r);
            Fx *= scale;
            Fy *= scale;
        }
    }

    void CalcCombineForce_SAE(out float _outLongF, out float _outLatF)
    {
        // https://www.sae.org/publications/technical-papers/content/2023-01-0684/
        // 各スリップのピーク値を取得
        float slipRatio_peak = m_longForceCurve.PeakSlipRatio;
        float slipAngle_peak = m_latForceCurve.PeakSlipAngle;
        // 正規化
        float slipRatio_norm = m_slipRatio / slipRatio_peak;
        float slipAngle_norm = m_slipAngle / slipAngle_peak;
        // 複合スリップ
        float combineSlip = Mathf.Sqrt(m_slipRatio * m_slipRatio + m_slipAngle * m_slipAngle);
        // 0除算回避
        if (float.Epsilon > combineSlip)
            combineSlip = float.Epsilon;
        // 正規化複合スリップ
        float combineSlip_norm = Mathf.Sqrt(slipRatio_norm * slipRatio_norm + slipAngle_norm * slipAngle_norm);
        _outLongF = m_slipRatio / (slipRatio_peak * combineSlip) * m_longForceCurve.Evaluate(combineSlip_norm * slipRatio_peak);
        _outLatF = m_slipAngle / (slipAngle_peak * combineSlip) * m_latForceCurve.Evaluate(combineSlip_norm * slipAngle_peak);
    }

#region RaycastExpansion
    /// <summary>
    /// レイキャストを円形に飛ばす
    /// </summary>
    void RaycastExpansion()
    {
        float radiusOffset = 0.0f;
        for (int i = 0; i <= m_raysNumber; i++)
        {
            // Rayを飛ばす角度を計算
            // Quaternion.AngleAxis(タイヤ角, タイヤの上方向ベクトル)
            // * Quaternion.AngleAxis(現在のレイのナンバー * (レイの最大角 / レイの数)
            Vector3 rayDirection = Quaternion.AngleAxis(m_steerAngle, transform.up) * Quaternion.AngleAxis(i * (m_raysMaxAngle / m_raysNumber) + ((180.0f - m_raysMaxAngle) / 2.0f), transform.right) * transform.forward;
            // 現在のレイの方向にタイヤの中心から半径の長さ分、判定を取る
            if (Physics.Raycast(transform.position, rayDirection, out RaycastHit hit, m_radius, m_layerMask))
            {
                // レイの原点から衝突点までの距離を計算
                radiusOffset = Mathf.Max(radiusOffset, m_radius - hit.distance);
            }
        }

        // 新しい半径を計算した分線形補間する
        m_radius = Mathf.LerpUnclamped(m_radius, m_orgRadius + radiusOffset, Time.deltaTime * 10.0f);
    }

#endregion
#region Gizmo
    /// <summary>
    /// 選択時のみ呼び出されるGizmo描画用関数
    /// </summary>
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        var forwardOffset = transform.forward * 0.07f;
        var springOffset = transform.up * m_radius;
        Gizmos.DrawLine(transform.position - forwardOffset, transform.position + forwardOffset);
        Gizmos.DrawLine(transform.position - springOffset - forwardOffset, transform.position - springOffset + forwardOffset);
        Gizmos.DrawLine(transform.position, transform.position - springOffset);
        // タイヤを描画
        DrawWheelGizmo(m_radius * transform.lossyScale.x, m_width, m_visual.position, transform.up, transform.forward, transform.right);
        // サスペンションを描画
        DrawSuspensionGizmo();
    }

    /// <summary>
    /// ホイールの形のGizmoを描画
    /// </summary>
    void DrawWheelGizmo(float radius, float width, Vector3 position, Vector3 up, Vector3 forward, Vector3 right)
    {
        Gizmos.color = Color.green;
        var halfWidth = width / 2.0f;
        float theta = 0.0f;
        float x = radius * Mathf.Cos(theta);
        float y = radius * Mathf.Sin(theta);
        Vector3 pos = position + up * y + forward * x;
        Vector3 newPos;
        for (theta = 0.0f; theta <= Mathf.PI * 2; theta += Mathf.PI / 12.0f)
        {
            x = radius * Mathf.Cos(theta);
            y = radius * Mathf.Sin(theta);
            newPos = position + up * y + forward * x;
            Gizmos.DrawLine(pos - right * halfWidth, newPos - right * halfWidth);
            Gizmos.DrawLine(pos + right * halfWidth, newPos + right * halfWidth);
            Gizmos.DrawLine(pos - right * halfWidth, pos + right * halfWidth);
            Gizmos.DrawLine(pos - right * halfWidth, newPos + right * halfWidth);
            pos = newPos;
        }
    }

    /// <summary>
    /// サスペンションのGizmoを描画
    /// </summary>
    void DrawSuspensionGizmo()
    {
        Gizmos.color = Color.green;
        if (!m_bOnGround)
            Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position - transform.up * m_radius, transform.position + (transform.up * m_suspensionDistance));
        Gizmos.DrawSphere(transform.position + transform.up * m_suspensionDistance, 0.1f);
    }

#endregion
    /// <summary>
    /// 加減速時の前後荷重配分の計算
    /// </summary>
    private Vector3 prevForwardVelocity;
    [Header("LoadBalance front:0 ~ rear:1.0")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float weightBalance;
    float CalcAcceleAndDeceleLoad(bool _isFront)
    {
        Vector3 m_velocity = m_vehicleRigidbody.linearVelocity;
        Vector3 forward = gameObject.transform.forward;
        Vector3 forwardVelocity = Vector3.Dot(m_velocity, forward) * forward;
        float w = 1190.0f;
        float l = 2.51f;
        float lf = l * (1.0f - weightBalance);
        float lr = l * weightBalance;
        float h = 0.3343f;
        float a = forwardVelocity.magnitude - prevForwardVelocity.magnitude;
        float g = 9.80665f;
        prevForwardVelocity = forwardVelocity;
        float wf = (w * (lr / l)) - (h / l) * (w / g) * a * 100.0f;
        float wr = (w * (lf / l)) + (h / l) * (w / g) * a * 100.0f;
        return _isFront ? wf : wr;
    }

    /// <summary>
    /// ホイールスピンをさせる処理
    /// </summary>
    private void WheelSpin(float _driveTorque, float _brakeTorque)
    {
        float tireRadius = m_radius; // タイヤ直径
        float mu = 0.9f;
        float Fz = m_vehicleRigidbody.mass * 9.81f / 4.0f; // タイヤ一個あたりの質量
        float vWheel = m_wheelRPM * m_radius; // タイヤ外周速度 [m/s](単位不明)
        float vCar = m_vehicleController.KPH / 3600.0f; // 車体前進速度 [m/s](時速から秒速に変換)
        // スリップ率（発進時に特に重要）
        float slipRatio = (vWheel - vCar) / Mathf.Max(Mathf.Abs(vCar), 1.0f);
        // 空転時の補正
        if (slipRatio > 0.001f)
        {
            float scale = Mathf.Clamp01(1f - (slipRatio - 0.15f) * 10f);
            m_Fx = scale;
        }

        // 1.駆動力の理想値
        float idealForce = _driveTorque / tireRadius;
        // 2.摩擦限界
        float Fmax = mu * Fz;
        // 3.実際に伝わる力
        float Fx = m_Fx;
        // 駆動力制限
        float Fx_max = mu * Fz;
        Fx = Mathf.Clamp(Fx, -Fx_max, Fx_max);
        // ホイールスピン判定
        bool isFullThrottle = false;
        // フルスロットル状態ならフラグを立てる
        if (m_vehicleController.EngineRPM >= m_vehicleController.passEngine.OverRevRPM)
        {
            isFullThrottle = true;
        }

        // エンジン回転数の処理
        bool isWheelSpin = (m_vehicleController.ActiveGear == 1) && isFullThrottle && slipRatio > 0.15f;
        if (Mathf.Abs(idealForce) > Fmax)
        {
            // 限界値に張り付くため、車は加速できない
            Fx = Mathf.Sign(idealForce) * Fmax * SpinCoefficient;
            // 余ったトルクでホイール回転数だけが増える
            float slipTorque = _driveTorque - Fx * tireRadius;
            m_angularVelocity += (slipTorque / Inertia) * Time.fixedDeltaTime;
        }
        else
        {
            Fx = idealForce;
        }

        // 4. ブレーキ力を追加
        if (_brakeTorque > 0f)
        {
            float brakeForce = Mathf.Min(_brakeTorque / tireRadius, Fmax);
            Fx -= Mathf.Sign(WheelAngularVelocity) * brakeForce;
            // ホイール回転も減速
            m_angularVelocity -= (_brakeTorque / Inertia) * Time.fixedDeltaTime;
        }

        // 5.車体に力を加える
        m_vehicleRigidbody.AddForceAtPosition(transform.forward * Fx, transform.position);
    }

    // 08/31 追加
    /// <summary>
    /// 発進時に抵抗値をそのままにして発進できるようにする関数
    /// </summary>
    /// <returns></returns>
    private float GetResistanceValue()
    {
        // InspectorでresistanceValueの値を調整できるように変数に代入
        float afterResistVal = resistanceValue;
        // 指定速度以下は抵抗値を弱くする
        return afterResistVal = resistanceValue * (m_vehicleController.KPH / speedAdjustment) + 0.015f;
    }

    // 09/15 追加
    public float stabilityFactor = 2.0f; // 調整用（大きいほど強制的に前を向く）
    /// <summary>
    /// スピンと逆方向に力を加えて急回転を防ぐ関数
    /// </summary>
    void ApplyStability()
    {
        // 車速が小さいときは無視
        Vector3 velocity = m_vehicleRigidbody.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < 1f)
            return;
        // 車体の前方方向
        Vector3 forward = transform.forward;
        // 進行方向ベクトル
        Vector3 velDir = velocity.normalized;
        // 車体前方と進行方向の角度差（横滑り角っぽい値）
        float angle = Vector3.SignedAngle(forward, velDir, Vector3.up);
        // 角度差に比例して復元トルクを加える
        float restoringTorque = -angle * stabilityFactor;
        m_vehicleRigidbody.AddTorque(Vector3.up * restoringTorque, ForceMode.Acceleration);
    }

    float CalcLoadSensitiveMu(float load)
    {
        // 0除算・異常値対策
        if (load < 1f)
            load = 1f;
        // μ(Fz) = μ0 * (Fz / Fz0)^n
        return m_baseMu * Mathf.Pow(load / m_refLoad, m_loadSensitivity);
    }
}
