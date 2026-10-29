using System;
using NUnit.Framework;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;

namespace Apppofo.Tests.EditMode
{
    public sealed class FaceEmbeddingTests
    {
        [Test]
        public void Constructor_WhenValuesAreEmpty_ThrowsArgumentException()
        {
            // 현재 시간을 직접 가져오지 않고 고정된 시간을 사용하면 테스트를 언제 실행해도 결과 동일
            DateTimeOffset fixedTime = new DateTimeOffset(
                year: 2026,
                month: 7,
                day: 23,
                hour: 23,
                minute: 0,
                second: 0,
                offset: TimeSpan.Zero
            );

            float[] emptyValues = Array.Empty<float>(); // 빈 배열 생성

            // 얼굴 특징값이 하나도 없는 배열은 유효한 임베딩이 아님
            Assert.Throws<ArgumentException>(() =>
            {
                // 예외가 발생해야 하는 코드
                _ = new FaceEmbedding(
                    id: 0,
                    profileId: 1,
                    values: emptyValues,
                    modelVersion: "test-model-v1",
                    qualityScore: null,
                    sourceType: FaceEmbeddingSourceType.Image,
                    createdAt: fixedTime);
            });
        }

        [Test]
        public void Constructor_WhenModelVersionIsEmpty_ThrowsArgumentException()
        {
            DateTimeOffset fixedTime = new DateTimeOffset(
                year: 2026,
                month: 7,
                day: 23,
                hour: 23,
                minute: 0,
                second: 0,
                offset: TimeSpan.Zero
            );

            float[] values = {0.1f, 0.2f, 0.3f}; // 유효한 임베딩 값 예시

            // 모델 버전이 비어있으면 유효하지 않음
            Assert.Throws<ArgumentException>(() =>
            {
                // 예외가 발생해야 하는 코드
                _ = new FaceEmbedding(
                    id: 0,
                    profileId: 1,
                    values: values,
                    modelVersion: "     ",
                    qualityScore: null,
                    sourceType: FaceEmbeddingSourceType.Image,
                    createdAt: fixedTime);
            });
        }

        [Test]
        public void Constructor_WhenOriginalArrayIsChanged_KeepsInternalValues()
        {
            DateTimeOffset fixedTime = new DateTimeOffset(
                year: 2026,
                month: 7,
                day: 23,
                hour: 23,
                minute: 0,
                second: 0,
                offset: TimeSpan.Zero
            );

            float[] originalValues = {0.1f, 0.2f, 0.3f}; // 유효한 임베딩 값 예시

            FaceEmbedding embedding = new FaceEmbedding(
                id: 0,
                profileId: 1,
                values: originalValues,
                modelVersion: "test-model-v1",
                qualityScore: null,
                sourceType: FaceEmbeddingSourceType.Image,
                createdAt: fixedTime);

            // 원본 배열을 변경
            originalValues[0] = 999f;

            // 생성자가 배열을 복사했다면 내부 값은 기존 0.1f로 유지
            Assert.That(embedding.Values[0], Is.EqualTo(0.1f));
            
        }
    }
}