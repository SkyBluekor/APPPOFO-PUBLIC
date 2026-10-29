using AppPofo.Application.Interfaces;

// 이 코드는 Profile에 저장된 AvatarPath를 이용하여
// 실제 아바타 이미지 데이터를 저장소에서 읽어오는 UseCase입니다.

namespace AppPofo.Application.UseCases
{
    public sealed class LoadAvatarUseCase
    {
        private readonly IAvatarStorage avatarStorage;

        public LoadAvatarUseCase(IAvatarStorage avatarStorage)
        {
            this.avatarStorage = avatarStorage;
        }

        public byte[] Execute(string avatarPath)
        {
            if(string.IsNullOrWhiteSpace(avatarPath))
                return null;

            return avatarStorage.Load(avatarPath);
        }
    }
}