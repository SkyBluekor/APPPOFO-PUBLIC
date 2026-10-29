# AppPoFo 공개 후보 설치·검증

## 모델과 네이티브 파일

공개 소스 스냅샷에는 SFace 및 OpenCV 네이티브 바이너리를 넣지 않는다. **로컬 후보에는 공식 출처에서 설치되어 있고 실제 추론까지 검증했다.** 새 체크아웃은 Unity를 열기 전에 다음 명령으로 의존성을 설치한다. 원본 Private 프로젝트를 참조하지 않는다.

```powershell
python tools/setup_dependencies.py --all-platforms --accept-upstream-licenses
python tools/setup_optional_tmp.py --accept-unity-sample-terms
```

첫 명령은 `tools/dependencies.lock.json`의 고정 커밋 URL과 파일 크기·SHA-256을 대조하고, 원래 GUID의 `.meta`를 복원한다. 다운로드가 달라지면 설치를 거부한다. Windows/Android만 필요하면 `--all-platforms` 대신 `--platform Windows --platform Android`를 쓴다. 다른 플랫폼을 설치하지 않은 상태를 그 플랫폼의 실행 검증으로 간주하지 않는다.

- SFace: OpenCV Zoo 커밋 `48b10fa505aeb51fbd127fc60a5c660040e823b7`, 공식 LFS 바이너리, Apache-2.0 고지. 정확한 가중치의 학습 데이터/배포 권한 질문이 미해결인 점은 라이선스 문서에 남겼다. 로컬 설치와 모델 파일의 직접 재배포를 구분한다.
- OpenCV+Unity: 커밋 `071116d63cde332e5d0d98728d6e9d0747a1a1b9`, OpenCV/OpenCvSharp 4.11. 다섯 플랫폼 바이너리의 SHA-256이 원본과 일치한다. macOS Info.plist는 공식 LF와 원본 CRLF를 정규화 비교하고 두 해시를 기록했다. 네이티브 파일의 검증 기준을 생략하지 않았다.
- 두 번째 명령: Unity 공식 `com.unity.textmeshpro` 3.0.6 패키지를 해시 검증하고, 원래 기본 sprite GUID의 EmojiOne 샘플 네 파일만 로컬 복원한다. 현재 uGUI 패키지에서 빠진 구형 기본 참조를 해소하기 위한 호환성 설치다. EmojiOne artwork의 직접 재배포 조건은 미확인이라 이 샘플도 공개 커밋에서 제외한다. 앱 바이너리에 묶을 경우 별도 권리 확인이 필요하다.

설치 전과 설치 후의 소스 스냅샷 경계를 [PUBLICATION_POLICY.json](../Audit/PUBLICATION_POLICY.json)에 기록했다. `.gitignore`와 [PUBLIC_FILE_MANIFEST.csv](../Audit/PUBLIC_FILE_MANIFEST.csv)가 설치 파일의 실수 포함을 막는다. 의존성이 누락된 checkout을 mock으로 통과시키지 않는다.

## Unity

Unity **6000.5.4f1**, Windows의 D3D11에서 검증했다. Android C# 컴파일에는 해당 Editor의 Android Build Support가 필요하다. `Assets/AppPofo/Presentation/Scenes/Bootstrap.unity`를 연다. 회사/제품 식별자는 공개 후보의 별도 설정을 유지한다. 원본 운영 환경이나 인증 파일을 가져오지 않는다.

기존 전체 자동화 테스트는 다음 도구로 실행할 수 있다. 그래픽 테스트가 있으므로 `-nographics`를 추가하지 않는다. 이번 최종 실행은 `-batchmode -force-d3d11 -runTests -testPlatform EditMode/PlayMode -testCategory !Manual`이었다.

```powershell
.\tools\run_s5_01_tests.ps1 -Suite Full -Platform Both
python tools/public_preflight.py
```

별도 Editor 검증 메서드:

- `PublicFaceInferenceValidation.Run`: 가상 인물 fixture에서 실제 YuNet → native crop/affine alignment → 실제 SFace → 동기/비동기 및 밝기 변화 비교. 환경 변수 `APPPOFO_PUBLIC_INFERENCE_OUTPUT`으로 로그 밖 결과 JSON 경로를 지정한다.
- `PublicCandidateValidation.Run`: 씬/프리팹 Missing Script 및 Windows/Android C# 플레이어 컴파일. 결과 경로는 `APPPOFO_PUBLIC_VALIDATION_OUTPUT`.
- `PublicAssetReferenceRepair.Run`: 현재 Unity Core의 공식 리소스로 구형 APV 참조를 재연결하는 한정된 호환성 도구. 이미 적용되어 있으며 일반 실행 전에 반복할 필요는 없다.

이 검증 메서드는 Editor 전용 어셈블리에 있다. 앱의 배포 런타임에 mock 모델이나 검증 우회 분기를 추가하지 않았다.

## 음성

CLOVA 무료 음성은 소프트웨어 탑재가 허용되지 않아 공개 후보에 포함하지 않는다. 현재 WAV 6개는 새 한국어 안내문을 eSpeak NG의 `ko` formant 음성으로 로컬 합성한 것이다. 임시 신호음은 제거했고 원래 GUID와 등록 단계는 유지한다.

`Audit/GUIDANCE_AUDIO.json`에 전체 문장·제작 도구 버전·파일 해시·PCM 검증을 기록했다. 엔진이나 음성 데이터베이스는 Unity에 포함하지 않는다. GPL 엔진의 조건과 새 입력 문장의 합성 출력물을 구분한 근거는 제3자 고지에 있다. 자연스러움·한국어 발음·접근성의 최종 청취 승인은 아직 없다. 기존 CLOVA와 동일한 음색이라고 표현하지 않는다.

직접 녹음할 경우 아래 문장을 사용하고, 배포자의 녹음·실연 권한을 확인한 뒤 같은 파일명으로 검토용 폴더에 WAV를 준비한다. 기존 `.meta`는 유지한다. 외부 eSpeak NG CLI로 재생성하려면 다음처럼 **Assets 밖**의 명시적 출력 폴더를 쓴다.

```powershell
python tools/generate_guidance.py --espeak-exe <espeak-ng-실행파일> --output-dir <검토용-출력폴더>
```

CLI 도구는 문장과 설정을 재현한다. release WAV는 `@echogarden/espeak-ng-emscripten` 0.3.5로 제작했으므로 다른 CLI 버전/랜덤 excitation의 PCM이 바이트 단위로 같다고 약속하지 않는다. 이 대체 CLI 실행은 이번 환경에서 미검증이며 `--help`와 구문만 확인했다.

## Cloudflare

Node.js 22.13.0 이상에서 `cloudflare/apppofo-api`의 `npm ci --ignore-scripts --no-audit --no-fund`, `npm test`를 실행한다. 로컬 Miniflare D1과 합성 데이터만 사용한다. Unity 기본 환경은 offline, Worker 원격 얼굴 기능은 disabled, 운영 주소와 인증정보는 없다. 원본 Worker/D1 배포·migration·remote smoke를 실행하지 않았다.

## 검수 스냅샷의 줄바꿈

`.gitattributes`는 자동 줄바꿈 변환을 끈다. 원래 Unity metadata 및 검수한 파일의 SHA-256을 새 checkout에서도 그대로 비교하기 위한 설정이다. 파일을 수정했다면 manifest를 의도적으로 재검토·갱신하고 검증한다.

## 새 Clone의 경로·실행 문제 해결

- Windows에서는 `D:\AppPoFoPublic`처럼 짧은 폴더를 사용한다. 이번 재현 중 깊은 임시 폴더에서는 Unity 패키지의 긴 내부 경로에 `DirectoryNotFoundException`이 발생했다. 앱 기능이나 보안 검사를 우회하지 않고, 짧은 별도 프로젝트 폴더에서 설치·임포트를 다시 검증했다. 전역 레지스트리/보안 정책을 바꾸지 않았다.
- Python 3.9+와 Git이 PATH에 있어야 한다. Unity는 정확히 6000.5.4f1을 사용하고 최초 UPM 복원·컴파일·임포트 완료 후 Play를 누른다. 네트워크/Unity 라이선스가 필요하며, 저장소에 Library나 비공개 패키지 캐시를 제공하지 않는다.
- `DllNotFoundException`이면 해당 플랫폼을 `setup_dependencies.py`로 설치했는지 확인한다. 모델/GUID 누락이면 Unity를 닫고 두 설치 명령의 성공 여부와 hash 오류를 확인한다. 다른 출처의 파일로 검증을 무력화하지 않는다.
- Game 창을 9:16으로 설정하고 Bootstrap 씬에서 로컬 모드로 시작한다. 프로필 생성·기술 저장·얼굴 등록 선택·명함·상세 화면으로 이어진다. 카메라 없는 환경에서도 로컬 프로필·명함은 사용할 수 있지만 카메라 등록/인식 시연 완료로 표시하지 않는다.
- 안내 음성은 WAV 6개와 원래 `.meta`를 포함한다. 시스템/Unity 음소거·Audio Listener·오디오 장치를 확인한다. 재생 기술 검증과 한국어 발음/접근성 청취 평가는 다르다.

`PublicCloneRunValidation.Run`은 새 소스 복원 사본에서만 선택적으로 실행하는 통합 검증/실제 UI 캡처 도구다. `APPPOFO_PUBLIC_CLONE_SMOKE_OUTPUT`에 Assets 밖의 기존 증거 디렉터리를 지정한다. 이 도구는 임시 productName으로 새 실제 SQLite 저장 경로를 분리하고 종료 시 productName을 복원한다. 기존 UI controller와 실제 ONNX/native/SQLite를 사용하며 카메라 프레임은 가상 인물 fixture로 검증한다. 실제 3방향 카메라 등록을 완료했다고 주장하지 않는다. 전체 테스트나 일반 Play를 대신하는 런타임 분기는 없다.
