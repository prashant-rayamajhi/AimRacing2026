#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

//Mapの車両へ結合用WheelCollider構成を設定するクラス
static class AimRacingVehicleMergeInstaller
{
    //結合対象のMapシーンを識別するためのパス
    const string MapScenePath = "Assets/!Scenes/Map.unity";
    //車輪制御オブジェクトを重複作成しないための名前
    const string WheelControllerName = "WheelController";
    //GRヤリスの車輪半径をWheelColliderへ設定する値
    const float WheelRadius = 0.334f;
    //サスペンションの可動距離をWheelColliderへ設定する値
    const float SuspensionDistance = 0.13f;
    //サスペンションのばね強度をWheelColliderへ設定する値
    const float SuspensionSpring = 60000f;
    //サスペンションの減衰力をWheelColliderへ設定する値
    const float SuspensionDamper = 7800f;
    //静止時のサスペンション位置をWheelColliderへ設定する値
    const float SuspensionTarget = 0.30f;
    //接地点へ力を伝える高さをWheelColliderへ設定する値
    const float ForceApplicationDistance = 0.08f;
    //前右、前左、後右、後左のWheelCollider位置
    static readonly Vector3[] WheelPositions =
    {
        new Vector3(0.791089f, -0.15196148f, 1.480806f),
        new Vector3(-0.7977507f, -0.15196148f, 1.4808059f),
        new Vector3(0.79108894f, -0.1519615f, -1.0791938f),
        new Vector3(-0.7977512f, -0.15196145f, -1.0791949f)
    };
    //前右、前左、後右、後左をHierarchyで識別する名前
    static readonly string[] WheelNames = { "WheelCollider_FR", "WheelCollider_FL", "WheelCollider_RR", "WheelCollider_RL" };

    //不足しているWheelController2026と4輪WheelColliderだけをMapへ追加する関数
    [MenuItem("Tools/AimRacing/Apply Vehicle Behavior Merge")]
    static void ApplyVehicleMerge()
    {
        if (Application.isPlaying) { return; }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != MapScenePath)
        {
            Debug.LogError("Mapシーンを開いてから車両挙動の結合を実行してください。");
            return;
        }
        VehicleController vehicle = Object.FindFirstObjectByType<VehicleController>(FindObjectsInactive.Include);
        if (vehicle == null)
        {
            Debug.LogError("Map内にVehicleControllerが見つかりません。");
            return;
        }

        Transform wheelControllerTransform = vehicle.transform.Find(WheelControllerName);
        if (wheelControllerTransform == null)
        {
            GameObject wheelControllerObject = new GameObject(WheelControllerName);
            Undo.RegisterCreatedObjectUndo(wheelControllerObject, "Create WheelController");
            wheelControllerTransform = wheelControllerObject.transform;
            wheelControllerTransform.SetParent(vehicle.transform, false);
        }
        if (wheelControllerTransform.GetComponent<WheelController2026>() == null)
        {
            Undo.AddComponent<WheelController2026>(wheelControllerTransform.gameObject);
        }

        for (int index = 0; index < WheelNames.Length; index++)
        {
            Transform wheelTransform = wheelControllerTransform.Find(WheelNames[index]);
            if (wheelTransform == null)
            {
                GameObject wheelObject = new GameObject(WheelNames[index]);
                Undo.RegisterCreatedObjectUndo(wheelObject, "Create WheelCollider");
                wheelTransform = wheelObject.transform;
                wheelTransform.SetParent(wheelControllerTransform, false);
            }
            wheelTransform.localPosition = WheelPositions[index];
            WheelCollider wheel = wheelTransform.GetComponent<WheelCollider>();
            if (wheel == null) { wheel = Undo.AddComponent<WheelCollider>(wheelTransform.gameObject); }
            ConfigureWheelCollider(wheel);
        }

        WheelController2024[] legacyWheels = vehicle.GetComponentsInChildren<WheelController2024>(true);
        foreach (WheelController2024 legacyWheel in legacyWheels)
        {
            Undo.RecordObject(legacyWheel, "Disable Legacy WheelController");
            legacyWheel.enabled = false;
            EditorUtility.SetDirty(legacyWheel);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("MapへWheelController2026と4輪WheelColliderを結合しました。Ctrl+Sで保存してください。", vehicle);
    }

    //NewAimRacingと同じサスペンションとタイヤ摩擦をWheelColliderへ設定する関数
    static void ConfigureWheelCollider(WheelCollider wheel)
    {
        Undo.RecordObject(wheel, "Configure WheelCollider");
        wheel.radius = WheelRadius;
        wheel.suspensionDistance = SuspensionDistance;
        wheel.forceAppPointDistance = ForceApplicationDistance;
        wheel.mass = 20f;
        wheel.wheelDampingRate = 0.35f;
        JointSpring spring = wheel.suspensionSpring;
        spring.spring = SuspensionSpring;
        spring.damper = SuspensionDamper;
        spring.targetPosition = SuspensionTarget;
        wheel.suspensionSpring = spring;
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.extremumSlip = 0.18f;
        forward.extremumValue = 1.15f;
        forward.asymptoteSlip = 0.55f;
        forward.asymptoteValue = 0.9f;
        forward.stiffness = 1.65f;
        wheel.forwardFriction = forward;
        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.extremumSlip = 0.14f;
        sideways.extremumValue = 1.15f;
        sideways.asymptoteSlip = 0.4f;
        sideways.asymptoteValue = 0.92f;
        sideways.stiffness = 2.1f;
        wheel.sidewaysFriction = sideways;
        EditorUtility.SetDirty(wheel);
    }
}
#endif
