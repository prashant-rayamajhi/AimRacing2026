using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// 登録されたカメラをHomeキーで順番に切り替えるクラス
public class SwitchingCamera : MonoBehaviour
{
    // 待機中のカメラへ設定する優先度
    const int DefaultCameraPriority = 10;
    // 選択中のカメラへ設定する優先度
    const int ActiveCameraPriority = 11;
    // NewAimRacingと同じ初期視点として使用するカメラ名
    const string InitialGameplayCameraName = "Bumper";
    // 車体が傾いた時に画面の横傾きだけを水平へ戻す速さ
    [SerializeField, Range(1f, 30f)]
    float m_horizonLevelSpeed = 18f;
    // 旧型と新型の両方を登録できるCinemachineカメラの一覧
    [SerializeField]
    List<CinemachineVirtualCameraBase> virtualCameras = new List<CinemachineVirtualCameraBase>();
    // 現在有効になっているカメラの番号
    int currentIndex;
    // 最初のカメラを優先表示に設定する関数
    void Start()
    {
        ActivateGameplayCamera();
    }

    // Timeline終了後にゲーム用カメラへ確実に戻す関数
    public void ActivateGameplayCamera()
    {
        ResolveGameplayCameras();
        DisableLegacyVehiclePhysicsCameras();
        if (virtualCameras == null)
        {
            enabled = false;
            return;
        }

        virtualCameras.RemoveAll(camera => camera == null);
        if (virtualCameras.Count == 0)
        {
            enabled = false;
            AppLog.LogError("ゲーム用Cinemachineカメラが設定されていません。", this);
            return;
        }

        for (int index = 0; index < virtualCameras.Count; index++)
        {
            virtualCameras[index].Priority = DefaultCameraPriority;
        }

        currentIndex = virtualCameras.FindIndex(camera => camera.name == InitialGameplayCameraName);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        virtualCameras[currentIndex].Priority = ActiveCameraPriority;
        enabled = true;
    }

    // 車両に用意されたPOV・Bonnet・Bumperを名前から取得して切替順を統一する関数
    void ResolveGameplayCameras()
    {
        CinemachineVirtualCameraBase[] sceneCameras = FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<CinemachineVirtualCameraBase> resolvedCameras = new List<CinemachineVirtualCameraBase>();
        string[] gameplayCameraNames =
        {
            "POV",
            "Bonnet",
            "Bumper"
        };
        for (int nameIndex = 0; nameIndex < gameplayCameraNames.Length; nameIndex++)
        {
            for (int cameraIndex = 0; cameraIndex < sceneCameras.Length; cameraIndex++)
            {
                CinemachineVirtualCameraBase camera = sceneCameras[cameraIndex];
                if (camera == null || camera.name != gameplayCameraNames[nameIndex] || resolvedCameras.Contains(camera))
                {
                    continue;
                }

                resolvedCameras.Add(camera);
                break;
            }
        }

        if (virtualCameras != null)
        {
            for (int index = 0; index < virtualCameras.Count; index++)
            {
                CinemachineVirtualCameraBase camera = virtualCameras[index];
                if (camera == null || resolvedCameras.Contains(camera))
                {
                    continue;
                }

                resolvedCameras.Add(camera);
            }
        }

        virtualCameras = resolvedCameras;
    }

    // 旧カメラ追従処理がNaN座標を生成しないようCinemachineカメラ本体を残して制御だけ停止する関数
    void DisableLegacyVehiclePhysicsCameras()
    {
        for (int index = 0; index < virtualCameras.Count; index++)
        {
            CinemachineVirtualCameraBase camera = virtualCameras[index];
            if (camera == null || !camera.TryGetComponent(out VehiclePhysics.VPCameraController controller) || !controller.enabled)
            {
                continue;
            }

            controller.enabled = false;
        }
    }

    // Homeキー入力時に次のカメラへ切り替える関数
    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Home))
        {
            return;
        }

        virtualCameras.RemoveAll(camera => camera == null);
        if (virtualCameras.Count == 0)
        {
            enabled = false;
            return;
        }

        currentIndex = Mathf.Clamp(currentIndex, 0, virtualCameras.Count - 1);
        virtualCameras[currentIndex].Priority = DefaultCameraPriority;
        currentIndex = (currentIndex + 1) % virtualCameras.Count;
        virtualCameras[currentIndex].Priority = ActiveCameraPriority;
    }

    // 車体の進行方向を維持しながら選択中カメラの横傾きだけを水平へ戻す関数
    void LateUpdate()
    {
        if (virtualCameras == null || virtualCameras.Count == 0 || currentIndex < 0 || currentIndex >= virtualCameras.Count)
        {
            return;
        }

        CinemachineVirtualCameraBase activeCamera = virtualCameras[currentIndex];
        if (activeCamera == null)
        {
            return;
        }

        Vector3 forward = activeCamera.transform.forward;
        if (forward.sqrMagnitude <= 0.0001f || Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) >= 0.98f)
        {
            return;
        }

        Quaternion levelRotation = Quaternion.LookRotation(forward, Vector3.up);
        float levelAmount = 1f - Mathf.Exp(-m_horizonLevelSpeed * Time.deltaTime);
        activeCamera.transform.rotation = Quaternion.Slerp(activeCamera.transform.rotation, levelRotation, levelAmount);
    }
}
