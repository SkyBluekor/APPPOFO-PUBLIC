# AppPoFo — 얼굴 인식 기반 디지털 포트폴리오

Unity/C#으로 프로필과 기술·프로젝트·링크를 관리하고, 등록한 얼굴을 인식하면 해당 프로필의 디지털 명함을 표시하는 앱입니다. 카메라 입력, 기존 ONNX 모델 추론, 얼굴 정렬·비교, SQLite 저장소와 UI를 연결했습니다. **모델을 직접 학습한 프로젝트가 아니라 YuNet과 SFace 사전 학습 모델을 활용한 얼굴 인식 파이프라인 구현 프로젝트입니다.**

## 실제 실행 화면

아래는 공개 소스만 복원하고 공식 의존성을 설치한 Unity 프로젝트의 Play Mode에서 캡처한 실제 UI입니다. 개인 이름·연락처 대신 새로 만든 데모 프로필을 사용했습니다. 이미지로 만든 UI 목업이나 실기기 카메라 시연 화면이 아닙니다.

| 시작 화면 | 프로필·기술 등록 | 디지털 명함 |
|---|---|---|
| <img src="Docs/Images/01-welcome.png" width="230" alt="AppPoFo 실제 시작 화면"> | <img src="Docs/Images/02-onboarding-skills.png" width="230" alt="실제 프로필 기술 등록 화면"> | <img src="Docs/Images/03-business-card.png" width="230" alt="SQLite에 저장한 데모 프로필의 실제 명함 화면"> |

[프로필 상세 화면](Docs/Images/04-profile-detail.png) · [화면과 검증 데이터의 출처](Docs/Images/README.md)

## 핵심 기능

- 프로필 생성·수정·삭제·검색, 사진 및 기술·프로젝트·링크 관리
- 카메라 회전·미러링 보정, 얼굴 검출·품질 판단, 정면·좌측·우측 등록 단계와 음성 안내
- SFace 임베딩 저장, 코사인 유사도 기반 얼굴 비교, 인식된 프로필과 명함·상세 화면 연결
- 기존 이미지·테마를 유지한 디지털 명함과 갤러리/카메라 아바타 입력
- SQLite 스키마 마이그레이션, 로컬 프로필·항목·얼굴 임베딩 저장
- 선택적인 Cloudflare Workers/D1 API: 기기 인증, 소유권, 동의·철회·삭제, 서버 측 암호화 얼굴 저장·비교

## 설치와 실행

Windows에서 검증했습니다. 준비물은 **Unity Hub와 Unity 6000.5.4f1**, **Python 3.9 이상**(검증 3.12), **Git** 및 최초 설치 시 인터넷 연결입니다. Unity 라이선스를 활성화하세요. SQLite UPM Git 패키지를 받으려면 Git이 PATH에 있어야 합니다. Android 빌드에는 같은 Unity 버전의 Android Build Support/SDK/NDK/OpenJDK가 필요합니다.

Windows에서는 `D:\AppPoFoPublic`처럼 **짧은 경로**에 Clone하세요. 깊은 폴더에서는 Unity 패키지 내부 파일이 Windows 경로 제한에 걸릴 수 있습니다. 공개 저장소는 [SkyBluekor/APPPOFO-PUBLIC](https://github.com/SkyBluekor/APPPOFO-PUBLIC)입니다.

```powershell
git clone https://github.com/SkyBluekor/APPPOFO-PUBLIC.git D:\AppPoFoPublic
Set-Location D:\AppPoFoPublic
```

프로젝트 루트에서 **Unity를 열기 전에** 실행합니다.

```powershell
python tools/setup_dependencies.py --all-platforms --accept-upstream-licenses
python tools/setup_optional_tmp.py --accept-unity-sample-terms
```

YuNet은 라이선스 고지와 함께 소스에 포함됩니다. 첫 명령은 SFace와 OpenCV 네이티브 파일을 공식 고정 URL에서 받아 크기·SHA-256을 검증하고 원래 Unity GUID를 복원합니다. Windows와 Android만 필요하면 `--all-platforms` 대신 `--platform Windows --platform Android`를 사용할 수 있습니다. 두 번째 명령은 현재 패키지에서 빠진 TMP 기본 sprite 참조를 Unity 공식 패키지로 복원합니다. 원본 Private 프로젝트나 별도 비공개 파일은 필요하지 않습니다.

1. Unity Hub → **Add project from disk**로 이 폴더를 추가하고 6000.5.4f1로 엽니다. 최초 패키지 복원·스크립트 컴파일·에셋 임포트가 끝날 때까지 기다립니다.
2. `Assets/AppPofo/Presentation/Scenes/Bootstrap.unity`를 엽니다.
3. Game 창을 세로 비율(예: 540×960 또는 9:16)로 설정하고 **Play**를 누릅니다.
4. 시작 화면의 **내 기기**(로컬) 모드로 시작합니다. 프로필을 만들고 이름·소개와 기술을 저장합니다. 얼굴 등록은 완료하거나 나중에 진행할 수 있습니다. 저장한 프로필에서 명함과 상세 화면을 확인할 수 있습니다.
5. 물리 카메라를 사용하는 얼굴 등록·인식은 연결된 카메라와 OS 권한이 필요합니다. 화면·음성 안내에 따라 정면·좌·우를 등록한 뒤 인식 화면에서 비교합니다. 원격 얼굴 API 연결은 기본적으로 꺼져 있습니다.

로컬 DB·아바타는 `Application.persistentDataPath`에 저장되며 저장소 폴더에 포함되지 않습니다. [자세한 설치·문제 해결](Docs/PUBLIC_SETUP.md)에서 누락 모델/네이티브, 카메라 권한, 경로 제한과 검증 명령을 확인하세요.

## 얼굴 인식 처리 과정

1. 카메라 입력을 회전·미러링 보정합니다.
2. **YuNet**으로 얼굴 영역과 5개 랜드마크를 검출하고 품질·등록 단계 조건을 검사합니다.
3. **OpenCvSharp/OpenCV native**로 얼굴을 정렬하고 SFace 입력을 전처리합니다.
4. **SFace**의 실제 ONNX 추론으로 정규화된 128차원 임베딩을 얻습니다. Unity `Unity.InferenceEngine`을 사용합니다.
5. 로컬 SQLite의 임베딩과 코사인 유사도를 비교하고 프로필을 조회하여 디지털 명함을 표시합니다. 별도로 설정·인증·동의한 원격 경로에서는 서버 비교 결과를 사용합니다.

임계값은 구현 설정값입니다. 단일 가상 인물 검증을 실제 사람 집단의 정확도·오인식률·생체 검증 성능으로 표현하지 않습니다.

## 구조와 저장소 역할

```text
Assets/AppPofo/
  Domain/           프로필·항목·얼굴 임베딩
  Application/      저장소 추상화·유스케이스
  Infrastructure/   실제 SQLite 저장소·마이그레이션·아바타 파일
  Presentation/     카메라·추론·등록/인식·명함·프로필 UI
  Models/           YuNet, 공식 설치 후 SFace
  Tests/            기존 EditMode·PlayMode 테스트
  Editor/           선택적으로 실행하는 공개본 검증 도구
Packages/           Unity 패키지 고정 정보·OpenCvSharp 관리 코드
ProjectSettings/    Unity·렌더링·빌드 설정
cloudflare/apppofo-api/  Workers 소스·D1 migrations·테스트·예제 설정
tools/              공식 의존성 설치·공개 소스 내보내기·검증
Docs/               설치 설명·실제 실행 화면·권리 검토
```

SQLite는 기기 안의 프로필·항목·로컬 얼굴 임베딩을 관리합니다. 로컬 DB 전체를 암호화한다고 주장하지 않습니다. Cloudflare Workers는 선택적인 원격 API이고 D1은 원격 프로필·소유권·동의 상태 등을 저장합니다. 서버 얼굴 집합에는 별도 키를 사용하는 AES-256-GCM이 적용됩니다. 서버 측 암호화이며 종단 간 암호화는 아닙니다.

## Cloudflare 로컬 예제

로컬 Unity 기능 실행에는 서버나 Node.js가 필요하지 않습니다. 서버 소스를 실행하려면 **Node.js 22.13.0 이상**에서 다음을 실행합니다.

```powershell
python tools/create_local_worker_env.py
Set-Location cloudflare/apppofo-api
npm ci --ignore-scripts --no-audit --no-fund
npm run migrate:local
npm run dev
```

키 생성 도구는 새로운 로컬 전용 토큰·암호화 키를 무시되는 `.dev.vars`에 기록하고 값을 출력하지 않습니다. 기존 파일은 덮어쓰지 않습니다. 예제 Wrangler의 D1 ID는 더미이고, 원격 얼굴 기능은 disabled이며 `workers_dev`도 false입니다. 위 명령은 로컬 D1만 사용합니다. Unity의 기본 환경은 offline입니다. 본인 소유의 별도 서버 연결은 [서버 안내](cloudflare/apppofo-api/README.md)를 따릅니다. 기존 운영 환경·인증 파일은 필요하지 않습니다.

## 검증 결과와 범위

검증일: 2026-10-29 KST.

| 검증 | 결과 |
|---|---|
| 기존 EditMode / PlayMode | **531/531 / 53/53 PASS** |
| 기존 Cloudflare 전체 | **85/85 PASS** |
| 실제 YuNet·SFace / native 정렬 | **CPU·GPU PASS** |
| Windows·Android C# 컴파일 | **PASS** |
| Missing Script / 미해결 GUID | **0 / 0** |
| 공개 파일만 복원 → 공식 의존성 설치 → Unity Play Mode | **PASS** |
| 실제 UI 프로필 생성 → SQLite 재조회 → 명함 표시 | **PASS** |
| 실제 임베딩 저장·재조회·인식 / 안내 WAV 6개 로딩·재생 | **PASS** |
| 새 Node 설치·로컬 D1 migration·localhost Worker health | **PASS** |

이번 정리에서는 통과한 전체 테스트를 반복하지 않고 새 설치와 실제 실행을 집중 검증했습니다. 독립된 소스 폴더와 새 DB를 사용했지만 같은 Windows 호스트의 Unity 라이선스·SDK·전역 UPM 캐시는 사용했습니다. 새 OS·Android 실기기·전체 APK/IL2CPP 빌드·물리 카메라 3방향 등록·실제 사람 집단의 정확도는 미검증입니다. 음성의 파일 로딩·재생과 한국어 발음·접근성의 사람 청취 평가는 구분합니다.

[설치 재현 증거](Audit/CLONE_REPRODUCIBILITY.json) · [기존 검증 결과](Audit/TEST_RESULTS.json) · [공개 준비 보고서](PUBLIC_READINESS_REPORT.md)

필요할 때만 기존 전체 테스트를 실행합니다.

```powershell
.\tools\run_s5_01_tests.ps1 -Suite Full -Platform Both
# 서버 전체 테스트: cloudflare/apppofo-api에서 npm test
```

공개 스냅샷을 점검할 때는 `python tools/public_preflight.py --check-installed`를 실행합니다. 파일/비밀값/설치 무결성 오류는 종료 코드 1, 게시 승인 대기는 2입니다. 소유자의 게시 승인은 제3자 권리 허가를 의미하지 않습니다. 별도 빈 폴더에 공개 대상만 내보내려면 `python tools/create_source_snapshot.py --output <존재하지-않는-독립-폴더>`를 사용합니다. 이 도구는 원본 Git 이력·설치 바이너리·캐시·사용자 DB를 복사하지 않습니다.

## 개인정보·외부 리소스·공개 상태

실제 얼굴 사진·프로필·임베딩·DB·인증정보·미승인 실사용 스크린샷을 제공하지 않습니다. 서버의 인증·소유권·동의·삭제 검사는 유지합니다. UI 170개는 기존 디자인과 GUID를 유지하며, CLOVA 무료 MP3 대신 새로 합성한 한국어 안내 WAV 6개를 제공합니다.

제3자 모델·라이브러리·폰트·음성의 출처와 조건은 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 있습니다. 모델/native/구형 TMP sample은 공식 설치형으로 제공하며 직접 묶어 배포하는 APK·설치 파일의 권리 검토는 별도입니다. 남은 브랜드 그림 20개의 구체적인 검토와 해결 경로는 [UI_RIGHTS_REVIEW.md](Docs/UI_RIGHTS_REVIEW.md)에 기록했습니다. 소유자는 검토 사항을 기록하고 기존 UI를 유지한 소스 공개를 승인했습니다. 개별 이미지의 미해결 항목은 **REVIEW**로 남기며, 허가를 취득했거나 법적으로 문제가 없다고 판정한 것으로 표시하지 않습니다. 프로젝트 자체 라이선스는 아직 선택하지 않았습니다.

이 프로젝트는 개인 포트폴리오와 기술 시연 목적으로 공개합니다. 프로필의 기술 아이콘과 외부 링크는 각 기술·서비스를 설명하기 위한 것이며, 해당 기업·재단의 공식 제품, 후원, 인증 또는 제휴를 뜻하지 않습니다. 기술명·브랜드 표장에 대한 권리는 각 권리자에게 있습니다. 이 고지는 개별 로고 이용 허가나 변형 조건을 대신하지 않습니다.

이 저장소는 원본 Private 저장소와 별도로 구성하며 원본 커밋·브랜치·태그·Git 이력을 포함하지 않습니다. [게시 범위와 소유자 결정](Audit/PUBLICATION_DECISION.json)에 공개 대상 및 남은 검토를 기록했습니다. 프로젝트 자체 라이선스는 아직 선택하지 않았고, 제3자 파일의 라이선스가 프로젝트 전체에 적용된다는 뜻은 아닙니다.
