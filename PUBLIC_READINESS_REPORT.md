# PUBLIC_READINESS_REPORT — 공개 차단 요인 해결 결과

검수일: **2026-10-29 KST**. **기술 복구·설치 재현 PASS / 현재 게시 결정 OWNER_APPROVED_WITH_REVIEW_NOTES**.

소유자는 README에 목적·비제휴·남은 검토를 기록하고 기존 UI를 유지한 새 Public 저장소 게시를 승인했다. [최신 게시 결정](Audit/PUBLICATION_DECISION.json). 제3자 권리 검토는 REVIEW로 남으며 법적 허가/무위험 판정으로 바꾸지 않았다. 아래의 이전 BLOCKED 기록은 승인 전 검수 이력이다. 원본 Private 저장소 및 그 이력은 게시 대상이 아니다.

이전 실패 **29개를 전부 해결**했다. EditMode **531/531**, PlayMode **53/53**, Cloudflare **85/85**가 통과했다. Windows·Android C# 플레이어 컴파일, 실제 CPU/GPU YuNet·SFace 추론, 씬·프리팹 Missing Script 및 전체 리소스 GUID 검사도 통과했다. UI 원본 170개는 그대로 유지했다. 남은 차단은 아래의 특정 리소스 권리 검토이며, 테스트 실패를 그대로 둔 이전 후보와 구분한다.

원본 Private 프로젝트의 추적 파일 2,292개, HEAD, 작업 트리 상태가 작업 전과 동일하다. 원본 파일 수정·삭제, Private GitHub 변경·이력 이식·기존 저장소 Public 전환은 하지 않았다. 새 공개 Git 초기화 전 원본 무변경을 확인한 증거이며, 이후 소유자가 승인한 독립 저장소 게시와 구분한다. [원본 무변경 증거](Audit/SOURCE_INTEGRITY.json).

## 1. 실패했던 29개 테스트의 원인과 해결

이전 raw NUnit XML의 **실패 이름·메시지·스택**을 새 raw NUnit XML의 동일 `fullname`과 대조했다. 기존 테스트 C# 파일은 바이트 단위로 그대로이며 삭제·무조건 Skip·검증 기준 완화를 하지 않았다. [테스트 코드 무변경](Audit/EXISTING_TEST_CODE_INTEGRITY.json), [29개 상세 JSON](Audit/FAILURE_ANALYSIS_29.json). 이 문서의 부록에도 각 원문 메시지와 정규화 스택을 기록한다. 개인 PC 절대경로 접두사만 프로젝트 상대경로로 정리했다.

| 원인 분류 | 해당 테스트 | 근거와 해결 |
|---|---:|---|
| OpenCV 네이티브 누락 | EditMode 8개 | `DllNotFoundException: OpenCvSharpExtern`, `PixelsToMat` 또는 `core_Mat_sizeof`에서 중단. 공식 x64 DLL 해시 검증 설치 후 8개 전부 PASS |
| SFace ONNX 누락 | PlayMode 21개 | `faceRecognitionModel` null 또는 SFace 미연결 예외. 실제 모델과 원래 GUID 복원 후 21개 전부 PASS |
| Windows 플랫폼 의존성 | 위 EditMode 8개와 관련 | Windows native P/Invoke 파일 부재였다. 복원된 DLL 로딩·실행 PASS. ABI 불일치 결함은 관찰하지 않음 |
| Android 플랫폼 의존성 | 기존 29개를 Android 실행 오류로 분류할 근거 없음 | ARM64 파일 해시·빌드 문자열과 Android C# 컴파일은 확인. 실제 Android native 실행은 미검증 |
| 테스트 환경·테스트 코드 문제 | 해당 29개에서는 0개 | 이전 별도 `-nographics` 시도는 렌더링 환경 오류가 섞였으므로 그 70개 실패를 본 29개와 혼동하지 않음. 최종 D3D11 자동화 묶음으로 실행 |
| 실제 앱 기능 회귀 | 해당 29개에서는 관찰 0개 | 의존성 설치 및 GUID 복원만으로 기존 검증문이 통과했고 실제 모델/native 경로도 별도 실행. 인식 정확도까지 증명했다는 의미는 아님 |

### 테스트별 해결 요약

| 번호 | 테스트 fullname | 이전 원인 | 최종 |
|---:|---|---|---|
| 1 | `AppPofo.Tests.EditMode.FaceDetectionControllerTests.ProcessFrameWithCrop_UsesSnapshotWhenSourceChangesDuringDetection` | OpenCV native 없음 | PASS |
| 2 | `AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_DoesNotRetainFacesFromPreviousCall` | OpenCV native 없음 | PASS |
| 3 | `AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(0,False)` | OpenCV native 없음 | PASS |
| 4 | `AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(90,True)` | OpenCV native 없음 | PASS |
| 5 | `AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(270,False)` | OpenCV native 없음 | PASS |
| 6 | `AppPofo.Tests.EditMode.ProfileOnboardingTests.FrontAvatar_SelectedCropSurvivesFrameDisposal_AndCanBeReplacedManually` | OpenCV native 없음 | PASS |
| 7 | `AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.NearClipWarningDoesNotReplaceCorrectedBrightnessQuality` | OpenCV native 없음 | PASS |
| 8 | `AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.RawTopLeftStatsAgreeWithTheActualOpenCvQualityCrop` | OpenCV native 없음 | PASS |
| 9 | `AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests.CloudRegistrationConfirmationDetailConsentAndCameraRoundTrip` | SFace ONNX 없음 | PASS |
| 10 | `AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests.LocalRegistrationConfirmationDetailAndCameraRoundTrip` | SFace ONNX 없음 | PASS |
| 11 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.AlreadyRegisteredFace_UsesExistingConfirmation_InsteadOfRecognitionCamera` | SFace ONNX 없음 | PASS |
| 12 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraMetadataChanges_UpdateOnlyVideo_AndCaptureUpright` | SFace ONNX 없음 | PASS |
| 13 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RecognitionDoesNotRequireRegistrationConfirmation` | SFace ONNX 없음 | PASS |
| 14 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RecognitionIconIsSmallAndAnchoredToSafeAreaBottomRight` | SFace ONNX 없음 | PASS |
| 15 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RegistrationIconStaysOutsidePreviewAndWithinSafeArea` | SFace ONNX 없음 | PASS |
| 16 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RequiresConfirmationAndClearsStaleRegistrationCandidates` | SFace ONNX 없음 | PASS |
| 17 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_UnsupportedRearDoesNotShowButton` | SFace ONNX 없음 | PASS |
| 18 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CloudEnrollment_UsesUuidTarget_AndCannotClaimUnimplementedSave` | SFace ONNX 없음 | PASS |
| 19 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CompleteFeedback_ReturnsToProfile_AndReopeningCancelsOldClose` | SFace ONNX 없음 | PASS |
| 20 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.DarkWarning_Debounces_Latches_AndYieldsToStepVoice` | SFace ONNX 없음 | PASS |
| 21 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.LocalFaceWriter_RequiresOptInAndNeverUsesSQLiteTarget` | SFace ONNX 없음 | PASS |
| 22 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.PortraitGameViewSizes_ReflowWithoutRestartingCamera` | SFace ONNX 없음 | PASS |
| 23 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.ProfileEntry_UsesCameraControllerPreview_AndBackStopsCamera` | SFace ONNX 없음 | PASS |
| 24 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.ReenterActiveMainPanel_StartsFreshRegistrationSession` | SFace ONNX 없음 | PASS |
| 25 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegisteredProfile_RequiresConfirmation_CancelAndRetryPreserveEmbeddings` | SFace ONNX 없음 | PASS |
| 26 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegistrationCamera_DeliversFirstPreviewBeforeConstructingAiWorkers` | SFace ONNX 없음 | PASS |
| 27 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegistrationToRecognition_TransfersSharedCameraAndCanReturn` | SFace ONNX 없음 | PASS |
| 28 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.SideRejectionFeedback_UpdatesLiveChipWithoutCameraInference` | SFace ONNX 없음 | PASS |
| 29 | `AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.VoiceGuidance_DoesNotReplaySameStep_AndNewStepInterruptsOldVoice` | SFace ONNX 없음 | PASS |

## 2. 모델·네이티브·참조 복구

[설치 lock](tools/dependencies.lock.json), [복원 증거](Audit/DEPENDENCY_RESTORATION.json)에 모든 공식 URL·버전·해시·설치/배포 상태를 기록했다. 원본 프로젝트의 바이너리를 복사하지 않고 공식 배포처에서 다운로드했다.

| 대상 | 복구 결과 |
|---|---|
| YuNet 2026may | 기존 MIT 모델 유지, 공식 LFS와 SHA-256 일치, 원래 GUID `b50bd7ca6efb52c44a98482462ef9421` |
| SFace 2021dec | 공식 OpenCV Zoo 고정 커밋의 38,696,353-byte 가중치 설치. SHA-256 `0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79`, 원래 GUID `bf55204260a4ac84d927fe4c1d8dbaa4` |
| OpenCV native 5종 | Windows x64·Android ARM64·Linux x64·iOS·macOS ARM64 파일 설치, 각각 원본과 SHA-256 일치. Windows는 실제 실행도 확인 |
| macOS Info.plist | 원본 CRLF와 공식 LF의 텍스트 동일성을 확인하고 공식 byte hash와 원래 CRLF hash를 둘 다 lock에 기록. checksum 검사를 생략하지 않음 |
| 구형 APV 참조 6개 | 현재 공식 Core shader/mesh/texture 리소스로 재연결. Renderer2D GUID와 지원되는 렌더 설정 유지 |
| 구형 FallOffLookup | 현재 Renderer2DData에 없는 직렬화 필드임을 확인하고 Unity 공식 재직렬화로 정리. 기능이나 검증문을 삭제한 것이 아님 |
| TMP 기본 sprite | Unity 공식 TMP 3.0.6 패키지의 원래 GUID sample 네 파일을 명시적인 로컬 설치로 복원. artwork 직접 재배포는 REVIEW |

재현 명령은 [PUBLIC_SETUP.md](Docs/PUBLIC_SETUP.md)에 있다. installer 재실행도 통과했다. 새 체크아웃은 설치 후 실행한다. **로컬 설치된 의존성은 유지하며 공개 소스 manifest와 Git 대상에서는 제외**한다. 따라서 테스트를 통과시킨 모델을 검증 후 지워서 실행 불능으로 되돌리지 않았다. [배포 경계](Audit/PUBLICATION_POLICY.json).

### 실제 추론 — Mock 통과와 구분

새로 생성한 가상 인물 fixture에서 실제 `SentisFaceDetector`, `FaceDetectionController`, OpenCvSharp crop/5-landmark affine alignment, `SFaceRecognizer`, 비교 코드를 실행했다. 실제 ONNX Worker 출력으로 확인했으며 preprocessing-only 진단 우회나 fake detector를 사용하지 않았다. [실제 추론 결과](Audit/REAL_INFERENCE_RESULTS.json).

| 경로 | 얼굴 검출 | 검출 confidence | 임베딩 | 반복 cosine | 밝기 변화 cosine | 비동기 cosine |
|---|---:|---:|---|---:|---:|---:|
| CPU | 1 | 0.953152 | 128차원, norm 1.000000 | 1.000000 | 0.975731 | 1.000000 |
| GPUCompute | 1 | 0.953152 | 128차원, norm 1.000000 | 1.000000 | 0.975731 | 1.000000 |

단일 가상 인물의 실행·전처리·정렬·비교 검증이다. 실제 사람 집단의 정확도, 오인식률, 생체 검증이나 Android 실기기 카메라 성능으로 확대 해석하지 않는다. 임베딩 원시 벡터나 프로필 DB를 공개 자료에 기록하지 않았다.

## 3. 외부 리소스별 권리 검토

[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)와 `ThirdPartyLicenses/`를 갱신했다. AppPoFo 자체에 임의의 MIT/GPL 등 라이선스를 선언하지 않았다.

| 대상 | 확인 및 현재 판정 |
|---|---|
| YuNet | MIT 고지·원본 해시 확인, 포함 |
| SFace | 공식 Apache-2.0 고지·원본 해시 확인. [정확한 가중치 출처 질문 #313](https://github.com/opencv/opencv_zoo/issues/313)은 미해결. 직접 파일 배포는 보류하고 공식 설치형 경로 제공 |
| OpenCV+Unity / OpenCvSharp / OpenCV | Apache-2.0 소스 고지 확인. native에 IPP·codec·Eigen 등이 포함된 실제 빌드 정보를 추가 조사. native 직접 재배포의 고지 완결은 미확인, 설치형 유지 |
| SQLite 계열 | MIT 및 패키지 동봉 고지 유지 |
| Pretendard / Liberation Sans | OFL 전문·저작권·예약명 유지 |
| Unity TMP/uGUI/렌더 리소스 | Unity Companion 고지 유지, 유효 Unity 프로젝트 조건 |
| UI 원본 170개 | 사용자 확인과 OpenAI 공식 출력물 권리로 제작 출처 문제 해소. 170개 픽셀과 원래 GUID 유지. **150 COPY, 20 REVIEW** |
| Git/HTML5 대응 그림 | CC BY 3.0 및 Git 상표 정책 확인, 제작자·변형·비보증 고지 추가 |
| 남은 브랜드/기술 그림 20개 | GitHub/LinkedIn/Unity/Python/AWS/Docker 등 현재 스타일과 이용 조건의 적합성 또는 권한 미확인. 사용자 지시대로 삭제·도형 대체하지 않고 REVIEW |
| CLOVA MP3 6개 | 사용자 확인: 무료 사용. [무료 공식 정책](https://help.naver.com/service/23823/bookmark/12463?osType=COMMONOS)의 소프트웨어 탑재 불허 조건 확인. 원본 EXCLUDE |
| 새 WAV 안내 6개 | 새 한국어 문장을 formant 합성, 원래 GUID 유지. engine/code/database/CLOVA/사람 녹음을 앱에 포함하지 않음. 생성 근거·버전·해시·PCM 검증 기록. 청취 품질은 미검증 |
| Legacy EmojiOne sample | 공식 Unity 패키지 출처·패키지 해시·원래 GUID 확인. 정확한 sample artwork의 직접 배포 권한 미확인, 설치형 제외 |

20개 REVIEW 경로와 근거는 [UI_IMAGE_PROVENANCE.csv](Audit/UI_IMAGE_PROVENANCE.csv), [UI_RIGHTS_REVIEW.md](Docs/UI_RIGHTS_REVIEW.md)에 파일별로 기록했다. OpenAI 생성물이라는 이유만으로 모든 브랜드 권리를 자동 승인하지도, 전체 UI를 일괄 불허하지도 않았다.

## 4. 기존 AppPoFo와 현재 후보의 기능 차이

| 항목 | 현재 후보 |
|---|---|
| 얼굴 검출·정렬·임베딩·비교 | 코드와 실제 모델 경로 복구. Windows CPU/GPU 실제 추론 통과. 새 체크아웃에는 공식 의존성 설치 단계 필요 |
| 얼굴 등록/인식·명함·프로필 UI | 기존 자동화 584개 통과, UI PNG 170개 원본 유지. 물리 카메라의 전체 사용자 흐름은 미검증 |
| SQLite 프로필 관리 | 기존 코드와 저장 구조 유지. 원본 DB나 개인정보는 가져오지 않음 |
| 안내 음성 | CLOVA 음색 대신 새 eSpeak NG 한국어 문장 음성. 정면·좌/우·완료·어두움·중앙 안내와 기존 중단/단계 로직 유지. 기존 음색과 같지 않으며 최종 청취 승인 없음 |
| Cloudflare | 서버 소스·보안 검사 유지, 85개 통과. 원본 운영 연결을 제거하고 기본 offline. 직접 운영 연결을 복구하지 않음 |
| 배포 리소스 | 모델/native/legacy sample은 로컬 설치 파일로 유지하되 source snapshot 제외. 이를 묶은 APK/installer 공개는 추가 라이선스 검토 필요 |
| 개발 자료·실제 촬영 화면 | 내부 자료·미승인 스크린샷·로그·실제 얼굴/임베딩/DB 제외 유지 |

## 5. 최종 검증 결과와 한계

| 항목 | 결과 | 범위 |
|---|---|---|
| 이전 EditMode 8개 | PASS, 8/8 해결 | 공식 native DLL 설치, 기존 테스트 무변경 |
| 이전 PlayMode 21개 | PASS, 21/21 해결 | 공식 SFace와 원래 GUID 복원 |
| EditMode 자동화 전체 | PASS 531/531 | skip 0, D3D11, !Manual |
| PlayMode 자동화 전체 | PASS 53/53 | skip 0, D3D11, !Manual |
| Cloudflare 전체 | PASS 85/85 | 합성 데이터·실제 local Miniflare/workerd D1 |
| Windows C# 플레이어 | PASS, 44 assemblies | 완성 player/IL2CPP/native packaging 검증은 아님 |
| Android C# 플레이어 | PASS, 43 assemblies | Gradle/APK/대상 기기 native 실행 검증은 아님 |
| 실제 YuNet/SFace | PASS CPU 및 GPUCompute | 신규 가상 인물 한 명, 실제 native 정렬·동기/비동기·비교 |
| Missing Script | PASS, 0 | 씬 3개·프리팹 6개 실제 Unity 로딩 |
| 모델 및 전체 리소스 GUID | PASS, 미해소 0 | 설치 후 현재 패키지/Assets 참조 그래프 대조 |
| 기존 UI/테스트 보존 | PASS | UI 170개 원본 hash, 기존 테스트 C# byte hash 유지 |
| 원본 프로젝트 무변경 | PASS | 2,292개 tracked hash·HEAD·작업 트리 상태 동일 |
| 운영 인증·개인정보 정적 검사 | PASS (정적 검사 범위) | 실제 값 출력 없이 source 및 로컬 설치 파일 대조, 원본 운영 값·인증값·개인 절대경로 검출 0 |
| 외부 라이선스 고지 | 확인된 고지 PASS / 잔여 REVIEW | 브랜드 20개 및 직접 묶어 배포할 외부 파일의 검토 미완료 |
| 원본 Git 이력 | PASS, 미포함 | 후보 .git 없음, 새 저장소/Push 없음 |
| Android 실기기·물리 카메라 | NOT VERIFIED | 통과로 표시하지 않음 |
| 인식 정확도/FAR/FRR/liveness | NOT VERIFIED | 단일 가상 fixture 결과로 주장하지 않음 |
| 새 음성 최종 청취·접근성 | NOT VERIFIED | PCM·문장/생성 기록은 있음; 실제 청취 승인과 구분 |

원시 NUnit XML과 Editor 로그는 후보 밖 로컬 recovery 감사 폴더에 보존했다. 공개 자료에는 실제 메시지·스택의 개인 절대경로만 정리한 증거와 테스트/추론 요약을 넣었다.

## 6. 남은 리소스 검토 — 소유자 게시 승인 전 검수 기록

**승인 전에는 BLOCKED로 기록했다. 현재는 소유자의 source-only 게시 승인과 REVIEW 고지를 반영한다. 29개 테스트 실패는 모두 해결됐다.**

1. 기존 브랜드/기술 PNG 20개의 현재 스타일에 대한 개별 사용 조건 또는 권한이 아직 확정되지 않았다. GitHub·LinkedIn·Unity·Python 등의 공식 조건은 단순한 OpenAI 출력물 소유와 별개다. 원본 보존 지시에 따라 임의 교체나 삭제는 하지 않았다. 허용 근거 또는 사용자 승인한 적합한 자산 방안이 필요하다.
2. SFace/native/legacy sample을 source repo에 직접 넣거나 APK·설치 패키지에 묶어 배포하려면 해당 파일의 미완결 권리·고지 검토를 마쳐야 한다. 현재 **공식 설치형 source snapshot**으로 이 문제를 분리했으며, 로컬 실행 가능성과 재현성은 검증했다. source repo 공개와 외부 파일을 묶은 바이너리 공개를 동일한 승인으로 간주하지 않는다.

추가 미검증은 Android 대상 기기·물리 카메라·정확도·음성 청취이다. 위 표에 명시했고 완료 또는 PASS로 바꾸지 않았다. 프로젝트 자체 라이선스와 최종 게시 범위 역시 사용자 결정 사항으로 남긴다.

## 7. 소유자 승인에 따른 새 저장소 게시 범위

원본 Private 저장소는 유지한다. 소유자의 새 게시 승인에 따라 검토 사항을 남긴 source manifest만 독립 Git 저장소의 이력 없는 최초 커밋 대상으로 사용한다. 실제 stage 목록은 `Audit/PUBLIC_FILE_MANIFEST.csv` 및 `Audit/PUBLICATION_POLICY.json`과 대조한다. `tools/public_preflight.py --check-installed`는 현 로컬 설치의 원본 hash와 GUID도 검증한다.

검수 manifest와 원래 Unity metadata의 byte hash가 checkout의 자동 줄바꿈 변환 때문에 달라지지 않도록 `.gitattributes`에서 EOL 자동 변환을 끈다.

소유자가 새 Public 저장소 생성·Push를 승인했다. 대상은 `SkyBluekor/APPPOFO-PUBLIC`이며 기존 저장소를 재사용하거나 공개로 전환하지 않는다. 원본 .git/remote/태그/커밋/브랜치를 가져오거나 Private 저장소를 Public으로 변경하지 않는다.


## 최종 프로젝트 정리·설치 재현성 추가 검증

**공개 소스만으로 복원·설치·실행 PASS / 권리 검토 REVIEW 유지, 소유자 source-only 게시 승인.** 기존 전체 531/53/85개 테스트와 Windows·Android 컴파일 결과는 존중하고 반복하지 않았다.

[CLONE_REPRODUCIBILITY.json](Audit/CLONE_REPRODUCIBILITY.json)에 새 검증의 범위와 증거를 기록했다. 기존 manifest의 공개 파일만 빈 독립 폴더로 내보냈다. 원본 Private 파일, `.git`, Library, 설치된 모델/native, DB, 인증 파일은 가져오지 않았다. README와 같은 공식 설치 명령으로 7개 payload 및 TMP 네 파일을 새로 내려받아 해시와 원래 GUID를 확인했다. 같은 Windows 호스트의 설치된 Unity/라이선스/SDK와 전역 UPM cache는 사용했으므로 새 OS·실기기 검증이라고 표현하지 않는다. GitHub 저장소는 아직 생성하지 않아 실제 네트워크 Clone 대신 최종 공개 파일과 같은 source-only 복원을 사용했다.

- 실제 Bootstrap Play Mode·SQLite 초기화, 기존 UI로 프로필/기술 생성 및 독립 재조회 PASS.
- 실제 YuNet → native 정렬 → SFace 출력 128차원 임베딩의 SQLite 저장·재조회·실제 matcher 비교 PASS. 원시 벡터/DB는 공개 파일에 넣지 않음.
- 저장 프로필의 실제 명함·상세 화면 표시, 원본 UI를 사용한 540×960 캡처 4장 PASS. [실제 실행 화면](Docs/Images/README.md).
- 원래 참조 WAV 6개 로딩, 기존 지연 AudioSource 초기화와 실제 재생 PASS. 사람의 발음/접근성 청취 평가는 미검증.
- 새 npm lock 설치·로컬 D1 migrations·localhost Worker health·새 로컬 키로 실제 AES-GCM 왕복 PASS. 운영/원격 Worker·D1·배포 접근 없음.

깊은 Windows 경로에서 Unity 패키지 파일 경로 제한을 실제 발견했다. 짧은 새 폴더에서 공식 설치를 다시 재현했고 README에 짧은 Clone 경로를 명시했다. 레지스트리나 보안 설정을 바꾸지 않았다. 첫 새 smoke 도구가 지연 생성 AudioSource를 미리 요구한 오류는 검증 도구에서 실제 initializer를 호출하도록 수정했다. 앱/기존 테스트는 바꾸지 않았다. 최초 임포트에는 Unity Search의 Editor 인덱스 예외가 관찰됐고 실행 창의 앱 오류와 구분해 증거에 남겼다. 최종 실행의 runtime errors는 0이다.

중복 분류 TXT 네 파일은 완전한 `FILE_CLASSIFICATION.csv`로 통합했고, 비공개 로컬 bundle을 요구하던 옛 installer는 공식 URL installer로 통합했다. 원래 파일은 후보 밖에 보존했다. [최종 파일 구성](Audit/FINAL_FILE_LAYOUT.json). 실행 씬·프리팹·C#·UI·음성·설정·Cloudflare 기능은 삭제하지 않았다. README는 개요·구조·설치 명령·로컬 시작·모델 파이프라인·실제 화면·검증 범위 순으로 정리했다.

브랜드 그림은 삭제/단순화하지 않았다. 공식 정책과 공개 대체 자산을 추가 확인하고 [파일별 구체적 해결 경로](Docs/UI_RIGHTS_REVIEW.md)를 기록했다. 예를 들어 Java coffee-cup에는 Oracle의 별도 조건이 있고 BSD Duke 대안이 있으며, 구형 CSS shield에는 CSS-Next CC0 대안이 있다. 공식 무변형 사용/허가/대체 artwork의 조건을 실제 개별 파일에 적용하기 전에는 권리 검토를 임의 PASS로 바꾸지 않는다. 설치형 SFace/native/TMP 경계와 앱 자체 라이선스 미선택 상태도 유지한다.

설치 재현 검증 시점에는 Git 초기화·새 저장소 생성·Push를 하지 않았다. 이후 소유자가 게시를 승인했으며 이 공개 source manifest만 새 저장소의 최초 커밋 대상으로 사용한다.

## 부록. 29개 테스트의 이전 메시지·스택과 현재 결과

아래는 새로 만든 추정 오류가 아니라 이전 NUnit XML의 실패 기록이다. 전체 machine-readable 버전은 [FAILURE_ANALYSIS_29.json](Audit/FAILURE_ANALYSIS_29.json)에 있다.

### 1. AppPofo.Tests.EditMode.FaceDetectionControllerTests.ProcessFrameWithCrop_UsesSnapshotWhenSourceChangesDuringDetection

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Presentation.FaceRegistration.Detection.FaceDetectionController.ProcessFrameWithCrop (UnityEngine.Texture frame, AppPofo.Presentation.Avatar.CameraFrameOrientation orientation) [0x0024e] in Assets/AppPofo/Presentation/FaceRegistration/Detection/FaceDetectionController.cs:150 
  at AppPofo.Tests.EditMode.FaceDetectionControllerTests.ProcessFrameWithCrop_UsesSnapshotWhenSourceChangesDuringDetection () [0x000af] in Assets/AppPofo/Tests/EditMode/FaceDetectionControllerTests.cs:94 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 2. AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_DoesNotRetainFacesFromPreviousCall

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Presentation.FaceRegistration.Detection.FaceDetectionController.ProcessMultiFrame (UnityEngine.Texture frame, AppPofo.Presentation.Avatar.CameraFrameOrientation orientation) [0x0021a] in Assets/AppPofo/Presentation/FaceRegistration/Detection/FaceDetectionController.cs:261 
  at AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_DoesNotRetainFacesFromPreviousCall () [0x00060] in Assets/AppPofo/Tests/EditMode/MultiFaceDetectionTests.cs:115 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 3. AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(0,False)

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Presentation.FaceRegistration.Detection.FaceDetectionController.ProcessMultiFrame (UnityEngine.Texture frame, AppPofo.Presentation.Avatar.CameraFrameOrientation orientation) [0x0021a] in Assets/AppPofo/Presentation/FaceRegistration/Detection/FaceDetectionController.cs:261 
  at AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace (System.Int32 angle, System.Boolean mirror) [0x00141] in Assets/AppPofo/Tests/EditMode/MultiFaceDetectionTests.cs:70 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 4. AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(90,True)

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Presentation.FaceRegistration.Detection.FaceDetectionController.ProcessMultiFrame (UnityEngine.Texture frame, AppPofo.Presentation.Avatar.CameraFrameOrientation orientation) [0x0021a] in Assets/AppPofo/Presentation/FaceRegistration/Detection/FaceDetectionController.cs:261 
  at AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace (System.Int32 angle, System.Boolean mirror) [0x00141] in Assets/AppPofo/Tests/EditMode/MultiFaceDetectionTests.cs:70 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 5. AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace(270,False)

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Presentation.FaceRegistration.Detection.FaceDetectionController.ProcessMultiFrame (UnityEngine.Texture frame, AppPofo.Presentation.Avatar.CameraFrameOrientation orientation) [0x0021a] in Assets/AppPofo/Presentation/FaceRegistration/Detection/FaceDetectionController.cs:261 
  at AppPofo.Tests.EditMode.MultiFaceDetectionTests.MultiFrame_UsesOneSnapshotAndMapsEveryFace (System.Int32 angle, System.Boolean mirror) [0x00141] in Assets/AppPofo/Tests/EditMode/MultiFaceDetectionTests.cs:70 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 6. AppPofo.Tests.EditMode.ProfileOnboardingTests.FrontAvatar_SelectedCropSurvivesFrameDisposal_AndCanBeReplacedManually

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.TypeInitializationException : The type initializer for 'OpenCvSharp.Internal.NativeMethods' threw an exception.
  ----> OpenCvSharp.OpenCvSharpException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
  ----> System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at OpenCvSharp.Mat..ctor (System.Int32 rows, System.Int32 cols, OpenCvSharp.MatType type, OpenCvSharp.Scalar s) [0x00006] in Packages/com.opencvplus.unity/Runtime/Modules/core/Mat/Mat.cs:174 
  at AppPofo.Tests.EditMode.ProfileOnboardingTests.FrontAvatar_SelectedCropSurvivesFrameDisposal_AndCanBeReplacedManually () [0x00058] in Assets/AppPofo/Tests/EditMode/ProfileOnboardingTests.cs:165 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0 
--OpenCvSharpException
  at OpenCvSharp.Internal.NativeMethods.TryPInvoke () [0x00049] in Packages/com.opencvplus.unity/Runtime/Internal/PInvoke/NativeMethods/NativeMethods.cs:145 
  at OpenCvSharp.Internal.NativeMethods..cctor () [0x00037] in Packages/com.opencvplus.unity/Runtime/Internal/PInvoke/NativeMethods/NativeMethods.cs:66 
--DllNotFoundException
  at (wrapper managed-to-native) OpenCvSharp.Internal.NativeMethods.core_Mat_sizeof()
  at OpenCvSharp.Internal.NativeMethods.TryPInvoke () [0x0000e] in Packages/com.opencvplus.unity/Runtime/Internal/PInvoke/NativeMethods/NativeMethods.cs:133
```

### 7. AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.NearClipWarningDoesNotReplaceCorrectedBrightnessQuality

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.NearClipWarningDoesNotReplaceCorrectedBrightnessQuality () [0x000dc] in Assets/AppPofo/Tests/EditMode/RearCameraExposureProcessorTests.cs:276 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 8. AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.RawTopLeftStatsAgreeWithTheActualOpenCvQualityCrop

- 원인: OpenCV native library missing
- 관련 의존성: `Packages/com.opencvplus.unity/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
System.DllNotFoundException : OpenCvSharpExtern assembly:<unknown assembly> type:<unknown type> member:(null)
```

이전 스택:

```text
at (wrapper managed-to-native) OpenCvSharp.Unity.utils_texture_to_mat(intptr,int,int,bool,bool,int)
  at OpenCvSharp.Unity.PixelsToMat (UnityEngine.Color32[] pixels32, System.Int32 width, System.Int32 height, System.Boolean flipVertically, System.Boolean flipHorizontally, System.Int32 rotationAngle) [0x00049] in Packages/com.opencvplus.unity/Runtime/Unity/Unity.cs:59 
  at AppPofo.Tests.EditMode.RearCameraExposureProcessorTests.RawTopLeftStatsAgreeWithTheActualOpenCvQualityCrop () [0x00067] in Assets/AppPofo/Tests/EditMode/RearCameraExposureProcessorTests.cs:236 
  at (wrapper managed-to-native) System.Reflection.RuntimeMethodInfo.InternalInvoke(System.Reflection.RuntimeMethodInfo,object,object[],System.Exception&)
  at System.Reflection.RuntimeMethodInfo.Invoke (System.Object obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) [0x0006a] in <816f70bc2a9c4332b0bd119906a8d40e>:0
```

### 9. AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests.CloudRegistrationConfirmationDetailConsentAndCameraRoundTrip

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Error] faceRecognitionModel 이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:StartPreview () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:250)
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:OnEnable () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:417)
UnityEngine.Behaviour:set_enabled (bool)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowRecognition (UnityEngine.GameObject) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:416)
AppPofo.Presentation.Screens.ProfileListScreen:OpenRecognitionCamera () (at Assets/AppPofo/Presentation/Screens/ProfileListScreen.cs:71)
AppPofo.Presentation.Screens.CloudFaceConsentPanel/<ApproveScan>d__18:MoveNext () (at Assets/AppPofo/Presentation/Screens/CloudFaceConsentPanel.cs:181)
AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests/<CloudRegistrationConfirmationDetailConsentAndCameraRoundTrip>d__22:MoveNext () (at Assets/AppPofo/Tests/PlayMode/CloudProfileDetailFlowTests.cs:504)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)
```

### 10. AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests.LocalRegistrationConfirmationDetailAndCameraRoundTrip

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Error] faceRecognitionModel 이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:StartPreview () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:250)
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:OnEnable () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:417)
UnityEngine.Behaviour:set_enabled (bool)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowRecognition (UnityEngine.GameObject) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:416)
AppPofo.Presentation.Screens.ProfileListScreen:OpenRecognitionCamera () (at Assets/AppPofo/Presentation/Screens/ProfileListScreen.cs:71)
AppPofo.Tests.PlayMode.CloudProfileDetailFlowTests/<LocalRegistrationConfirmationDetailAndCameraRoundTrip>d__21:MoveNext () (at Assets/AppPofo/Tests/PlayMode/CloudProfileDetailFlowTests.cs:425)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)
```

### 11. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.AlreadyRegisteredFace_UsesExistingConfirmation_InsteadOfRecognitionCamera

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Presentation.Screens.ProfileDetailScreen:ConfirmProfileAction() (at Assets/AppPofo/Presentation/Screens/ProfileDetailScreen.cs:195)
AppPofo.Tests.PlayMode.<AlreadyRegisteredFace_UsesExistingConfirmation_InsteadOfRecognitionCamera>d__9:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:330)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 12. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraMetadataChanges_UpdateOnlyVideo_AndCaptureUpright

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<CameraMetadataChanges_UpdateOnlyVideo_AndCaptureUpright>d__27:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:894)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 13. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RecognitionDoesNotRequireRegistrationConfirmation

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Error] faceRecognitionModel 이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:StartPreview () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:250)
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:OnEnable () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:417)
UnityEngine.Behaviour:set_enabled (bool)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowRecognition (UnityEngine.GameObject) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:416)
AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests/<CameraSwitch_RecognitionDoesNotRequireRegistrationConfirmation>d__21:MoveNext () (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:722)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)
```

### 14. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RecognitionIconIsSmallAndAnchoredToSafeAreaBottomRight

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Error] faceRecognitionModel 이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:StartPreview () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:250)
AppPofo.Presentation.FaceRecognition.FaceRecognitionCamera:OnEnable () (at Assets/AppPofo/Presentation/FaceRecognition/FaceRecognitionCamera.cs:417)
UnityEngine.Behaviour:set_enabled (bool)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowRecognition (UnityEngine.GameObject) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:416)
AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests/<CameraSwitch_RecognitionIconIsSmallAndAnchoredToSafeAreaBottomRight>d__18:MoveNext () (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:627)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)
```

### 15. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RegistrationIconStaysOutsidePreviewAndWithinSafeArea

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<CameraSwitch_RegistrationIconStaysOutsidePreviewAndWithinSafeArea>d__17:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:588)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 16. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_RequiresConfirmationAndClearsStaleRegistrationCandidates

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<CameraSwitch_RequiresConfirmationAndClearsStaleRegistrationCandidates>d__20:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:670)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 17. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CameraSwitch_UnsupportedRearDoesNotShowButton

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<CameraSwitch_UnsupportedRearDoesNotShowButton>d__19:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:659)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 18. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CloudEnrollment_UsesUuidTarget_AndCannotClaimUnimplementedSave

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowCloud(String, GameObject, Action`2, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:333)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowCloud(String, GameObject, Action`2, Boolean, Boolean) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:321)
AppPofo.Tests.PlayMode.<CloudEnrollment_UsesUuidTarget_AndCannotClaimUnimplementedSave>d__6:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:66)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 19. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.CompleteFeedback_ReturnsToProfile_AndReopeningCancelsOldClose

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<CompleteFeedback_ReturnsToProfile_AndReopeningCancelsOldClose>d__14:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:512)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 20. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.DarkWarning_Debounces_Latches_AndYieldsToStepVoice

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<DarkWarning_Debounces_Latches_AndYieldsToStepVoice>d__13:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:477)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 21. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.LocalFaceWriter_RequiresOptInAndNeverUsesSQLiteTarget

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowCloud(String, GameObject, Action`2, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:333)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:ShowCloud(String, GameObject, Action`2, Boolean, Boolean) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:321)
AppPofo.Tests.PlayMode.<LocalFaceWriter_RequiresOptInAndNeverUsesSQLiteTarget>d__7:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:157)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 22. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.PortraitGameViewSizes_ReflowWithoutRestartingCamera

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<PortraitGameViewSizes_ReflowWithoutRestartingCamera>d__26:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:826)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 23. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.ProfileEntry_UsesCameraControllerPreview_AndBackStopsCamera

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Presentation.Screens.ProfileDetailScreen:OpenFaceRegistration() (at Assets/AppPofo/Presentation/Screens/ProfileDetailScreen.cs:168)
AppPofo.Presentation.Screens.ProfileDetailScreen:OpenFaceCamera() (at Assets/AppPofo/Presentation/Screens/ProfileDetailScreen.cs:80)
UnityEngine.Events.UnityEvent:Invoke()
AppPofo.Tests.PlayMode.<ProfileEntry_UsesCameraControllerPreview_AndBackStopsCamera>d__8:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:264)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 24. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.ReenterActiveMainPanel_StartsFreshRegistrationSession

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<ReenterActiveMainPanel_StartsFreshRegistrationSession>d__10:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:350)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 25. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegisteredProfile_RequiresConfirmation_CancelAndRetryPreserveEmbeddings

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Presentation.Screens.ProfileDetailScreen:ConfirmProfileAction() (at Assets/AppPofo/Presentation/Screens/ProfileDetailScreen.cs:195)
UnityEngine.Events.UnityEvent:Invoke()
AppPofo.Tests.PlayMode.<RegisteredProfile_RequiresConfirmation_CancelAndRetryPreserveEmbeddings>d__15:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:555)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 26. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegistrationCamera_DeliversFirstPreviewBeforeConstructingAiWorkers

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<RegistrationCamera_DeliversFirstPreviewBeforeConstructingAiWorkers>d__16:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:571)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 27. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.RegistrationToRecognition_TransfersSharedCameraAndCanReturn

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<RegistrationToRecognition_TransfersSharedCameraAndCanReturn>d__22:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:748)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 28. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.SideRejectionFeedback_UpdatesLiveChipWithoutCameraInference

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<SideRejectionFeedback_UpdatesLiveChipWithoutCameraInference>d__11:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:396)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```

### 29. AppPofo.Tests.PlayMode.FaceRegistrationInputPlayModeTests.VoiceGuidance_DoesNotReplaySameStep_AndNewStepInterruptsOldVoice

- 원인: SFace ONNX missing
- 관련 의존성: `Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx`
- 해결: 공식 고정 출처 다운로드 + SHA-256 검증 + 원래 `.meta` GUID 복원.
- 최종 동일 테스트 결과: **PASS**

이전 메시지:

```text
Unhandled log message: '[Exception] InvalidOperationException: SFace faceRecognitionModel이 연결되지 않았습니다.'. Use UnityEngine.TestTools.LogAssert.Expect
```

이전 스택:

```text
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.StartPreview () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:238)
AppPofo.Presentation.FaceRegistration.FaceRegistrationCamera.OnEnable () (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationCamera.cs:168)
UnityEngine.GameObject:SetActive(Boolean)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:OpenEnrollment(Int64, String, Boolean, Boolean, Boolean, CloudFaceStatusData) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:380)
AppPofo.Presentation.FaceRegistration.FaceRegistrationScreen:Show(Int64) (at Assets/AppPofo/Presentation/FaceRegistration/FaceRegistrationScreen.cs:304)
AppPofo.Tests.PlayMode.<VoiceGuidance_DoesNotReplaySameStep_AndNewStepInterruptsOldVoice>d__12:MoveNext() (at Assets/AppPofo/Tests/PlayMode/FaceRegistrationInputPlayModeTests.cs:455)
UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)
```
