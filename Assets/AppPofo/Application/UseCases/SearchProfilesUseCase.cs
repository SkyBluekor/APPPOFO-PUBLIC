using System;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;

namespace AppPofo.Application.UseCases
{
    /// <summary>Queries only one page of profiles; multiple technology filters use AND.</summary>
    public sealed class SearchProfilesUseCase
    {
        public const int DefaultPageSize = 12;
        public const int MaxPageSize = 24;
        private readonly IProfileSearchRepository searchRepository;

        public SearchProfilesUseCase(IProfileSearchRepository searchRepository)
        {
            this.searchRepository = searchRepository ??
                throw new ArgumentNullException(nameof(searchRepository));
        }

        public ProfileSearchPage Execute(
            string name, string commaSeparatedSkills, int pageIndex = 0,
            int pageSize = DefaultPageSize)
        {
            if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
            if (pageSize < 1 || pageSize > MaxPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            var skills = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(commaSeparatedSkills))
            {
                foreach (string raw in commaSeparatedSkills.Split(','))
                {
                    string skill = raw.Trim();
                    if (skill.Length == 0 || !seen.Add(skill)) continue;
                    skills.Add(skill);
                    if (skills.Count > 8)
                        throw new ArgumentException("최대 8개 기술까지 검색할 수 있습니다.",
                            nameof(commaSeparatedSkills));
                }
            }

            int offset = checked(pageIndex * pageSize);
            return searchRepository.Search(name?.Trim() ?? string.Empty, skills, pageSize, offset);
        }
    }
}
