using System;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class GetProfileUsecase
    {
        private readonly IProfileRepository _profileRepository;

        public GetProfileUsecase(IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository ?? throw new ArgumentException(nameof(profileRepository));
        }

        public Profile Execute(long profileId)
        {
            if(profileId <= 0)
                throw new ArgumentOutOfRangeException(nameof(profileId));
            
            return _profileRepository.Find(profileId);
        }
    }
}