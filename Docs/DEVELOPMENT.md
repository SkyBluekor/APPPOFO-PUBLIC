# AppPoFo 개발 과정과 문제 해결

[← 프로젝트 소개](../README.md) · [아키텍처](ARCHITECTURE.md) · [Android 성능 실측](ANDROID_PERFORMANCE.md)

이 문서는 기술을 나열하지 않고, **문제 → 원인 → 실제 변경 → 검증 → 남은 한계** 순서로 설명합니다.

## 사례 1. 얼굴 인식은 되는데 왜 명함이 나타나지 않을까?

**문제:** 카메라가 얼굴 위치를 찾는 것만으로는 사용자의 이름을 알 수 없고, 얼굴 특징을 추출하는 것만으로도 해당 프로필이 누구 것인지 연결할 수 없습니다.

**구현 내용**

1. YuNet으로 검출하고 OpenCV로 얼굴을 정렬한 뒤 SFace가 128개의 특징값을 만듭니다.
2. 등록된 여러 벡터를 프로필 ID별로 묶어 유사도 최고값을 계산합니다.
3. 가장 비슷한 프로필이 기준을 만족할 때만 ID를 채택합니다.
4. 이 프로필 ID로 SQLite에서 정보를 조회해 명함과 상세 화면에 연결합니다.

**설계 선택:** 같은 사람이 여러 각도에서 등록한 샘플을 활용하되, 다른 사람의 프로필을 마음대로 표시하지 않도록 임계값을 둡니다. 외부 모델을 새로 학습한 것이 아니라 기존 추론 결과를 저장·조회·UI 로직과 연결했습니다.

**검증:** 원본 Galaxy Tab A7에서 실제 인물의 얼굴 인식과 명함 출력을 촬영했습니다. 공개 소스 복원본도 **합성 입력을 사용한** 실제 모델·SQLite·명함 연결 흐름을 확인했습니다. 두 결과를 혼동하지 않습니다.

코드: [유사도 계산](../Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceEmbeddingSimilarity.cs), [매칭](../Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceRecognitionMatcher.cs), [SQLite](../Assets/AppPofo/Infrastructure/SQLite/Repositories/SQLiteFaceEmbeddingRepository.cs), [명함 컨트롤러](../Assets/AppPofo/Presentation/Screens/BusinessCardPanelController.cs).

## 사례 2. 태블릿에서 카메라 첫 인식이 수십 초 동안 멈추다

**문제:** Galaxy Tab A7(SM-T500)에서 얼굴 인식 화면 처음 진입 시 영상이 장시간 멈추고, 인식 중에도 화면이 끊기는 현상이 발생했습니다.

**원인 분석:** 'AI가 느리다'로 끝내지 않고 카메라 시작·YuNet 입력 변환·SFace 첫 스케줄·명함 표시 시점을 나누어 로그를 확인했습니다. 기존 GPU 경로에서 **YuNet 입력 변환 약 25.99초**, **SFace 첫 스케줄 약 26.39초** 등 첫 실행 병목을 기록했습니다. 서로 다른 단계/실험의 수치이며 단순 합산하지 않습니다.

**개선:** 카메라 프리뷰를 먼저 띄우고, 해당 태블릿에서는 **YuNet CPU16·SFace CPU4**의 프레임 분할 처리로 변경했습니다. 이 숫자는 AI 레이어를 한 화면 프레임에 몇 개씩 처리할지 정하는 예산이지 CPU 코어 수가 아닙니다. 다른 기기의 GPU 기본 경로는 유지합니다.

**검증:** 신규 얼굴 등록 후 명함보기 화면에 진입한 실기기 개발 로그에서 **첫 명함 표시 1,442.4ms** 사례를 확인했습니다. 별도 CPU4 명함보기 5회 시연의 첫 명함 시간은 **1.83, 2.87, 3.60, 1.47, 2.73초(평균 약 2.50초)**였습니다. 이는 통제된 전후 벤치마크나 모든 기기의 보장 성능이 아닙니다.

[실기기 성능 기록 자세히](ANDROID_PERFORMANCE.md) · [인식 카메라](../Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs) · [기기별 정책](../Assets/AppPofo/Presentation/FaceRegistration/Detection/YuNetExecutionPolicy.cs).

## 사례 3. 얼굴을 잠깐 놓쳤다고 명함이 사라지면?

**문제:** 실시간 카메라에서는 조명과 움직임 때문에 한 번의 인식이 실패할 수 있습니다. 이때 명함을 즉시 지우면 화면이 깜빡이는 것처럼 느껴집니다.

**개선:** 마지막으로 인식한 프로필을 일정 시간 유지하고, 인식 실패가 계속되거나 카메라를 닫으면 관련 화면을 정리합니다. 로컬 화면과 서버 화면은 서로 다른 유지 정책을 갖습니다.

**검증:** 코드의 로컬 인식 UI 완충 시간과 서버 측 일시적 미인식 허용 시간·테스트를 확인할 수 있습니다. 실제 사용 환경별 체감 차이는 별도 평가가 필요합니다.

코드: [FaceRecognitionCamera](../Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs) · [CloudFaceRecognitionPresenter](../Assets/AppPofo/Presentation/FaceRecognition/CloudFaceRecognitionPresenter.cs).

## 사례 4. 서버 검색은 성공했는데 사진 명함이 나오지 않다

**문제:** 서버가 인물 검색에 성공하더라도 사진이 포함된 응답은 더 커져, 앱에서 응답 데이터를 온전히 읽지 못하면 명함 표시가 실패할 수 있습니다.

**개선:** 사진을 포함한 응답 크기를 고려해 데이터 처리 제한을 수정하고 관련 회귀 테스트를 보강했습니다.

**검증:** Private 원본의 Android 실기기 검증에서는 사진이 있는 명함을 표시했지만, **이 공개 저장소를 원격 서버에 연결해 재검증한 결과는 아닙니다.** 기본 공개본은 로컬 모드입니다.

## 사례 5. 내 PC가 아니어도 실행 가능한 공개본으로 만들기

**문제:** 사설 서버 키, 개발자 PC의 설치 바이너리, 테스트용 개인정보가 없으면 실행이 막히는 소스 공개는 포트폴리오로서 재현성이 떨어집니다.

**조치:** 인증정보·사용자 DB 없이 소스를 공개하고, 별도 설치 스크립트가 공식 출처의 고정된 모델·네이티브 파일을 크기·해시로 검증해 설치하도록 했습니다.

**검증:** 새 소스 사본에서 Unity Play Mode, 실제 SQLite 저장/재조회, 모델 추론, 명함 표시를 확인했습니다. 대신 **가상 얼굴 fixture를 사용했으며 완전히 새로운 PC/실기기를 재검증한 것은 아닙니다.**

[실행 방법](GETTING_STARTED.md) · [재현 검증](../Audit/CLONE_REPRODUCIBILITY.json).

## 객관적 확인 자료

| 자료 | 의미 | 주의 |
|---|---|---|
| [실제 얼굴 인식 화면](Images/real-face-recognition.png) | 기존 Android 앱의 로컬 인식·명함 표시 | 인물 사진은 모자이크; 전체 정확도 측정 아님 |
| [EditMode/PlayMode 테스트](../Audit/TEST_RESULTS.json) | Unity 531/531 및 53/53 통과 기록 | 실제 카메라 전 환경 검증 아님 |
| [Cloudflare 테스트](../Audit/TEST_RESULTS.json) | 서버 85/85 로컬·합성 테스트 | 원격 운영 서버 검증 아님 |
| [공개본 재현 보고](../Audit/CLONE_REPRODUCIBILITY.json) | 공개 소스·설치 스크립트·합성 입력 재현 | 새 APK/물리 카메라 미검증 |

**포트폴리오의 핵심:** 화면을 만드는 데서 그치지 않고, 모델 출력을 해석하고 데이터를 연결해 실제 기능으로 만들며, 제한된 하드웨어의 문제를 로그와 실측으로 분석했습니다. 학습 모델을 직접 만들었다는 주장이나 측정되지 않은 인식률·FPS를 추가하지 않습니다.

[← README로 돌아가기](../README.md)

