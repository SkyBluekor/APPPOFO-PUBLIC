using System;
using System.Collections.Generic;
using AppPofo.Application.Abstractions;
using AppPofo.Application.Interfaces;
using AppPofo.Application.UseCases;
using AppPofo.Domain.Entities;
using NUnit.Framework;

namespace AppPofo.Tests.EditMode
{
    public sealed class DeleteProfileUsecaseTests
    {
        [Test]
        public void Delete_RemovesDbFirstThenItsAvatar()
        {
            var events = new List<string>();
            var repository = new ProfileStore(ProfileWithAvatar("Avatars/profile_7.png"), events);
            var avatars = new AvatarStore(events);

            new DeleteProfileUsecase(repository, avatars).Execute(7);

            Assert.That(repository.Current, Is.Null);
            Assert.That(avatars.LastDeleted, Is.EqualTo("Avatars/profile_7.png"));
            Assert.That(events, Is.EqualTo(new[] { "db", "avatar" }));
        }

        [Test]
        public void Delete_WhenDbFails_DoesNotTouchAvatar()
        {
            var repository = new ProfileStore(ProfileWithAvatar("Avatars/profile_7.png"))
            {
                FailDelete = true
            };
            var avatars = new AvatarStore();

            Assert.Throws<InvalidOperationException>(
                () => new DeleteProfileUsecase(repository, avatars).Execute(7));

            Assert.That(repository.Current, Is.Not.Null);
            Assert.That(avatars.LastDeleted, Is.Null);
        }

        [Test]
        public void Delete_WhenAvatarNotPresent_DoesNotCallFileStorage()
        {
            var repository = new ProfileStore(ProfileWithAvatar(string.Empty));
            var avatars = new AvatarStore();

            new DeleteProfileUsecase(repository, avatars).Execute(7);

            Assert.That(repository.Current, Is.Null);
            Assert.That(avatars.LastDeleted, Is.Null);
        }

        [Test]
        public void Delete_WhenFileCleanupFails_KeepsDbDeleteSuccessAndReportsWarning()
        {
            var repository = new ProfileStore(ProfileWithAvatar("Avatars/profile_7.png"));
            var avatars = new AvatarStore { FailDelete = true };
            Exception reported = null;

            Assert.DoesNotThrow(() => new DeleteProfileUsecase(repository, avatars,
                exception => reported = exception).Execute(7));

            Assert.That(repository.Current, Is.Null);
            Assert.That(reported, Is.TypeOf<InvalidOperationException>());
            Assert.That(avatars.LastDeleted, Is.EqualTo("Avatars/profile_7.png"));
        }

        [Test]
        public void Delete_UnknownProfile_PreservesOtherFiles()
        {
            var repository = new ProfileStore(null);
            var avatars = new AvatarStore();

            Assert.Throws<InvalidOperationException>(
                () => new DeleteProfileUsecase(repository, avatars).Execute(7));

            Assert.That(avatars.LastDeleted, Is.Null);
        }

        [Test]
        public void Delete_InvalidId_DoesNotAccessStorage()
        {
            var repository = new ProfileStore(ProfileWithAvatar("Avatars/profile_7.png"));
            var avatars = new AvatarStore();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DeleteProfileUsecase(repository, avatars).Execute(0));

            Assert.That(repository.Current, Is.Not.Null);
            Assert.That(avatars.LastDeleted, Is.Null);
        }

        private static Profile ProfileWithAvatar(string avatarPath)
        {
            var date = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
            return new Profile(7, "Test Profile", "", "", avatarPath,
                "", "", true, date, date);
        }

        private sealed class ProfileStore : IProfileRepository
        {
            private readonly List<string> events;
            public Profile Current { get; private set; }
            public bool FailDelete { get; set; }

            public ProfileStore(Profile profile, List<string> events = null)
            {
                Current = profile;
                this.events = events;
            }

            public Profile Find(long id) => Current?.Id == id ? Current : null;
            public IReadOnlyList<Profile> GetAll() =>
                Current == null ? Array.Empty<Profile>() : new[] { Current };
            public Profile Save(Profile profile) => Current = profile;

            public void Delete(long id)
            {
                if (FailDelete || Current == null || Current.Id != id)
                    throw new InvalidOperationException("DB deletion failed.");
                events?.Add("db");
                Current = null;
            }
        }

        private sealed class AvatarStore : IAvatarStorage
        {
            private readonly List<string> events;
            public bool FailDelete { get; set; }
            public string LastDeleted { get; private set; }
            public AvatarStore(List<string> events = null) => this.events = events;
            public string Save(long id, byte[] bytes) => throw new NotSupportedException();
            public byte[] Load(string path) => null;

            public void Delete(string path)
            {
                LastDeleted = path;
                events?.Add("avatar");
                if (FailDelete)
                    throw new InvalidOperationException("Avatar cleanup failed.");
            }
        }
    }
}
