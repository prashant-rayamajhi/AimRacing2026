using System;
using UnityEngine;

// 壁との衝突応答と衝突後の姿勢安定化を管理するクラス
public partial class VehicleController
{
    // 壁の角へ衝突した際に過剰な横転を抑えるための設定
    [Header("Collision Stability")]
    [SerializeField]
    bool m_collisionStabilityEnabled = true;
    [SerializeField, Range(1f, 15f)]
    float m_sideImpactMinimumSpeed = 3f;
    [SerializeField, Range(0.1f, 4f)]
    float m_sideImpactStabilityDuration = 0.45f;
    [SerializeField, Range(0.05f, 1f)]
    float m_sideImpactMaximumRollSpeed = 0.09f;
    // 衝突時に許可する車体前後方向の最大回転速度
    [SerializeField, Range(0.05f, 1f)]
    float m_sideImpactMaximumPitchSpeed = 0.09f;
    // 衝突時に許可する車体旋回方向の最大回転速度
    [SerializeField, Range(0.2f, 3f)]
    float m_sideImpactMaximumYawSpeed = 0.9f;
    [SerializeField, Range(0f, 20f)]
    float m_sideImpactUprightStrength = 4f;
    [SerializeField, Range(0f, 20f)]
    float m_sideImpactRollDamping = 12f;
    // 衝突後の前後揺れを収める減衰速度
    [SerializeField, Range(0f, 20f)]
    float m_sideImpactPitchDamping = 12f;
    [UnityEngine.Serialization.FormerlySerializedAs("m_sideImpactMaximumCorrectionAngle")]
    [SerializeField, Range(10f, 80f)]
    float m_sideHitMaxTurnAngle = 30f;
    [UnityEngine.Serialization.FormerlySerializedAs("m_sideImpactMaximumReboundSpeed")]
    [SerializeField, Range(0f, 0.5f)]
    float m_sideHitMaxBounceSpeed = 0.15f;
    [UnityEngine.Serialization.FormerlySerializedAs("m_sideImpactMaximumUpwardSpeed")]
    [SerializeField, Range(0f, 1f)]
    float m_sideHitMaxRiseSpeed = 0.05f;
    [SerializeField, Range(0.1f, 1f)]
    float m_sideImpactDriveTorqueRatio = 0.5f;
    [SerializeField, Range(0.2f, 3f)]
    float m_maxDepenetrationVelocity = 0.8f;
    [UnityEngine.Serialization.FormerlySerializedAs("m_sideImpactMinimumIncidence")]
    [SerializeField, Range(0.05f, 0.8f)]
    float m_sideHitMinIncidence = 0.1f;
    [UnityEngine.Serialization.FormerlySerializedAs("m_sideImpactDriveReductionIncidence")]
    [SerializeField, Range(0.1f, 0.9f)]
    float m_sideHitTorqueCutThreshold = 0.5f;
    [SerializeField, Range(0.05f, 1f)]
    float m_sideImpactReleaseDuration = 0.18f;
    // 浅く壁を擦った時に維持する接線方向の速度割合
    [SerializeField, Range(0.5f, 1f)]
    float m_glancingImpactSpeedRetention = 0.997f;
    // 正面に近い壁衝突で残す接線方向の速度割合
    [SerializeField, Range(0f, 1f)]
    float m_directImpactSpeedRetention = 0.97f;
    // 浅い衝突で壁方向の速度を壁沿いへ振り替える割合
    [SerializeField, Range(0f, 0.5f)]
    float m_glancingImpactTangentialRedirect = 0.12f;
    // 正面に近い衝突で壁方向の速度を壁沿いへ振り替える割合
    [SerializeField, Range(0f, 0.3f)]
    float m_directImpactTangentialRedirect = 0.04f;
    // 壁接触の速度補正を始める法線方向速度
    [SerializeField, Range(0.1f, 3f)]
    float m_impactResponseMinimumSpeed = 0.5f;
    // 同じ壁Colliderの継ぎ目で初回衝突減速を繰り返さない待機時間
    [SerializeField, Range(0.05f, 0.5f)]
    float m_initialImpactRepeatCooldown = 0.2f;
    // 隣接する壁Colliderを同じ壁として扱う初回衝突減速の待機時間
    [SerializeField, Range(0.1f, 1f)]
    float m_adjacentImpactRepeatCooldown = 0.4f;
    // 隣接する壁面を同じ向きとして扱う法線の一致率
    [SerializeField, Range(0.5f, 1f)]
    float m_repeatedImpactNormalSimilarity = 0.8f;
    // 正面衝突以外で必ず残す最低速度割合
    [UnityEngine.Serialization.FormerlySerializedAs("m_nonFrontalMinimumSpeedRetention")]
    [SerializeField, Range(0.05f, 0.9f)]
    float m_sideHitMinSpeedRatio = 0.18f;
    [Header("Collision Assist")]
    // 車両外側の衝突で操作を戻しやすくする補助割合
    [SerializeField, Range(0f, 1f)]
    float m_collisionGameplayAssist = 0.65f;
    // ヘッドライト外側の深い衝突で残す速度割合
    [UnityEngine.Serialization.FormerlySerializedAs("m_offCenterImpactSpeedRetention")]
    [SerializeField, Range(0.1f, 0.4f)]
    float m_offCenterSpeedRatio = 0.3f;
    // 車体中央からこの距離以上の接触へ外側補助を最大適用する距離
    [UnityEngine.Serialization.FormerlySerializedAs("m_offCenterFullAssistLateralOffset")]
    [SerializeField, Range(0.5f, 1.5f)]
    float m_fullAssistSideOffset = 0.9f;
    // 完全停止に近い減速を許可する正面衝突割合
    [SerializeField, Range(0.8f, 1f)]
    float m_frontalStopIncidence = 0.985f;
    // 完全停止を許可する車体中央からの最大横方向距離
    [UnityEngine.Serialization.FormerlySerializedAs("m_frontalStopMaximumLateralOffset")]
    [SerializeField, Range(0.1f, 1f)]
    float m_frontStopMaxSideOffset = 0.35f;
    // 完全停止を許可する車体中心から進行方向側への最小距離
    [UnityEngine.Serialization.FormerlySerializedAs("m_frontalStopMinimumLongitudinalOffset")]
    [SerializeField, Range(0.1f, 2f)]
    float m_frontStopMinForwardOffset = 0.8f;
    // 完全停止を許可する壁面法線と車体進行方向の最低一致率
    [UnityEngine.Serialization.FormerlySerializedAs("m_frontalStopMinimumNormalAlignment")]
    [SerializeField, Range(0.5f, 1f)]
    float m_frontStopNormalThreshold = 0.9f;
    // 車体底面が路面へ触れた時に許可する最大上向き速度
    [UnityEngine.Serialization.FormerlySerializedAs("m_roadBodyContactMaximumUpwardSpeed")]
    [SerializeField, Range(0.1f, 2f)]
    float m_roadContactMaxRiseSpeed = 0.55f;
    // 浅い接触として扱う衝突速度の法線成分割合
    [SerializeField, Range(0f, 0.5f)]
    float m_glancingImpactIncidence = 0.18f;
    // 正面衝突として扱う衝突速度の法線成分割合
    [SerializeField, Range(0.5f, 1f)]
    float m_directImpactIncidence = 0.95f;
    // 車体が壁を擦った時にタイヤ摩擦とは別の強い減速が発生しないための摩擦値
    [SerializeField, Range(0f, 0.2f)]
    float m_bodyCollisionFriction = 0.02f;
    // 壁接触後の姿勢安定化を継続する残り時間
    float m_sideImpactStabilityTime;
    // 壁から車体側へ向く衝突面の水平法線
    Vector3 m_sideImpactNormal;
    // 衝突前速度に占める壁方向速度の割合
    float m_sideImpactIncidence;
    // 衝突後の速度補正に使う物理更新直前の車体速度
    Vector3 m_preStepVelocity;
    // 壁接触後の逆ハンドルを短時間だけ補助する継続時間
    [SerializeField, Range(0.1f, 1.5f)]
    float m_wallSpinRecoverySeconds = 0.8f;
    // 逆ハンドルと反対へ回り続ける速度を減衰する時定数
    [SerializeField, Range(0.05f, 0.5f)]
    float m_wallSpinDampingSeconds = 0.12f;
    // 浅い接触後にも姿勢回復を続けるため最後に壁へ触れた時刻を保持する
    float m_wallSpinContactTime = float.NegativeInfinity;
    // 物理更新直前の車体速度を取得済みか示す状態
    bool m_hasPreStepVelocity;
    // 車体と壁の摩擦と反発を抑える実行時物理マテリアル
    PhysicsMaterial m_bodyCollisionMaterial;
    // 直前に初回衝突減速を適用した壁Collider
    Collider m_lastInitialImpactCollider;
    // 直前に初回衝突減速を適用した物理時刻
    float m_lastInitialImpactTime = float.NegativeInfinity;
    // 直前に初回衝突減速を適用した壁面の水平法線
    Vector3 m_lastInitialImpactNormal;
    // 壁へ押し付けられて低速停止した車体を小さく路面側へ戻す設定
    [Header("Wall Stuck Recovery")]
    [SerializeField]
    bool m_wallStuckRecoveryEnabled = true;
    [SerializeField, Range(0.2f, 1f)]
    float m_wallStuckMinimumAccel = 0.55f;
    [SerializeField, Range(0.5f, 5f)]
    float m_wallStuckMaximumSpeedKph = 2f;
    [SerializeField, Range(0.05f, 2f)]
    float m_wallStuckRecoveryDelay = 0.2f;
    [SerializeField, Range(0.02f, 0.3f)]
    float m_wallStuckPositionNudge = 0.12f;
    [SerializeField, Range(0.5f, 5f)]
    float m_wallStuckReleaseSpeedKph = 3f;
    [SerializeField, Range(0f, 5f)]
    float m_wallStuckOutwardAcceleration = 0.5f;
    // 壁擦り直後の速度消失を防ぐために壁沿い速度を維持する時間
    [SerializeField, Range(0.1f, 1f)]
    float m_wallFollowSupportDuration = 0.35f;
    // 壁沿い速度を自然に減衰させる毎秒の速度
    [SerializeField, Range(1f, 15f)]
    float m_wallFollowDeceleration = 8f;
    Vector3 m_wallContactNormal;
    // 正面中央以外の接触で壁沿いへ復帰してよいか保持する状態
    bool m_wallContactAllowsSlide;
    // 低速停止後も維持する壁沿いの脱出方向
    Vector3 m_wallEscapeDirection;
    float m_lastWallContactTime = float.NegativeInfinity;
    float m_wallStuckTime;
    bool m_wallStuckNudgeApplied;
    Vector3 m_wallFollowDirection;
    float m_wallFollowMinimumSpeed;
    float m_wallFollowMaximumSpeed;
    float m_wallFollowSupportTime;
    // 壁沿い復帰で一度に加えられる速度差を制限する加速度
    [SerializeField, Range(0.1f, 5f)]
    float m_wallAssistAcceleration = 2f;
    // 同じ物理更新で接触した壁面をまとめて制約するための法線配列
    readonly Vector3[] m_contactWallNormals = new Vector3[16];
    // 物理更新内で保存した壁面法線の数
    int m_contactWallNormalCount;
    // 複数コールバックを同じ物理更新として扱う時刻
    float m_contactWallFixedTime = float.NegativeInfinity;
    // 路面と壁の接触を分ける上向き法線の境界
    const float m_wallNormalUpLimit = 0.55f;
    // 複数面の制約が互いに再発生しないよう繰り返す回数
    const int m_contactProjectionPasses = 8;
    // 一物理更新で観測した補正前の最大反発速度
    public float ContactReboundBeforeKph { get; private set; }
    // 一物理更新で全壁面の補正後に残る最大反発速度
    public float ContactReboundAfterKph { get; private set; }
    // 運転操作による離脱速度を除いた補正後の反発上限超過
    public float ContactExcessReboundKph { get; private set; }

    // 壁へ衝突した側と衝突速度をFFBへ通知するイベント
    public event Action<bool, float> WallImpact;
    // 衝突安定化中に壁から離れる方向へ残っている速度
    public float WallReboundSpeedKph => m_sideImpactStabilityTime > 0f && m_rigidbody != null ? Mathf.Max(0f, Vector3.Dot(Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up), m_sideImpactNormal)) * 3.6f : 0f;

    // 同じ壁への細かな再接触で衝突FFBを連打しないための最終通知時刻
    float m_lastWallImpactNotificationTime = float.NegativeInfinity;
    // 同じ壁面への再接触か判定するための最終通知法線
    Vector3 m_lastWallImpactNotificationNormal;
    // 旧シーン設定を残したまま壁擦り時の速度維持と再発進時間を安全範囲へ収める関数
    void ApplyCollisionTuningLimits()
    {
        m_glancingImpactTangentialRedirect = Mathf.Min(m_glancingImpactTangentialRedirect, 0.12f);
        m_directImpactTangentialRedirect = Mathf.Min(m_directImpactTangentialRedirect, 0.04f);
        m_sideHitMinSpeedRatio = Mathf.Min(m_sideHitMinSpeedRatio, 0.18f);
        m_sideHitMaxBounceSpeed = Mathf.Min(m_sideHitMaxBounceSpeed, 0.15f);
        m_maxDepenetrationVelocity = Mathf.Min(m_maxDepenetrationVelocity, 0.5f);
        m_wallStuckRecoveryDelay = Mathf.Min(m_wallStuckRecoveryDelay, 0.2f);
        m_wallStuckMaximumSpeedKph = Mathf.Max(m_wallStuckMaximumSpeedKph, 5f);
        m_wallStuckReleaseSpeedKph = Mathf.Max(m_wallStuckReleaseSpeedKph, 5f);
        m_wallStuckPositionNudge = Mathf.Min(m_wallStuckPositionNudge, 0.05f);
        m_wallStuckOutwardAcceleration = Mathf.Min(m_wallStuckOutwardAcceleration, 0.5f);
        m_frontalStopIncidence = Mathf.Max(m_frontalStopIncidence, 0.985f);
        m_frontStopMaxSideOffset = Mathf.Min(m_frontStopMaxSideOffset, 0.35f);
        m_frontStopMinForwardOffset = Mathf.Max(m_frontStopMinForwardOffset, 0.8f);
        m_frontStopNormalThreshold = Mathf.Max(m_frontStopNormalThreshold, 0.9f);
        m_collisionGameplayAssist = Mathf.Clamp01(m_collisionGameplayAssist);
        m_offCenterSpeedRatio = Mathf.Clamp(m_offCenterSpeedRatio, m_sideHitMinSpeedRatio, 0.35f);
        m_fullAssistSideOffset = Mathf.Max(m_fullAssistSideOffset, m_frontStopMaxSideOffset + 0.1f);
    }

    // 車体コライダーの摩擦と反発を抑えて壁を浅く擦った時の急停止を防ぐ関数
    void ConfigureBodyCollisionMaterial()
    {
        m_bodyCollisionMaterial = new PhysicsMaterial("VehicleBodyCollision")
        {
            dynamicFriction = m_bodyCollisionFriction,
            staticFriction = m_bodyCollisionFriction,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        Collider[] vehicleColliders = GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < vehicleColliders.Length; index++)
        {
            Collider vehicleCollider = vehicleColliders[index];
            if (vehicleCollider is WheelCollider || vehicleCollider.attachedRigidbody != m_rigidbody || vehicleCollider.isTrigger)
            {
                continue;
            }

            vehicleCollider.sharedMaterial = m_bodyCollisionMaterial;
        }
    }

    // 壁面へ衝突した瞬間から跳ね返りと姿勢変化を抑える関数
    void OnCollisionEnter(Collision _collision)
    {
        LimitContactRebound(_collision);
        if (HandleRoadSurfaceCollision(_collision))
        {
            return;
        }

        NotifyWallImpact(_collision);
        ApplyImpactAngleSpeedResponse(_collision, ShouldApplyInitialImpactLoss(_collision));
        SynchronizeAutomaticGearAfterImpact();
        BeginSideImpactStability(_collision);
        LimitContactRebound(_collision);
    }

    // 衝突応答後の実車速をATへ渡して高いギアが残ることを防ぐ関数
    void SynchronizeAutomaticGearAfterImpact()
    {
        if (m_mission == null || m_rigidbody == null)
        {
            return;
        }

        float speedKph = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up).magnitude * 3.6f;
        m_mission.SynchronizeAutomaticGearAfterImpact(speedKph);
    }

    // 壁衝突の接触側と衝突速度をG923の衝撃FFBへ渡す関数
    void NotifyWallImpact(Collision _collision)
    {
        if (_collision == null || _collision.contactCount == 0 || WallImpact == null)
        {
            return;
        }

        Vector3 horizontalIncomingVelocity = Vector3.ProjectOnPlane(GetVelocityBeforeImpact(), Vector3.up);
        Vector3 impactNormal = GetImpactNormal(_collision, horizontalIncomingVelocity);
        if (impactNormal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float impactSpeed = Mathf.Max(0f, -Vector3.Dot(horizontalIncomingVelocity, impactNormal));
        if (impactSpeed < m_impactResponseMinimumSpeed)
        {
            return;
        }

        bool repeatedSurface = Vector3.Dot(impactNormal, m_lastWallImpactNotificationNormal) >= m_repeatedImpactNormalSimilarity;
        if (Time.fixedTime - m_lastWallImpactNotificationTime < m_initialImpactRepeatCooldown)
        {
            return;
        }

        if (repeatedSurface && Time.fixedTime - m_lastWallImpactNotificationTime < m_adjacentImpactRepeatCooldown)
        {
            return;
        }

        Vector3 localContactPoint = transform.InverseTransformPoint(_collision.GetContact(0).point);
        m_lastWallImpactNotificationTime = Time.fixedTime;
        m_lastWallImpactNotificationNormal = impactNormal;
        WallImpact.Invoke(localContactPoint.x > 0f, impactSpeed);
    }

    // 壁面へ接触している間も跳ね返りと姿勢変化を抑える関数
    void OnCollisionStay(Collision _collision)
    {
        LimitContactRebound(_collision);
        if (HandleRoadSurfaceCollision(_collision))
        {
            return;
        }

        ApplyImpactAngleSpeedResponse(_collision, false);
        BeginSideImpactStability(_collision);
        LimitContactRebound(_collision);
    }

    // Roadレイヤーの上向き面を路面として処理し、縦面は壁処理へ渡す関数
    bool HandleRoadSurfaceCollision(Collision _collision)
    {
        if (_collision == null || _collision.collider == null)
        {
            return true;
        }

        int roadLayer = LayerMask.NameToLayer("Road");
        if (roadLayer < 0 || _collision.collider.gameObject.layer != roadLayer)
        {
            return false;
        }

        bool hasRoadSurface = false;
        bool hasWallSurface = false;
        for (int index = 0; index < _collision.contactCount; index++)
        {
            Vector3 normal = _collision.GetContact(index).normal;
            float upwardAlignment = Vector3.Dot(normal, Vector3.up);
            if (upwardAlignment >= 0.55f)
            {
                hasRoadSurface = true;
                LimitRoadBodyRebound(normal);
            }

            if (Mathf.Abs(upwardAlignment) < 0.55f)
            {
                hasWallSurface = true;
            }
        }

        return hasRoadSurface && !hasWallSurface;
    }

    // 坂道を進む速度を残し、路面から離れる反発成分だけを制限する関数
    void LimitRoadBodyRebound(Vector3 _normal)
    {
        if (m_rigidbody == null)
        {
            return;
        }

        m_rigidbody.linearVelocity = LimitSurfaceRebound(m_rigidbody.linearVelocity, _normal, m_roadContactMaxRiseSpeed);
    }

    // 同じ壁または同じ向きの隣接壁へ短時間で再接触した時に初回減速を一度だけ適用する関数
    bool ShouldApplyInitialImpactLoss(Collision _collision)
    {
        if (_collision == null || _collision.collider == null || _collision.contactCount == 0)
        {
            return true;
        }

        // 衝突直前の水平速度
        Vector3 incomingVelocity = Vector3.ProjectOnPlane(GetVelocityBeforeImpact(), Vector3.up);
        // 継ぎ目判定に使用する壁面の水平法線
        Vector3 selectedNormal = GetImpactNormal(_collision, incomingVelocity);
        float currentTime = Time.fixedTime;
        float elapsedTime = currentTime - m_lastInitialImpactTime;
        bool sameCollider = m_lastInitialImpactCollider == _collision.collider && elapsedTime < m_initialImpactRepeatCooldown;
        bool adjacentSurface = selectedNormal.sqrMagnitude > 0.0001f && m_lastInitialImpactNormal.sqrMagnitude > 0.0001f && Vector3.Dot(selectedNormal, m_lastInitialImpactNormal) >= m_repeatedImpactNormalSimilarity && elapsedTime < m_adjacentImpactRepeatCooldown;
        if (sameCollider || adjacentSurface)
        {
            return false;
        }

        m_lastInitialImpactCollider = _collision.collider;
        m_lastInitialImpactTime = currentTime;
        m_lastInitialImpactNormal = selectedNormal;
        return true;
    }

    // 複数の接触面から車体へ最も強く作用する壁面法線を返す関数
    Vector3 GetImpactNormal(Collision _collision, Vector3 _horizontalIncomingVelocity)
    {
        if (_collision == null || _horizontalIncomingVelocity.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        // 車体速度に対する衝突割合が最も大きい壁面法線
        Vector3 selectedNormal = Vector3.zero;
        // 現在選択している壁面の衝突割合
        float selectedIncidence = float.NegativeInfinity;
        float horizontalSpeed = _horizontalIncomingVelocity.magnitude;
        for (int index = 0; index < _collision.contactCount; index++)
        {
            Vector3 contactNormal = _collision.GetContact(index).normal;
            if (Mathf.Abs(Vector3.Dot(contactNormal, Vector3.up)) >= 0.55f)
            {
                continue;
            }

            Vector3 horizontalNormal = Vector3.ProjectOnPlane(contactNormal, Vector3.up);
            if (horizontalNormal.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            horizontalNormal.Normalize();
            float inwardSpeed = Mathf.Max(0f, -Vector3.Dot(_horizontalIncomingVelocity, horizontalNormal));
            float incidence = inwardSpeed / Mathf.Max(0.1f, horizontalSpeed);
            if (inwardSpeed < m_impactResponseMinimumSpeed || incidence <= selectedIncidence)
            {
                continue;
            }

            selectedIncidence = incidence;
            selectedNormal = horizontalNormal;
        }

        return selectedNormal;
    }

    // 衝突面の法線と速度から壁面衝突を判定して安定化を開始する関数
    void BeginSideImpactStability(Collision _collision)
    {
        if (!m_collisionStabilityEnabled || _collision.contactCount == 0)
        {
            return;
        }

        // 力積が小さい壁擦りでも車輪の横力が変わるため接触後の回復対象へ含める
        for (int i = 0; i < _collision.contactCount; i++)
        {
            if (Mathf.Abs(_collision.GetContact(i).normal.y) < 0.55f)
            {
                m_wallSpinContactTime = Time.fixedTime;
                break;
            }
        }

        Vector3 horizontalIncomingVelocity = Vector3.ProjectOnPlane(GetVelocityBeforeImpact(), Vector3.up);
        if (horizontalIncomingVelocity.sqrMagnitude <= 0.01f)
        {
            RememberRestingWallContact(_collision);
            return;
        }

        Vector3 horizontalNormal = GetImpactNormal(_collision, horizontalIncomingVelocity);
        if (horizontalNormal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        // 最も強く当たった壁面だけを使い重複Colliderの別法線で左右へ反射しないようにする
        RememberWallContact(horizontalNormal);
        LimitCollisionRebound(horizontalNormal);
        LimitSideImpactAngularVelocity();
        float sideImpactSpeed = Mathf.Max(0f, -Vector3.Dot(horizontalIncomingVelocity, horizontalNormal));
        float incidence = sideImpactSpeed / Mathf.Max(0.1f, horizontalIncomingVelocity.magnitude);
        if (sideImpactSpeed < m_sideImpactMinimumSpeed || incidence < m_sideHitMinIncidence)
        {
            return;
        }

        m_sideImpactNormal = horizontalNormal;
        m_sideImpactIncidence = incidence;
        m_sideImpactStabilityTime = Mathf.Min(m_sideImpactStabilityDuration, m_sideImpactReleaseDuration);
        LimitSideImpactRebound();
        LimitSideImpactAngularVelocity();
    }

    // 壁接触後に逆ハンドルと反対へ回り続ける車体を短時間だけ減衰する関数
    void ApplyWallSpinRecovery()
    {
        if (!m_collisionStabilityEnabled || m_isPullUp || GroundedWheelCount < 2)
        {
            return;
        }

        // 接触してからの経過時間で補助を終えるための秒数
        float elapsed = Time.fixedTime - m_wallSpinContactTime;
        if (elapsed < 0f || elapsed > m_wallSpinRecoverySeconds)
        {
            return;
        }

        // 通常のTrack旋回で許す横滑り幅を超えた時だけ接触後の回復を補助する
        Vector3 velocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
        if (Mathf.Abs(velocity.z) < 1f)
        {
            return;
        }

        // 車体の向きと実際の移動方向のずれを判定する角度
        float slip = Mathf.Abs(Mathf.Atan2(velocity.x, Mathf.Abs(velocity.z)) * Mathf.Rad2Deg);
        // モード切替の途中でも通常旋回の許容幅を連続的に変える割合
        float track = m_differential != null ? m_differential.TrackHandlingBlend : 0f;
        // ESCと同じ基準で接触後の滑りが大きいかを判定する角度
        float threshold = m_escSlipAngleThreshold * Mathf.Lerp(1f, m_trackESCSlipThresholdScale, track);
        // 許容角度を超えるほど回復補助を強める割合
        float slipAmount = Mathf.InverseLerp(threshold, threshold * m_escFullSlipMultiplier, slip);
        // 切り足している最中は補助せずプレイヤーが切り戻した方向と逆の回転だけを弱める
        float yaw = Vector3.Dot(m_rigidbody.angularVelocity, Vector3.up);
        // 実際の舵角と進行方向から運転者が曲がりたい向きを判定する回転速度
        float desiredYaw = velocity.z * Mathf.Tan(m_steering.CurrentCenterAngle * Mathf.Deg2Rad) / m_escWheelBase;
        if (yaw * desiredYaw > 0f)
        {
            return;
        }

        // 接触から離れるほど回復補助を滑らかに終了する割合
        float fade = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, m_wallSpinRecoverySeconds));
        // 滑り量と経過時間に既存の展示用補助設定を反映する強さ
        float strength = slipAmount * fade * Mathf.Clamp01(m_collisionGameplayAssist);
        // 逆方向へ押し戻さず不要な回転だけを減らした角速度
        float limitedYaw = CalculateWallSpinDecay(yaw, strength, Time.fixedDeltaTime, m_wallSpinDampingSeconds);
        m_rigidbody.angularVelocity += Vector3.up * (limitedYaw - yaw);
    }

    // 更新間隔に依存せず回転量を零へ近づけ逆向きの回転を加えない関数
    static float CalculateWallSpinDecay(float _yaw, float _strength, float _deltaTime, float _responseSeconds)
    {
        return _yaw * Mathf.Exp(-Mathf.Clamp01(_strength) * Mathf.Max(0f, _deltaTime) / Mathf.Max(0.01f, _responseSeconds));
    }

    // 停車中も接触している壁面の法線を保持して非正面衝突から再発進できるようにする関数
    void RememberRestingWallContact(Collision _collision)
    {
        float gearDirection = m_mission.CurrentGearRatio == 0f ? 1f : Mathf.Sign(m_mission.CurrentGearRatio);
        Vector3 driveDirection = transform.forward * gearDirection;
        Vector3 selectedNormal = Vector3.zero;
        float selectedScore = float.NegativeInfinity;
        for (int index = 0; index < _collision.contactCount; index++)
        {
            Vector3 rawContactNormal = _collision.GetContact(index).normal;
            if (Mathf.Abs(Vector3.Dot(rawContactNormal, Vector3.up)) >= 0.55f)
            {
                continue;
            }

            Vector3 horizontalNormal = Vector3.ProjectOnPlane(rawContactNormal, Vector3.up);
            if (horizontalNormal.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            horizontalNormal.Normalize();
            float score = -Vector3.Dot(driveDirection, horizontalNormal);
            if (score <= selectedScore)
            {
                continue;
            }

            selectedScore = score;
            selectedNormal = horizontalNormal;
        }

        if (selectedNormal.sqrMagnitude > 0.0001f)
        {
            RememberWallContact(selectedNormal);
        }
    }

    // 壁面の外向き法線と接触時刻を低速停止からの復帰判定へ保存する関数
    void RememberWallContact(Vector3 _wallNormal)
    {
        if (_wallNormal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        m_wallContactNormal = _wallNormal.normalized;
        m_lastWallContactTime = Time.fixedTime;
    }

    // 壁で停車した時に位置を飛ばさず運転者の操舵方向へ少しずつ復帰する関数
    void ApplyWallStuckRecovery()
    {
        // 接触を離れた後に古い壁から押し出さないための有効時間
        bool recentContact = Time.fixedTime - m_lastWallContactTime <= Time.fixedDeltaTime * 2f;
        // ニュートラルとブレーキ中は自動復帰させない
        bool canRecover = m_wallStuckRecoveryEnabled && recentContact && m_mission.CurrentGearRatio != 0f && m_accelInput >= m_wallStuckMinimumAccel && m_brakeInput <= 0.01f && m_kph <= m_wallStuckMaximumSpeedKph;
        if (!canRecover)
        {
            m_wallStuckTime = 0f;
            return;
        }

        m_wallStuckTime += Time.fixedDeltaTime;
        if (m_wallStuckTime < m_wallStuckRecoveryDelay)
        {
            return;
        }

        // 正面への押し付けでは運転者が舵を切った時だけ脱出を補助する
        bool steeringRequested = Mathf.Abs(m_steerInput) >= 0.2f;
        if (!m_wallContactAllowsSlide && !steeringRequested)
        {
            return;
        }

        // 現在のギアと操舵から壁沿いの移動方向を求める
        float direction = Mathf.Sign(m_mission.CurrentGearRatio);
        Vector3 requestedDirection = transform.forward * direction;
        if (steeringRequested)
        {
            requestedDirection += transform.right * m_steerInput * direction;
        }

        Vector3 tangent = Vector3.ProjectOnPlane(requestedDirection, m_wallContactNormal).normalized;
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        // 復帰速度へ瞬間的に書き換えず、加速度上限の範囲で近づける
        float speed = Vector3.Dot(m_rigidbody.linearVelocity, tangent);
        float targetSpeed = m_wallStuckReleaseSpeedKph / 3.6f;
        float deltaSpeed = Mathf.Clamp(targetSpeed - speed, 0f, m_wallAssistAcceleration * Time.fixedDeltaTime);
        ApplyBudgetedWallAssist(tangent, deltaSpeed);
    }

    // 接触が続く同じ壁面に限り加速度上限内で壁沿いの走行を補助する関数
    void ApplyWallFollowVelocitySupport()
    {
        // 壁から離れた時やペダルを離した時には過去の衝突速度を復元しない
        bool recentContact = Time.fixedTime - m_lastWallContactTime <= Time.fixedDeltaTime * 2f;
        if (!recentContact || m_wallFollowSupportTime <= 0f || m_accelInput <= 0.03f || m_brakeInput > 0.01f)
        {
            m_wallFollowSupportTime = 0f;
            m_wallFollowMinimumSpeed = 0f;
            return;
        }

        // 壁の向きが変わった場合にも現在の接触面へ沿う方向だけ使用する
        Vector3 tangent = Vector3.ProjectOnPlane(m_wallFollowDirection, m_wallContactNormal).normalized;
        if (tangent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        m_wallFollowSupportTime = Mathf.Max(0f, m_wallFollowSupportTime - Time.fixedDeltaTime);
        m_wallFollowMinimumSpeed = Mathf.Max(0f, m_wallFollowMinimumSpeed - m_wallFollowDeceleration * Time.fixedDeltaTime);
        // 速度差を全量加算せず、穏やかな駆動補助として上限を設ける
        float tangentSpeed = Vector3.Dot(m_rigidbody.linearVelocity, tangent);
        float deltaSpeed = Mathf.Clamp(m_wallFollowMinimumSpeed - tangentSpeed, 0f, m_wallAssistAcceleration * Time.fixedDeltaTime);
        ApplyBudgetedWallAssist(tangent, deltaSpeed);
    }

    // 衝突角度とギア方向から壁接触中の駆動トルク倍率を返す関数
    float GetSideImpactDriveTorqueRatio()
    {
        if (m_sideImpactStabilityTime <= 0f || m_sideImpactNormal.sqrMagnitude <= 0.0001f || m_mission.CurrentGearRatio == 0f || m_sideImpactIncidence < m_sideHitTorqueCutThreshold)
        {
            return 1f;
        }

        Vector3 driveDirection = transform.forward * Mathf.Sign(m_mission.CurrentGearRatio);
        if (Vector3.Dot(driveDirection, m_sideImpactNormal) >= -0.35f)
        {
            return 1f;
        }

        // 浅い接触では駆動力を残し、正面に近い衝突だけ壁登り防止を強くする
        float impactSeverity = Mathf.InverseLerp(m_sideHitTorqueCutThreshold, 1f, m_sideImpactIncidence);
        return Mathf.Lerp(1f, m_sideImpactDriveTorqueRatio, impactSeverity);
    }

    // 壁へ向かう駆動中で衝突角度に応じた駆動制限が必要か返す関数
    bool IsDrivingIntoSideImpact()
    {
        return GetSideImpactDriveTorqueRatio() < 0.999f;
    }

    // 物理解決後の速度を基準に減速し衝突前の速度を瞬間的に復元しない関数
    void ApplyImpactAngleSpeedResponse(Collision _collision, bool _applyInitialImpactLoss)
    {
        if (!m_collisionStabilityEnabled || m_rigidbody == null || _collision.contactCount == 0)
        {
            return;
        }

        // 衝突前の速度は角度の判定と補助目標のみに使用する
        Vector3 incoming = Vector3.ProjectOnPlane(GetVelocityBeforeImpact(), Vector3.up);
        Vector3 normal = GetImpactNormal(_collision, incoming);
        if (normal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        RememberWallContact(normal);
        // 運転者が壁から離れ始めた後は、その移動まで壁沿いへ押し戻さない
        if (Vector3.Dot(incoming, normal) > 0f)
        {
            return;
        }

        // 法線速度と接線速度を分け、正面に近いほど進行速度が減るようにする
        float speed = incoming.magnitude;
        float incidence = Mathf.Clamp01(-Vector3.Dot(incoming, normal) / Mathf.Max(0.1f, speed));
        Vector3 incomingTangent = Vector3.ProjectOnPlane(incoming, normal);
        Vector3 current = m_rigidbody.linearVelocity;
        Vector3 currentHorizontal = Vector3.ProjectOnPlane(current, Vector3.up);
        Vector3 currentTangent = Vector3.ProjectOnPlane(currentHorizontal, normal);
        // 接線方向が反転した接触では過去の進行方向へ高速で押し戻さない
        if (Vector3.Dot(currentTangent, incomingTangent) < 0f)
        {
            currentTangent = Vector3.zero;
        }

        float severity = Mathf.InverseLerp(m_glancingImpactIncidence, m_directImpactIncidence, incidence);
        float retention = _applyInitialImpactLoss ? Mathf.Lerp(m_glancingImpactSpeedRetention, m_directImpactSpeedRetention, severity) : 1f;
        currentTangent = Vector3.ClampMagnitude(currentTangent * retention, Mathf.Min(speed, currentHorizontal.magnitude));
        m_rigidbody.linearVelocity = currentTangent + Vector3.up * current.y;
        // 中央への正面衝突と外側の接触を実際の接触位置で区別する
        Vector3 contactPoint = _collision.GetContact(0).point;
        for (int index = 0; index < _collision.contactCount; index++)
        {
            ContactPoint contact = _collision.GetContact(index);
            if (Vector3.Dot(contact.normal, normal) > 0.9f)
            {
                contactPoint = contact.point;
                break;
            }
        }

        float travelSign = Vector3.Dot(incoming, transform.forward) >= 0f ? 1f : -1f;
        bool frontal = incidence >= m_frontalStopIncidence && IsCentralFrontalContact(contactPoint, normal, travelSign);
        m_wallContactAllowsSlide = !frontal;
        m_wallEscapeDirection = incomingTangent.normalized;
        // 同時接触のたびに最大値を引き継がず、現在の接線速度を基準に補助目標を更新する
        m_wallFollowDirection = incomingTangent.normalized;
        m_wallFollowMinimumSpeed = frontal ? 0f : incomingTangent.magnitude * m_collisionGameplayAssist;
        m_wallFollowMaximumSpeed = speed;
        m_wallFollowSupportTime = frontal ? 0f : m_wallFollowSupportDuration;
    }

    // 接触位置が車体中央の正面衝突範囲に入っているかを返す関数
    bool IsCentralFrontalContact(Vector3 _worldContactPoint, Vector3 _collisionNormal, float _travelSign)
    {
        // 接触位置を車体基準へ変換してヘッドライト外側や車体中央の横接触を除外する
        Vector3 localContactPoint = transform.InverseTransformPoint(_worldContactPoint);
        float directionalLongitudinalOffset = localContactPoint.z * _travelSign;
        Vector3 vehicleTravelDirection = transform.forward * _travelSign;
        float normalAlignment = Mathf.Max(0f, -Vector3.Dot(vehicleTravelDirection, _collisionNormal.normalized));
        return Mathf.Abs(localContactPoint.x) <= m_frontStopMaxSideOffset && directionalLongitudinalOffset >= m_frontStopMinForwardOffset && normalAlignment >= m_frontStopNormalThreshold;
    }

    // 後退ギア比とタイヤ半径から現在の後退速度上限とリミッター状態を更新する関数
    void UpdateReverseSpeedLimiter()
    {
        float theoreticalMaximumSpeed = m_mission.GetTheoreticalReverseMaximumSpeedKph(m_wheelController.WheelRadius, m_engine.OverRevRPM) * m_reverseMaximumSpeedRatio;
        m_reverseMaximumSpeedKph = Mathf.Min(theoreticalMaximumSpeed, m_reverseSpeedLimitKph);
        if (m_mission.ActiveGear >= 0)
        {
            m_reverseLimiterActive = false;
            return;
        }

        // 車体前方を基準にした現在の後退速度
        float reverseSpeedKph = Mathf.Max(0f, -Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f);
        // 上限付近でリミッターが点滅しないよう解除幅を含めた作動速度
        float activationSpeed = m_reverseLimiterActive ? m_reverseMaximumSpeedKph - m_reverseLimiterReleaseKph : m_reverseMaximumSpeedKph;
        m_reverseLimiterActive = reverseSpeedKph >= activationSpeed;
    }

    // 後退中の車体速度を理論上限以内へ収めて坂道でも過剰に加速しないようにする関数
    void LimitReverseSpeed()
    {
        if (m_mission.ActiveGear >= 0 || m_reverseMaximumSpeedKph <= 0f)
        {
            return;
        }

        // 車体方向を基準にした現在の速度
        Vector3 localVelocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
        // 後退速度上限を物理演算用のm/sへ変換した値
        float maximumReverseSpeed = m_reverseMaximumSpeedKph / 3.6f;
        if (localVelocity.z >= -maximumReverseSpeed)
        {
            return;
        }

        localVelocity.z = -maximumReverseSpeed;
        m_rigidbody.linearVelocity = transform.TransformDirection(localVelocity);
    }

    // 衝突判定で使う物理更新直前の車体速度を返す関数
    Vector3 GetVelocityBeforeImpact()
    {
        return m_hasPreStepVelocity ? m_preStepVelocity : m_rigidbody.linearVelocity;
    }

    // 壁から離れる方向と上方向の速度を制限して車体が跳ね上がることを防ぐ関数
    void LimitSideImpactRebound()
    {
        if (m_rigidbody == null || m_sideImpactNormal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        // 衝突前から壁を離れる方向へ動いている時は古い衝突制限を継続しない
        if (Vector3.Dot(GetVelocityBeforeImpact(), m_sideImpactNormal) > 0f)
        {
            return;
        }

        LimitCollisionRebound(m_sideImpactNormal);
    }

    // 接触角度に関係なく壁から離れる速度と上方向速度を上限内へ収める関数
    void LimitCollisionRebound(Vector3 _collisionNormal)
    {
        if (m_rigidbody == null || _collisionNormal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 limitedVelocity = m_rigidbody.linearVelocity;
        float reboundSpeed = Vector3.Dot(limitedVelocity, _collisionNormal);
        if (reboundSpeed > m_sideHitMaxBounceSpeed)
        {
            limitedVelocity -= _collisionNormal * (reboundSpeed - m_sideHitMaxBounceSpeed);
        }

        // 旧制限は上り坂を進む速度まで削り壁接触中に車体を路面へ押し込んでいた
        // 路面に沿って上る分を残し衝突で余分に増えた上向き速度だけを制限する
        limitedVelocity.y = Mathf.Min(limitedVelocity.y, GetSideImpactUpwardLimit(limitedVelocity));
        m_rigidbody.linearVelocity = limitedVelocity;
    }

    // 同じ物理更新の全壁面をまとめて扱い後続コールバックによる再反発を防ぐ関数
    void LimitContactRebound(Collision _collision)
    {
        if (!m_collisionStabilityEnabled || m_rigidbody == null || _collision == null)
        {
            return;
        }

        // 別の物理更新では前回の壁面と計測値を持ち越さない
        if (m_contactWallFixedTime != Time.fixedTime)
        {
            m_contactWallFixedTime = Time.fixedTime;
            m_contactWallNormalCount = 0;
            ContactReboundBeforeKph = 0f;
            ContactReboundAfterKph = 0f;
            ContactExcessReboundKph = 0f;
        }

        // 床や天井を除き車体側へ向く水平な壁法線を収集する
        for (int index = 0; index < _collision.contactCount; index++)
        {
            Vector3 rawNormal = _collision.GetContact(index).normal;
            if (Mathf.Abs(Vector3.Dot(rawNormal, Vector3.up)) >= m_wallNormalUpLimit)
            {
                continue;
            }

            Vector3 normal = Vector3.ProjectOnPlane(rawNormal, Vector3.up).normalized;
            bool duplicate = false;
            for (int n = 0; n < m_contactWallNormalCount; n++)
            {
                if (Vector3.Dot(normal, m_contactWallNormals[n]) > 0.999f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate && m_contactWallNormalCount < m_contactWallNormals.Length)
            {
                m_contactWallNormals[m_contactWallNormalCount++] = normal;
            }
        }

        if (m_contactWallNormalCount == 0)
        {
            return;
        }

        // 制限前と制限後を毎物理更新で計測し、一秒ログの隙間の反発も検出する
        Vector3 velocity = m_rigidbody.linearVelocity;
        ContactReboundBeforeKph = Mathf.Max(ContactReboundBeforeKph, GetMaximumContactRebound(velocity) * 3.6f);
        for (int pass = 0; pass < m_contactProjectionPasses; pass++)
        {
            for (int index = 0; index < m_contactWallNormalCount; index++)
            {
                Vector3 normal = m_contactWallNormals[index];
                float outwardSpeed = Vector3.Dot(velocity, normal);
                // 正常な離脱速度は残すが、離脱中の再接触による大きな反発も通さない
                float allowedSpeed = GetAllowedContactOutwardSpeed(normal);
                if (outwardSpeed > allowedSpeed)
                {
                    velocity -= normal * (outwardSpeed - allowedSpeed);
                }
            }
        }

        // 旧制限は壁を擦りながら上るために必要な速度まで削るため使用しない
        // 水平な反発制限を変えず路面の勾配に沿った上向き速度を残す
        velocity.y = Mathf.Min(velocity.y, GetSideImpactUpwardLimit(velocity));
        m_rigidbody.linearVelocity = velocity;
        ContactReboundAfterKph = GetMaximumContactRebound(velocity) * 3.6f;
        for (int index = 0; index < m_contactWallNormalCount; index++)
        {
            Vector3 normal = m_contactWallNormals[index];
            float excess = Vector3.Dot(velocity, normal) - GetAllowedContactOutwardSpeed(normal);
            ContactExcessReboundKph = Mathf.Max(ContactExcessReboundKph, excess * 3.6f);
        }
    }

    // 接地中は道路勾配を考慮し空中では従来の跳ね上がり上限を返す関数
    float GetSideImpactUpwardLimit(Vector3 _velocity)
    {
        if (m_wheelController == null || !m_wheelController.TryGetSupportNormal(out Vector3 normal))
        {
            return m_sideHitMaxRiseSpeed;
        }

        return CalculateRoadUpwardLimit(_velocity, normal, m_sideHitMaxRiseSpeed);
    }

    // 道路に沿う上昇速度と許容する跳ね上がり速度から上限を求める関数
    static float CalculateRoadUpwardLimit(Vector3 _velocity, Vector3 _normal, float _reboundAllowance)
    {
        // 壁に近い面では勾配計算の割り算が過大にならないよう従来上限を使う
        const float minimumRoadNormalY = 0.5f;
        if (_normal.y <= minimumRoadNormalY)
        {
            return _reboundAllowance;
        }

        // 路面接線の条件である速度と法線の内積0から必要な上昇速度を求める
        float roadUpwardSpeed = -(_velocity.x * _normal.x + _velocity.z * _normal.z) / _normal.y;
        // 下り坂へ強制的に押し付ける力は加えず上り坂で必要な速度だけを許す
        return Mathf.Max(0f, roadUpwardSpeed) + _reboundAllowance;
    }

    // 衝突前からの離脱速度と穏やかな加速分だけを反発制限から除外する関数
    float GetAllowedContactOutwardSpeed(Vector3 _normal)
    {
        float previousOutwardSpeed = Mathf.Max(0f, Vector3.Dot(GetVelocityBeforeImpact(), _normal));
        float controlledSpeed = previousOutwardSpeed + m_wallAssistAcceleration * Time.fixedDeltaTime;
        return Mathf.Max(m_sideHitMaxBounceSpeed, controlledSpeed);
    }

    // 保存した各接触面から離れる最大速度を返す関数
    float GetMaximumContactRebound(Vector3 _velocity)
    {
        // 異なる壁面の中で最も大きい外向き成分を検証へ渡す
        float maximum = 0f;
        for (int index = 0; index < m_contactWallNormalCount; index++)
        {
            maximum = Mathf.Max(maximum, Vector3.Dot(_velocity, m_contactWallNormals[index]));
        }

        return maximum;
    }

    // 衝突直後のピッチとロールを抑えて車体とカメラの大揺れを防ぐ関数
    void LimitSideImpactAngularVelocity()
    {
        if (m_rigidbody == null)
        {
            return;
        }

        Vector3 localAngularVelocity = transform.InverseTransformDirection(m_rigidbody.angularVelocity);
        localAngularVelocity.x = Mathf.Clamp(localAngularVelocity.x, -m_sideImpactMaximumPitchSpeed, m_sideImpactMaximumPitchSpeed);
        localAngularVelocity.y = Mathf.Clamp(localAngularVelocity.y, -m_sideImpactMaximumYawSpeed, m_sideImpactMaximumYawSpeed);
        localAngularVelocity.z = Mathf.Clamp(localAngularVelocity.z, -m_sideImpactMaximumRollSpeed, m_sideImpactMaximumRollSpeed);
        localAngularVelocity.x = Mathf.MoveTowards(localAngularVelocity.x, 0f, m_sideImpactPitchDamping * Time.fixedDeltaTime);
        localAngularVelocity.z = Mathf.MoveTowards(localAngularVelocity.z, 0f, m_sideImpactRollDamping * Time.fixedDeltaTime);
        m_rigidbody.angularVelocity = transform.TransformDirection(localAngularVelocity);
    }

    // 実行中に生成した車体用物理マテリアルを破棄する関数
    void OnDestroy()
    {
        if (m_bodyCollisionMaterial != null)
        {
            Destroy(m_bodyCollisionMaterial);
        }
    }

    // 接地を残したまま壁へ衝突した車体の過剰なロール回転を減衰する関数
    void ApplySideImpactStability()
    {
        if (m_sideImpactStabilityTime <= 0f || m_rigidbody == null)
        {
            return;
        }

        m_sideImpactStabilityTime = Mathf.Max(0f, m_sideImpactStabilityTime - Time.fixedDeltaTime);
        LimitSideImpactRebound();
        LimitSideImpactAngularVelocity();
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle <= 1f)
        {
            return;
        }

        // ピッチとヨーを変えず車体のロールだけを緩やかに水平へ戻す
        float rollError = Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);
        float limitedRollError = Mathf.Clamp(rollError, -m_sideHitMaxTurnAngle, m_sideHitMaxTurnAngle);
        float uprightAcceleration = limitedRollError * Mathf.Deg2Rad * m_sideImpactUprightStrength;
        m_rigidbody.AddTorque(transform.forward * uprightAcceleration, ForceMode.Acceleration);
    }
}
