# AppPoFo 설치 및 실행

[← 프로젝트 소개](../README.md) · [시스템 구조](ARCHITECTURE.md)

**먼저 해볼 일:** 로컬 모드로 프로필을 하나 만들고 디지털 명함을 열어 보세요. 서버 계정이나 실제 얼굴 등록 없이도 이 부분을 확인할 수 있습니다.

## 1. 준비물

| 항목 | 필요 여부 |
|---|---|
| **Unity Hub / Unity 6000.5.4f1** | 필수. 검증에 사용한 정확한 버전 |
| **Windows PC** | 이 안내의 검증 환경 |
| **Python 3.9 이상, Git** | 공식 의존성 설치에 필요 |
| 인터넷 연결·Unity 라이선스 | 최초 패키지·모델 설치에 필요 |
| 카메라와 OS 카메라 권한 | 실제 얼굴 등록·인식에 필요 |
| Node.js / Cloudflare 계정 | 로컬 앱 실행에는 **필요 없음** |
| Android Build Support/SDK/NDK/OpenJDK | Android 빌드 시에만 필요 |

Windows의 긴 경로 문제를 피하기 위해 `D:\AppPoFoPublic`처럼 짧은 경로를 권장합니다.

## 2. 저장소 받기

PowerShell에서 실행합니다.

```powershell
git clone https://github.com/SkyBluekor/APPPOFO-PUBLIC.git D:\AppPoFoPublic
cd D:\AppPoFoPublic
```

## 3. Unity를 열기 **전에** 의존성 설치

```powershell
python tools/setup_dependencies.py --platform Windows --platform Android --accept-upstream-licenses
python tools/setup_optional_tmp.py --accept-unity-sample-terms
```

첫 명령은 공식 출처의 SFace 모델과 OpenCV 네이티브 파일을 고정된 버전으로 내려받아 해시·크기·Unity GUID를 확인합니다. 두 번째 명령은 Unity에서 필요한 기존 TMP 샘플 참조를 복원합니다. 다른 플랫폼도 준비해야 한다면 첫 명령에서 `--platform Windows --platform Android` 대신 `--all-platforms`를 사용할 수 있습니다.

설치 오류가 나면 파일 검증을 임의로 건너뛰거나 원본 비공개 저장소에서 바이너리를 복사하지 말고 [고급 설치 설명](PUBLIC_SETUP.md)을 확인하세요.

## 4. Unity에서 실행

1. Unity Hub → **Add project from disk** → `D:\AppPoFoPublic` 선택
2. **Unity 6000.5.4f1**로 열고 패키지 설치·컴파일·에셋 임포트 완료 확인
3. `Assets/AppPofo/Presentation/Scenes/Bootstrap.unity` 열기
4. Game 창을 **9:16**(예: 540×960) 세로 비율로 설정하고 **Play**
5. **내 기기(로컬)** 모드 선택 → 프로필 생성 → 기술·소개 저장 → 명함 화면 확인

실제 얼굴을 등록하려면 카메라 권한을 허용하고 화면 안내에 따라 정면·좌·우 얼굴을 등록한 뒤 인식 화면에서 비교해야 합니다. **현재 README에 올라온 4개 캡처는 실제 카메라 시연 영상이 아닙니다.**

## 자주 막히는 문제

| 증상 | 먼저 확인할 것 |
|---|---|
| 패키지 파일이 없거나 Unity 임포트 오류 | 폴더 경로를 짧게 설정하고 설치 명령 성공 여부 확인 |
| `DllNotFoundException` | 실행 대상 플랫폼의 OpenCV 네이티브 의존성 설치 여부 |
| SFace 모델/GUID 누락 | `setup_dependencies.py`의 해시 검증 및 설치 로그 |
| 카메라 화면이 보이지 않음 | 카메라 연결·Windows/Android 권한·다른 앱의 카메라 점유 |
| 한글 안내 음성이 들리지 않음 | Unity 음소거·오디오 장치·Audio Listener 확인 |

## 서버 코드를 별도로 실행하려면

**선택 기능**입니다. 로컬 명함 기능에 필요하지 않습니다. Node.js **22.13.0 이상**을 준비한 뒤 프로젝트 루트에서 다음을 실행합니다.

```powershell
python tools/create_local_worker_env.py
cd cloudflare/apppofo-api
npm ci --ignore-scripts --no-audit --no-fund
npm run migrate:local
npm run dev
```

로컬 D1만 사용하며 운영 서버를 배포하거나 연결하지 않습니다. [서버 전용 README](../cloudflare/apppofo-api/README.md)에 세부 사항이 있습니다.

## 검증 범위

위 실행 절차는 2026-10-29에 독립된 소스 사본의 Windows 환경에서 검증된 설정을 기준으로 정리했습니다. 별도의 완전한 신규 PC·실제 Android 기기·물리 카메라 전체 흐름은 그 검증에 포함되지 않았습니다. [검증 근거](../Audit/CLONE_REPRODUCIBILITY.json)

[← README로 돌아가기](../README.md)

