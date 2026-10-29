using System.Collections.Generic;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.Abstractions
{
    public interface IProfileRepository
    {
        Profile Find(long profileId);
        IReadOnlyList<Profile> GetAll();
        Profile Save(Profile profile);
        void Delete(long profileId);

    }
}
