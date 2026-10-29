using System;
using NUnit.Framework;
using AppPofo.Domain.Entities;  
using AppPofo.Domain.ValueObjects;

namespace AppPofo.Tests.EditMode
{
    public sealed class ProfileTests
    {
        [Test] // 이 속성이 붙은 메서드를 NUin이 테스트로 인식
        public void Constructor_WhenDisplayNameIsEmpty_ThrowArgumentException()
        {
            // 현재 시간을 직접 가져오지 않고 고정된 시간을 사용하면 테스트를 언제 실행해도 결과 동일
            DateTimeOffset fixedTime = new DateTimeOffset
                (
                    year: 2026,
                    month: 7,
                    day: 23,
                    hour: 23,
                    minute: 0,
                    second: 0,
                    offset: TimeSpan.Zero
                );

            // 표시 이름으로 공백만 전달했으므로 profile 생성자는 ArgumentException을 발생시켜야함
            Assert.Throws<ArgumentException>(() =>
            {
                // 예외가 발생해야 하는 코드
                _ = new Profile(
                    id: 0,
                    displayName: "   ",
                    headline: "",
                    bio: "",
                    avatarPath: "",
                    email: "",
                    website: "",
                    isActive: true,
                    createdAt: fixedTime,
                    updatedAt: fixedTime);                
            });
        }

        [Test]
        public void Constructor_WhenDisplayNameExceeds100Characters_ThrowsArgumentException()
        {
            DateTimeOffset fixedTime = new DateTimeOffset(
                2026,
                7,
                23,
                0,
                0,
                0,
                TimeSpan.Zero);

            // '가' 문자를 101개 가진 문자열을 만듭니다.
            // 허용 범위가 최대 100자이므로 이 값은 거부되어야 합니다.
            string tooLongDisplayName = new string('가', 101);

            Assert.Throws<ArgumentException>(() =>
            {
                _ = new Profile(
                    id: 0,
                    displayName: tooLongDisplayName,
                    headline: "",
                    bio: "",
                    avatarPath: "",
                    email: "",
                    website: "",
                    isActive: true,
                    createdAt: fixedTime,
                    updatedAt: fixedTime);
            });
        }

        [Test]
        public void AddItem_WhenItemsHaveDifferentSortOrders_SortItemsAscending()
        {
            DateTimeOffset createdAt = new DateTimeOffset(
                2026,
                7,
                23,
                0,
                0,
                0,
                TimeSpan.Zero);

            var profile = new Profile(
                id: 1,
                displayName: "이태우",
                headline: "",
                bio: "",
                avatarPath: "",
                email: "",
                website: "",
                isActive: true,
                createdAt: createdAt,
                updatedAt: createdAt);

            var projectItem = new ProfileItem(
                id: 1,
                profileId: 1,
                itemType: ProfileItemType.Project,
                title: "AppPofo",
                description: "",
                url: "",
                sortOrder: 2);
            
            var skillItem = new ProfileItem(
                id: 2,
                profileId: 1,
                itemType: ProfileItemType.Skill,
                title: "C#",
                description: "",
                url: "",
                sortOrder: 0);

            var linkItem = new ProfileItem(
                id: 3,
                profileId: 1,
                itemType: ProfileItemType.Link,
                title: "GitHub",
                description: "",
                url: "https://github.com",
                sortOrder: 1);
            
            profile.AddItem(projectItem, createdAt.AddMinutes(1));
            profile.AddItem(skillItem, createdAt.AddMinutes(2));
            profile.AddItem(linkItem, createdAt.AddMinutes(3));

            Assert.That(profile.Items[0], Is.SameAs(skillItem));
            Assert.That(profile.Items[1], Is.SameAs(linkItem));
            Assert.That(profile.Items[2], Is.SameAs(projectItem));

            Assert.That(profile.Items[0].SortOrder, Is.EqualTo(0));
            Assert.That(profile.Items[1].SortOrder, Is.EqualTo(1));
            Assert.That(profile.Items[2].SortOrder, Is.EqualTo(2));
        }

        [Test]
        public void AddItem_WhenProfileIdDoesNotMatch_ThrowsArgumentException()
        {
            DateTimeOffset fixedTime = new DateTimeOffset(
                2026,
                7,
                23,
                0,
                0,
                0,
                TimeSpan.Zero);

            var profile = new Profile(
                id: 1,
                displayName: "이태우",
                headline: "",
                bio: "",
                avatarPath: "",
                email: "",
                website: "",
                isActive: true,
                createdAt: fixedTime,
                updatedAt: fixedTime);

            // 현재 Profile의 Id는 1이지만, 항목의 ProfileId는 2로 설정
            var otherProfileItem = new ProfileItem(
                id: 10,
                profileId: 2, // 다른 프로필 ID
                itemType: ProfileItemType.Skill,
                title: "C#",
                description: "",
                url: "",
                sortOrder: 0);
          
            Assert.Throws<ArgumentException>(() =>
            {
                profile.AddItem(otherProfileItem, fixedTime.AddMinutes(1));
            });

            // 예외가 발생하기 전에 목록이 변경되지 않았는지도 확인
            Assert.That(profile.Items.Count, Is.EqualTo(0));
        }

        [Test]
public void AddItem_WhenItemIdAlreadyExists_ThrowsArgumentException()
{
    DateTimeOffset fixedTime = new DateTimeOffset(
        2026,
        7,
        23,
        0,
        0,
        0,
        TimeSpan.Zero);

    var profile = new Profile(
        id: 1,
        displayName: "이태우",
        headline: "",
        bio: "",
        avatarPath: "",
        email: "",
        website: "",
        isActive: true,
        createdAt: fixedTime,
        updatedAt: fixedTime);

    var firstItem = new ProfileItem(
        id: 10,
        profileId: 1,
        itemType: ProfileItemType.Skill,
        title: "C#",
        description: "",
        url: "",
        sortOrder: 0);

    var duplicatedItem = new ProfileItem(
        id: 10,
        profileId: 1,
        itemType: ProfileItemType.Project,
        title: "AppPofo",
        description: "",
        url: "",
        sortOrder: 1);

    profile.AddItem(
        firstItem,
        fixedTime.AddMinutes(1));

    Assert.Throws<ArgumentException>(() =>
    {
        profile.AddItem(
            duplicatedItem,
            fixedTime.AddMinutes(2));
    });

    // 중복 추가가 실패했으므로 기존 항목 하나만 남아야 합니다.
    Assert.That(profile.Items.Count, Is.EqualTo(1));
    Assert.That(profile.Items[0], Is.SameAs(firstItem));
}
    }
}
