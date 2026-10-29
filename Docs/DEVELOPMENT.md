# AppPoFo 구현과 문제 해결

[← 프로젝트 소개](../README.md) · [시스템 구조](ARCHITECTURE.md)

이 문서는 기술 목록을 나열하기보다 **어떤 기능을 연결했고, 어떤 문제가 있었고, 코드에서 어떻게 다뤘는지**를 설명합니다. 검증된 사실과 아직 확인하지 않은 항목을 구분했습니다.

## 1. 얼굴 인식 결과를 디지털 명함으로 연결

**목표** — 얼굴을 검출하는 데서 끝내지 않고, 어떤 프로필에 해당하는지 판별해 실제 앱 화면에 표시하기.

**구현 흐름**

1. YuNet으로 얼굴과 주요 지점을 찾고 OpenCV로 정렬합니다.
2. 사전 학습 SFace 모델이 128차원 임베딩을 추출합니다.
3. 로컬 SQLite에 등록된 같은 모델 버전의 임베딩을 읽어 비교합니다.
4. 최고 후보가 로컬 기준을 충족하면 프로필 ID로 데이터를 조회해 명함에 연결합니다.

관련 소스: [SFace 처리](../Assets/AppPofo/Presentation/FaceRegistration/Embedding/SFaceRecognizer.cs) · [후보 판정](../Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceRecognitionMatcher.cs) · [인식 화면](../Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs) · [명함 표시](../Assets/AppPofo/Presentation/Screens/BusinessCardPanelController.cs)

**확인된 결과** — 공개 소스 복원 후 Unity Play Mode에서 실제 SQLite에 데모 프로필을 만들고, 합성 얼굴 입력으로 실제 모델 추론·임베딩 저장·재조회·매칭·명함 표시까지 확인했습니다. **물리 카메라로 사람을 인식한 실기기 시연 결과와는 구분**합니다. [복원 검증 기록](../Audit/CLONE_REPRODUCIBILITY.json)

## 2. 인식 후보를 어떻게 선택할 것인가?

**문제** — 한 프로필에 정면·좌·우 등 여러 임베딩이 저장되면, 한 벡터만 비교해서는 등록된 여러 샘플을 활용하기 어렵습니다.

**코드에서 채택한 방식**

- SFace 출력은 128차원인지 검사하고 L2 정규화합니다.
- `FaceEmbeddingSimilarity`는 같은 프로필의 등록 임베딩 각각과 입력 벡터의 코사인 유사도를 구합니다.
- 프로필별로 가장 높은 점수를 남긴 후 `FaceRecognitionMatcher`가 전체 최고 후보를 선택합니다.
- 로컬 인식 화면의 기본 임계값은 `0.40`입니다. 이 수치는 유사도 기준이며 실제 인식률이 아닙니다.

**설계상 주의점** — 로컬과 원격 검색은 같은 판정 정책이 아닙니다. 따라서 같은 입력도 서로 다른 결과가 나올 수 있으며, 실제 인식 실패의 원인을 점수 부족인지 후보 간 점수 차이인지 구분해 진단해야 합니다. 모든 실패가 특정 기준 하나 때문이라고 단정하지 않습니다.

관련 소스: [정규화](../Assets/AppPofo/Presentation/FaceRegistration/Embedding/SFaceRecognizer.cs) · [유사도](../Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceEmbeddingSimilarity.cs) · [인식 후보](../Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceRecognitionMatcher.cs)

## 3. 카메라 첫 화면이 늦게 보이는 문제

**문제** — 카메라 시작 전에 얼굴 검출과 인식 모델의 Worker를 모두 동기 생성하면, 사용자가 첫 카메라 프레임을 보기까지 기다리게 됩니다.

**조치** — `FaceRecognitionCamera`에서는 첫 카메라 프레임을 보여주는 단계와 추론 모델 준비 단계를 분리했습니다. 코드는 모델 준비 전 동기 초기화가 첫 화면을 지연시켰다는 주석과 이후 흐름을 포함합니다.

**확인 범위** — 코드상 시작 순서 개선을 확인했습니다. 여기에는 동일 기기·동일 조건의 수정 전후 지연 시간 측정값을 제시하지 않으므로 '몇 배 빨라졌다' 같은 성능 수치는 주장하지 않습니다.

관련 소스: [FaceRecognitionCamera.cs](../Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs)

## 4. 외부 사람이 프로젝트를 다시 실행할 수 있을까?

**문제** — 원본 프로젝트의 비공개 DB, 운영용 키, 모델·네이티브 설치 파일에 의존하면 공개 소스만 받은 사람이 실행하기 어렵습니다.

**조치** — 실행에 필요한 소스와 Unity 설정을 남기되, 별도 설치 스크립트가 공식 출처의 고정된 의존성을 내려받고 파일 크기·SHA-256 및 Unity GUID를 확인하도록 분리했습니다.

**확인된 결과** — 독립된 소스 폴더에서 Unity Play Mode·로컬 SQLite·실제 ONNX 추론과 명함 표시까지 재현했습니다. 다만 완전히 새로운 OS/실기기에서 같은 결과를 재검증한 것은 아닙니다. Windows의 긴 경로에서는 Unity 패키지 가져오기 문제가 있어 짧은 폴더 경로를 사용했습니다.

관련 문서: [설치 안내](GETTING_STARTED.md) · [복원 검증](../Audit/CLONE_REPRODUCIBILITY.json)

## 설명할 때 구분해야 하는 것

| 직접 설명할 수 있는 애플리케이션 구현 | 외부 기술 활용 또는 별도 검증 대상 |
|---|---|
| 얼굴과 프로필·명함을 이어 주는 동작 | YuNet·SFace 모델 자체의 학습 |
| SQLite 임베딩 조회와 유사도 기반 후보 선택 | 사전 학습 모델의 원래 가중치 |
| Unity 카메라·등록·인식 화면 연결 | 실사용자 집단의 통계적 정확도 |
| 기본 로컬 모드 및 선택적 서버 통합 | 신규 Android 기기에서의 전체 APK 실측 |

이 문서는 저장소 코드에 있는 구현을 설명합니다. 실제 협업 분담이나 개인이 직접 작성한 파일 범위를 외부 검증 없이 전부 단독 기여로 표기하지 않습니다. 면접에서는 **직접 작성·수정한 부분과 기존 모델·라이브러리 재사용 부분을 구분**해 설명하면 됩니다.

## 테스트 결과는 어떻게 읽어야 하나요?

- **EditMode 531/531, PlayMode 53/53**: 기존 Windows Unity Editor 자동 테스트 기록
- **Cloudflare 85/85**: 로컬/합성 데이터 기반 서버 테스트 기록
- **CPU/GPU 실제 추론**: 사전 학습 모델 입력·출력 경로 검증
- **미검증**: 새로운 Android 실기기 공개본의 전체 실행, 물리 카메라 3방향 등록·인식, 대규모 실제 인물 인식률

전체 증거 자료는 [TEST_RESULTS.json](../Audit/TEST_RESULTS.json)과 [CLONE_REPRODUCIBILITY.json](../Audit/CLONE_REPRODUCIBILITY.json)에 있습니다. 이 문서 수정 시 테스트를 다시 수행했다고 주장하지 않습니다.

[← README로 돌아가기](../README.md)

