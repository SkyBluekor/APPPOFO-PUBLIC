using System;
using UnityEngine;

namespace AppPofo.Presentation.Avatar
{
    /// <summary>
    /// 플랫폼별 이미지 선택 및 카메라 구현을 프레젠테이션 계층에 노출하는 계약입니다.
    /// </summary>
    public interface IAvatarImageSource
    {
        /// <summary>카메라 스트림의 재생 여부입니다. 촬영 가능 여부는 보장하지 않습니다.</summary>
        bool IsCameraRunning { get; }

        /// <summary>유효한 영상 프레임을 받아 촬영할 수 있는 상태인지 나타냅니다.</summary>
        bool IsCameraReady {get;}

        /// <summary>플랫폼의 이미지 선택 UI를 열고 선택된 파일의 인코딩 바이트를 반환합니다.</summary>
        void PickImage(
            Action<byte[]> onSuccess,
            Action<string> onFailure);

        /// <summary>첫 유효 프레임을 받은 뒤 미리보기 텍스처를 반환합니다.</summary>
        void StartCamera(
            Action<Texture> onPreviewReady,
            Action<string> onFailure);

        /// <summary>현재 카메라 프레임을 인코딩된 이미지 바이트로 반환합니다.</summary>
        void CaptureImage(
            Action<byte[]> onSuccess,
            Action<string> onFailure);

        /// <summary>카메라 스트림과 관련 리소스를 해제합니다.</summary>
        void StopCamera();
    }
}
