using System;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.UseCases
{
    public sealed class GetProfilesUseCase
    {
        private readonly IProfileRepository _profileRepository;

        public GetProfilesUseCase(
            IProfileRepository profileRepository)
        {
            _profileRepository = profileRepository
                ?? throw new ArgumentNullException(
                    nameof(profileRepository));
        }

        public IReadOnlyList<Profile> Execute()
        {
            return _profileRepository.GetAll();
        }
    }
}