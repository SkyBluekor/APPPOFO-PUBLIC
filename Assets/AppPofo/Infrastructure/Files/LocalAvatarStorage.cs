using System;
using System.IO;
using AppPofo.Application.Interfaces;
using UnityEngine;

namespace AppPofo.Infrastructure.Files
{
    /// <summary>
    /// Unity가 플랫폼별로 보장하는 영구 데이터 경로에 아바타 파일을 저장합니다.
    /// </summary>
    public sealed class LocalAvatarStorage : IAvatarStorage
    {
        private const string AvatarFolderName = "Avatars"   ;

        public string Save(
            long profileId,
            byte[] imageBytes
        )
        {
            if(profileId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId));
            }

            if(imageBytes == null || imageBytes.Length == 0)
            {
                throw new ArgumentException("저장할 아바타 이미지가 없습니다.",nameof(imageBytes));
            }

            string avatarDirectory = Path.Combine(UnityEngine.Application.persistentDataPath,AvatarFolderName);

            if(!Directory.Exists(avatarDirectory))
            {
                Directory.CreateDirectory(avatarDirectory);
            }

            // 프로필별 고정 경로를 사용해 변경 시 이전 파일을 자연스럽게 교체합니다.
            string filepath = Path.Combine(avatarDirectory,$"profile_{profileId}.png");

            // 입력 형식과 관계없이 확장자와 실제 데이터가 일치하는 PNG로 정규화합니다.
            Texture2D texture = new Texture2D(2,2);

            try
            {
                if(!texture.LoadImage(imageBytes))
                {
                    throw new InvalidOperationException("아바타 이미지를 읽을 수 없습니다.");
                }

                byte[] pngBytes = texture.EncodeToPNG();

                if(pngBytes == null || pngBytes.Length == 0)
                {
                    throw new InvalidOperationException("PNG 이미지 변환에 실패했습니다.");
                }

                File.WriteAllBytes(filepath,pngBytes);

                Debug.Log($"[Avatar Storage] 아바타 저장 완료: {filepath}");

                return filepath;
            }
            finally
            {
                // Texture2D의 네이티브 메모리는 관리 코드의 GC만으로 해제되지 않습니다.
                UnityEngine.Object.Destroy(texture);
            }
        }

        public byte[] Load(string avatarPath)
        {
            if(string.IsNullOrWhiteSpace(avatarPath))
                return null;

            if (!File.Exists(avatarPath))
            {
                Debug.LogWarning(
                    $"[Avatar Storage] 아바타 파일을 찾을 수 없습니다: {avatarPath}");

                return null;
            }

            return File.ReadAllBytes(avatarPath);
        }
        public void Delete(string avatarPath)
        {
            if (string.IsNullOrWhiteSpace(avatarPath))
                return;

            // Never delete arbitrary files based on a path recorded in SQLite.
            // This storage owns only files underneath persistentDataPath/Avatars.
            string avatarDirectory = Path.GetFullPath(
                Path.Combine(UnityEngine.Application.persistentDataPath, AvatarFolderName));
            string absolutePath = Path.GetFullPath(avatarPath);
            string ownedPrefix = avatarDirectory.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!absolutePath.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"아바타 저장소 외부 파일은 삭제할 수 없습니다: {avatarPath}");

            if (!File.Exists(absolutePath))
                return;

            File.Delete(absolutePath);
            Debug.Log($"[Avatar Storage] 아바타 삭제 완료: {absolutePath}");
        }
    }
}
