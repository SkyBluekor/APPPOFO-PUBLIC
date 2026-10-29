# AppPoFo
### 개발자 컨퍼런스에서 사람과 프로젝트를 연결하는 얼굴 인식 디지털 명함 앱

**개발자 컨퍼런스·기술 세미나에서 처음 만난 사람의 기술과 프로젝트를 쉽게 확인하기 위해 만든 앱입니다.** 자신의 프로필과 얼굴을 등록한 참가자를 카메라로 인식하면 해당 인물의 디지털 명함이 나타납니다.

## 1. 실제로 작동하는 모습

<p align="center">
  <img src="Docs/Images/real-face-recognition.png" width="400" alt="삼성 태블릿 카메라가 얼굴을 인식하고 아래에 태우의 이름과 기술이 적힌 디지털 명함을 표시하는 실제 AppPoFo 화면">
</p>
<p align="center"><strong>얼굴 인식 → 등록된 사람의 디지털 명함 표시</strong></p>

2026년 10월 28일 **Samsung Galaxy Tab A7(SM-T500)**에서 촬영한 **실제 앱 실행 화면**입니다. 카메라 화면과 명함 UI는 실제 결과이며, 인물 사진은 공개를 위해 모자이크했습니다. **내 기기(로컬) 모드**의 기존 앱 화면으로, 공개용 소스를 새로 빌드한 촬영본은 아닙니다. [사진 출처와 촬영 범위](Docs/Images/README.md)

## 기획 배경 — 왜 만들었나?

개발자 행사에서는 짧은 시간에 많은 사람을 만납니다. 그런데 대화 상대가 어떤 분야에서 개발하고, 어떤 기술을 다루고, 무슨 프로젝트를 만들었는지 알아보려면 명함을 주고받거나 프로필 링크를 따로 찾아야 합니다.

**"만난 사람의 기술과 프로젝트를 그 자리에서 자연스럽게 확인할 수 없을까?"** 이 질문에서 AppPoFo를 시작했습니다.

미리 자신의 프로필과 얼굴을 등록한 참가자가 **명함 검색에 동의한 상황**을 가정했습니다. 앱으로 등록된 얼굴을 인식하면 그 사람의 이름과 주요 기술이 담긴 명함이 나타나고, 더 자세한 프로젝트와 소개도 열어볼 수 있습니다. 이 과정이 개발자끼리 대화를 시작하고 교류하는 데 도움이 되도록 설계했습니다.

- **사용 상황:** 컨퍼런스 부스, 개발자 밋업, 기술 세미나에서 참가자 간 소개
- **핵심 목표:** 단순 얼굴 감지가 아니라 **인물 식별 → 프로필 조회 → 디지털 명함 표시**까지 하나로 연결
- **설계 방향:** 인터넷 연결 없이 사용하는 내 기기 모드와, 동의한 공개 프로필을 조회하는 서버 모드를 분리

*개발자 컨퍼런스에서 사용하기 위한 기획·개발 프로젝트로, 실제 행사에 도입해 운영한 사례를 뜻하지는 않습니다. 핵심 얼굴 인식·명함 표시 기능은 태블릿에서 시연했습니다.*

## 2. 주요 기능과 사용 흐름

```mermaid
flowchart LR
    A["프로필·명함 만들기"] --> B["정면·양옆 얼굴 등록"]
    B --> C["카메라로 사람 비추기"]
    C --> D["등록된 얼굴과 비교"]
    D --> E["해당 인물의 명함 표시"]
```

| 주요 기능 | 사용자 입장에서 할 수 있는 일 |
|---|---|
| **프로필 관리** | 이름·사진·소개·기술·프로젝트·링크 등록 및 수정 |
| **얼굴 등록** | 정면·좌측·우측 얼굴을 촬영하고 등록 |
| **실시간 얼굴 인식** | 카메라로 등록된 사람을 찾고 이름·명함 표시 |
| **명함·프로필 상세** | 명함 디자인을 고르고 인식한 사람의 상세 소개 열기 |
| **내 기기 모드** | 인터넷 없이 기기 내부의 등록 정보를 비교 |
| **서버 모드(선택)** | 인증·동의 후 공개된 프로필 검색 |

### 다른 화면도 보기

| 시작 화면 | 프로필·기술 등록 | 명함 미리보기 | 프로필 상세 |
|:---:|:---:|:---:|:---:|
| <img src="Docs/Images/01-welcome.png" width="160" alt="시작 화면"> | <img src="Docs/Images/02-onboarding-skills.png" width="160" alt="기술 등록 화면"> | <img src="Docs/Images/03-business-card.png" width="160" alt="명함 미리보기"> | <img src="Docs/Images/04-profile-detail.png" width="160" alt="프로필 상세"> |

이 네 장은 **공개 소스의 Unity Play Mode에서 가상 데모 프로필로 촬영한 화면**입니다. 위에 있는 **실제 태블릿 인식 사진**과 출처·촬영 환경이 다릅니다.

## 3. 핵심 기술: 얼굴을 어떻게 명함으로 연결했나?

카메라가 얼굴을 **찾는 일**과 그 얼굴이 **누구인지 판단하는 일**은 다릅니다. AppPoFo는 두 단계를 연결한 다음, 인식 결과를 프로필과 명함에 연결합니다.

```mermaid
flowchart TD
    CAM["카메라 영상"] --> DET["YuNet · 얼굴 위치 찾기"]
    DET --> ALN["OpenCV · 얼굴 정렬"]
    ALN --> EMB["SFace · 얼굴 특징 128개 값 추출"]
    EMB --> MODE{"어디에서 비교할까?"}
    MODE -->|내 기기| L[("SQLite · 등록 얼굴 특징")]
    MODE -->|서버 선택| S["Cloudflare Workers / D1"]
    L --> MATCH{"일치하는 프로필?"}
    S --> MATCH
    MATCH -->|일치| CARD["프로필 조회 · 디지털 명함 표시"]
    MATCH -->|불일치| NONE["명함 표시하지 않음"]
```

**쉬운 설명:** 얼굴의 위치를 찾고 → 각도를 맞추고 → 얼굴을 숫자로 바꾸고 → 저장해 둔 숫자와 비교해 → 일치한 사람의 명함을 보여줍니다.

**개발 관점:** YuNet은 얼굴 위치와 5개 주요 지점을 검출하고, OpenCV는 그 지점으로 얼굴을 정렬합니다. SFace는 **128차원 임베딩(얼굴 특징 벡터)**을 만들며, 로컬 인식은 정규화된 벡터의 **코사인 유사도**를 계산해 프로필별 최고 후보를 선택한 후 임계값을 적용합니다. 서버 인식은 별도의 인증·동의·판정 정책을 사용합니다.

> YuNet·SFace는 **이미 학습된 AI 모델**입니다. 모델 자체를 새로 학습시킨 프로젝트가 아니라, **얼굴 등록 → 특징 추출·저장 → 인물 비교 → 프로필·명함 표시**를 하나의 응용 소프트웨어로 연결하는 데 중점을 뒀습니다.

[그림과 예시로 보는 시스템 구조](Docs/ARCHITECTURE.md)

## 4. 주요 구현 내용과 실제 코드

이 프로젝트에서 개발 역량을 보여주는 부분은 단순한 AI 호출이 아니라, **추론 결과를 실제 사용 기능으로 만드는 C# 애플리케이션 구조**입니다.

| 구현 주제 | 구현한 내용 | 코드 |
|---|---|---|
| **얼굴 검출·정렬** | 영상에서 얼굴·랜드마크를 검출하고 일정한 방향으로 정렬 | [YuNet](Assets/AppPofo/Presentation/FaceRegistration/Detection/SentisFaceDetector.cs) · [정렬](Assets/AppPofo/Presentation/FaceRegistration/Alignment/FaceAligner.cs) |
| **얼굴 특징 추출** | SFace 입력·출력 처리, 128차원 검사, L2 정규화 | [SFaceRecognizer](Assets/AppPofo/Presentation/FaceRegistration/Embedding/SFaceRecognizer.cs) |
| **등록 인물 판별** | 여러 얼굴 특징을 프로필별로 묶어 최고 유사도와 임계값 비교 | [유사도](Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceEmbeddingSimilarity.cs) · [후보 판정](Assets/AppPofo/Presentation/FaceRecognition/Matching/FaceRecognitionMatcher.cs) |
| **데이터 관리** | 얼굴 특징과 프로필을 SQLite에 연결해 저장·조회 | [SQLite 저장소](Assets/AppPofo/Infrastructure/SQLite/Repositories/SQLiteFaceEmbeddingRepository.cs) |
| **인식 → 명함 표시** | 카메라 인식 결과를 프로필 ID와 연결하고 실제 명함 UI 표시 | [인식 화면](Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs) · [명함 UI](Assets/AppPofo/Presentation/Screens/BusinessCardPanelController.cs) |

### 담당 구현 및 기여

AppPoFo에서 중점적으로 구현한 부분은 **얼굴 인식 결과를 사용자 프로필과 연결하여 디지털 명함을 표시하는 기능**입니다.

- **얼굴 등록 및 임베딩 관리:** 등록된 얼굴에서 추출한 특징 벡터(임베딩)를 사용자 프로필과 연결하고, 저장된 데이터를 인식 과정에서 활용하도록 구성했습니다.
- **얼굴 인식 및 사용자 매칭:** 카메라에서 인식한 얼굴의 임베딩과 등록된 데이터를 비교하여 해당 인물의 프로필을 식별하는 기능을 구현했습니다.
- **디지털 명함 연동:** 인식된 사용자의 프로필 정보를 조회하고, 카메라 화면에 해당 인물의 디지털 명함이 표시되도록 연결했습니다.

**기술 활용 범위:** 얼굴 검출과 특징 추출에는 사전 학습된 YuNet·SFace 모델을 활용했습니다. 모델 자체를 학습하거나 추론 엔진을 처음부터 개발한 것은 아니며, 기존 AI 기술을 Unity/C# 애플리케이션의 데이터 처리 및 사용자 인터페이스와 통합하는 데 집중했습니다.

### 왜 이 기술을 선택했나?

행사장에서 참가자에게 **바로 명함을 보여주는 경험**을 목표로, 카메라 처리·얼굴 비교·프로필 저장·서버 공개 여부를 구분했습니다.

- **Unity + C#:** Android 카메라, 등록 화면, 인식 결과와 명함 표시를 하나의 앱에 연결하기 위해
- **YuNet + SFace:** 얼굴의 **위치 검출**과 **인물 구별용 특징 추출**을 분리해 사용하기 위해
- **SQLite:** 행사장에 인터넷 연결이 없더라도 **기기에 이미 등록된 프로필**을 조회할 수 있도록 하기 위해
- **Cloudflare Workers + D1:** 참가자가 동의하고 공개한 프로필을 다른 기기에서도 검색하는 방식으로 확장하기 위해

[더 자세한 기술 구조와 데이터 흐름](Docs/ARCHITECTURE.md)

## 5. 개발 과정에서 해결한 문제

단순히 기능이 동작하는지뿐 아니라, **느린 태블릿에서도 사용할 수 있는지**가 중요한 과제였습니다.

| 문제 | 원인과 개선 | 확인한 결과 |
|---|---|---|
| **첫 인식 화면이 오래 멈춤** | Galaxy Tab A7에서 첫 GPU 처리 경로의 병목을 분리 측정. 카메라 화면을 먼저 보여주고 해당 기기에 CPU 분할 추론 적용 | 개발 로그에서 최대 수십 초 정지 원인을 확인, 신규 등록 이후 명함보기 진입부터 **1.44초**에 첫 명함 표시한 사례 확인 |
| **인식을 잠깐 놓치면 명함 깜빡임** | 한 번의 일시적 인식 실패로 즉시 지우지 않도록 명함 유지 시간 적용 | 기존 테스트·코드로 인식 실패 후 표시 동작 확인 |
| **서버에서 사진 명함이 표시되지 않음** | 사진을 포함한 응답 데이터가 커진 상황의 처리 제한 수정 | 원본 Android 실기기에서 사진 명함 표시 확인 |
| **공개 저장소만으로 실행하기 어려움** | 비공개 DB·키를 제외하고 외부 의존성을 검증 후 설치하도록 분리 | 공개 소스 복원 환경에서 Unity·SQLite·모델 추론·명함 표시 확인 |

**성능 수치 주의:** 1.44초는 **얼굴 등록 시간**이 아니라, 별도 실기기 개발 로그에서 **명함보기 화면에 들어간 뒤 첫 명함이 표시되기까지**의 측정값입니다. 수십 초 정지 사례와 완전히 동일한 조건의 전후 비교나 모든 기기에서 보장하는 수치는 아닙니다.

[Android 성능 문제 분석과 실측 기록](Docs/ANDROID_PERFORMANCE.md) · [문제 해결 과정과 코드](Docs/DEVELOPMENT.md)

## 6. 실행 방법

**Unity 6000.5.4f1 · Python 3.9+ · Git**이 필요합니다. 소스 코드를 받은 후 **Unity를 열기 전에** 공식 의존성을 설치해야 합니다.

```powershell
git clone https://github.com/SkyBluekor/APPPOFO-PUBLIC.git
cd APPPOFO-PUBLIC
python tools/setup_dependencies.py --platform Windows --platform Android --accept-upstream-licenses
python tools/setup_optional_tmp.py --accept-unity-sample-terms
```

Unity에서 `Assets/AppPofo/Presentation/Scenes/Bootstrap.unity`를 열고 Play를 누른 다음 **내 기기(로컬)** 모드를 선택합니다.

설치 환경·화면 크기·오류 해결: [처음 실행하는 사람을 위한 안내](Docs/GETTING_STARTED.md)

## 7. 문서 모음

| 문서 | 읽으면 알 수 있는 것 |
|---|---|
| [시스템 구조](Docs/ARCHITECTURE.md) | 등록부터 인식·DB·명함까지 흐름과 코드 구성 |
| [문제 해결 과정](Docs/DEVELOPMENT.md) | 발생한 문제, 원인, 해결 방식과 검증 |
| [Android 실기기 성능 개선](Docs/ANDROID_PERFORMANCE.md) | GPU 병목 진단, CPU16/CPU4 적용과 성능 실측 |
| [실행 방법](Docs/GETTING_STARTED.md) | 로컬에서 프로젝트 복원하고 실행 |
| [사진 자료 설명](Docs/Images/README.md) | 실제 태블릿 촬영 vs Unity 가상 데모 화면 구분 |

<details>
<summary><strong>공개 범위·외부 리소스·상세 검증 기록</strong></summary>

개인정보·운영 데이터베이스·서버 비밀키는 공개 소스에 포함하지 않았습니다. 실기기 얼굴 인식 사진은 원본 프로젝트에서 공개용으로 모자이크한 이미지입니다. 기본 실행은 로컬 모드이며 서버는 선택 사항입니다.

제3자 모델·라이브러리·아이콘의 권리와 배포 조건은 [고지 문서](THIRD_PARTY_NOTICES.md) 및 [이미지 권리 검토](Docs/UI_RIGHTS_REVIEW.md)를 참고하세요. 일부 브랜드 아이콘은 사용 조건 확인이 남아 있습니다. 프로젝트 자체에 대한 일괄 사용 허가 라이선스는 지정하지 않았습니다.

공개 기술·검증 자료: [PUBLIC_READINESS_REPORT](PUBLIC_READINESS_REPORT.md) · [Audit](Audit/) · [상세 의존성](Docs/PUBLIC_SETUP.md) · [문서 안내](Docs/README.md)

</details>

