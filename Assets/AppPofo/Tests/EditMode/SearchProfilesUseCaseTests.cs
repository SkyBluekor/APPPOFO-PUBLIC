using System;
using System.Collections.Generic;
using System.Linq;
using AppPofo.Application.Abstractions;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class SearchProfilesUseCaseTests
    {
        [Test]
        public void Search_NormalizesDuplicateSkillsAndTrimsName()
        {
            var store = new MemorySearch();
            new SearchProfilesUseCase(store).Execute("  김  ", "  Python, Unity, python, ,  C# ");
            Assert.That(store.Name, Is.EqualTo("김"));
            Assert.That(store.Skills, Is.EqualTo(new[] { "Python", "Unity", "C#" }));
            Assert.That(store.Limit, Is.EqualTo(12));
            Assert.That(store.Offset, Is.Zero);
        }

        [Test]
        public void Search_PageIndexIsZeroBasedAndBounded()
        {
            var store = new MemorySearch();
            new SearchProfilesUseCase(store).Execute("", "", pageIndex: 3, pageSize: 12);
            Assert.That(store.Limit, Is.EqualTo(12));
            Assert.That(store.Offset, Is.EqualTo(36));
        }

        [Test]
        public void Search_AllSkillFiltersMustMatchSameProfile()
        {
            var store = new MemorySearch();
            store.Profiles.Add(Create(1, "Alice", "Python", "Unity"));
            store.Profiles.Add(Create(2, "Bob", "Python"));
            store.Profiles.Add(Create(3, "Charlie", "Unity"));
            store.Profiles.Add(Create(4, "Python Student", "Python", "Unity"));
            var page = new SearchProfilesUseCase(store).Execute("", "Python, Unity");
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(page.Profiles.Select(p => p.Id), Is.EqualTo(new long[] { 1, 4 }));
        }

        [Test]
        public void Search_DifferentPagesReturnDifferentProfilesAndCorrectTotal()
        {
            var store = new MemorySearch();
            for (int i = 1; i <= 29; i++) store.Profiles.Add(Create(i, "Person " + i, "Python"));
            var useCase = new SearchProfilesUseCase(store);
            var first = useCase.Execute("", "Python", 0, 12);
            var last = useCase.Execute("", "Python", 2, 12);
            Assert.That(first.TotalCount, Is.EqualTo(29));
            Assert.That(first.Profiles.Count, Is.EqualTo(12));
            Assert.That(last.Profiles.Count, Is.EqualTo(5));
            Assert.That(last.Profiles[0].Id, Is.EqualTo(25));
        }

        [Test]
        public void Search_RejectsUnsafePageAndTooManySkillFilters()
        {
            var useCase = new SearchProfilesUseCase(new MemorySearch());
            Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute("", "", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute("", "", 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => useCase.Execute("", "", 0, 25));
            Assert.Throws<OverflowException>(() => useCase.Execute("", "", int.MaxValue, 12));
            Assert.Throws<ArgumentException>(() => useCase.Execute("", "a,b,c,d,e,f,g,h,i"));
        }

        private static Profile Create(long id, string name, params string[] skills)
        {
            var date = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
            return new Profile(id, name, "", "", "", "", "", true, date, date,
                skills.Select((skill, index) => new ProfileItem(index + 1, id,
                    ProfileItemType.Skill, skill, "", "", index)));
        }

        private sealed class MemorySearch : IProfileSearchRepository
        {
            public readonly List<Profile> Profiles = new List<Profile>();
            public string Name;
            public IReadOnlyList<string> Skills;
            public int Limit, Offset;

            public ProfileSearchPage Search(string name, IReadOnlyList<string> skills, int limit, int offset)
            {
                Name = name; Skills = skills; Limit = limit; Offset = offset;
                var matches = Profiles.Where(profile =>
                    (string.IsNullOrEmpty(name) ||
                     profile.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    skills.All(skill => profile.Items.Any(item =>
                        item.ItemType == ProfileItemType.Skill &&
                        item.Title.IndexOf(skill, StringComparison.OrdinalIgnoreCase) >= 0)))
                    .OrderBy(profile => profile.Id).ToList();
                return new ProfileSearchPage(matches.Skip(offset).Take(limit).ToArray(), matches.Count);
            }
        }
    }
}
