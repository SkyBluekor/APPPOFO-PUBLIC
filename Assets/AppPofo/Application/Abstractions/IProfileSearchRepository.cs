using System.Collections.Generic;
using AppPofo.Domain.Entities;

namespace AppPofo.Application.Abstractions
{
    // Optional alongside IProfileRepository to preserve existing local-memory fakes.
    public interface IProfileSearchRepository
    {
        ProfileSearchPage Search(string name, IReadOnlyList<string> skills, int limit, int offset);
    }

    public sealed class ProfileSearchPage
    {
        public IReadOnlyList<Profile> Profiles { get; }
        public int TotalCount { get; }

        public ProfileSearchPage(IReadOnlyList<Profile> profiles, int totalCount)
        {
            Profiles = profiles ?? throw new System.ArgumentNullException(nameof(profiles));
            if (totalCount < 0) throw new System.ArgumentOutOfRangeException(nameof(totalCount));
            TotalCount = totalCount;
        }
    }
}
