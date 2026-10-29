using System;

namespace AppPofo.Application.Interfaces
{
    /// <summary>
    /// 애플리케이션 계층이 파일 시스템과 플랫폼을 알지 않도록 분리한 아바타 저장소 계약입니다.
    /// </summary>
    public interface IAvatarStorage
    {
        /// <summary>이미지를 저장하고 프로필에 기록할 영구 경로를 반환합니다.</summary>
        string Save(
            long profileId,
            byte[] imagesBytes
        );

        /// <summary>경로가 비어 있거나 파일이 없으면 <see langword="null"/>을 반환합니다.</summary>
        byte[] Load(string avatarPath);

        /// <summary>경로가 비어 있거나 파일이 없어도 실패하지 않습니다.</summary>
        void Delete(string avatarPath);
    }
}
