# Third-party notices — updated public candidate

검토일: 2026-10-29 KST. AppPoFo 자체 라이선스는 아직 선택하지 않았다. 제3자 고지는 해당 자료에만 적용한다. 로컬 설치와 공개 소스 커밋에 포함되는 파일을 구분한다.

| 리소스 | 확인한 조건 | 현재 처리 |
|---|---|---|
| YuNet 2026may | 공식 OpenCV Zoo 모델별 MIT, Copyright 2020 Shiqi Yu. 공식 LFS SHA-256 일치 | 포함. 원래 GUID. 실제 CPU/GPU 추론 PASS |
| SFace 2021dec | 공식 폴더 Apache-2.0와 정확한 LFS SHA-256 확인. 학습 데이터/가중치 권한 질문 #313은 미해결 | 공식 고정 URL로 로컬 설치·실제 추론 PASS. 직접 재배포하지 않고 설치형 제공 |
| OpenCV+Unity / OpenCvSharp managed | 고정 패키지 선언과 OpenCvSharp LICENSE Apache-2.0. 기존 관리 코드 보존 | 소스와 원래 metadata 유지, Apache 전문·저작권 포함 |
| OpenCV / native five platforms | OpenCV 4.11 Apache-2.0. 원본과 payload 해시 일치. Windows에서 실제 native crop/alignment 실행 | 로컬 설치. Intel IPP 및 codec 등 종속 고지 미완결이므로 native payload의 공개 커밋/바이너리 배포는 보류 |
| SQLite-net for Unity 1.3.2 | Gil Barbosa Reis, MIT | 공개 Git 의존성으로 복원. 캐시·DB 미포함, 전문 유지 |
| SQLite-net / SQLite3 Multiple Ciphers | Krueger Systems 등 MIT / Ulrich Telle MIT 및 포함 구성요소 고지 | 기존 전문과 패키지 원래 고지 유지. 전체 로컬 DB 암호화를 주장하지 않음 |
| Pretendard OTF/SDF | Kil Hyung-jin, Adobe, Inter, M+ FONTS; SIL OFL 1.1 | 전체 저작권·예약명·OFL 고지 유지 |
| Liberation Sans / SDF | 동봉 저작권과 SIL OFL 1.1 | 원래 고지 유지 |
| TextMesh Pro / uGUI / Unity package resources | Unity Companion License, 유효 Unity 프로젝트 이용·배포 조건 | 코드/셰이더 고지와 Companion 전문 유지. 이동한 Core 리소스는 현재 패키지 GUID에 재연결 |
| Legacy EmojiOne TMP sample | Unity 공식 TMP 3.0.6 sample 출처와 패키지 해시·원래 GUID 확인. 실제 artwork 버전의 개별 재배포 조건은 미확인 | 네 파일을 로컬 복원, 공개 소스 스냅샷에서 제외. 직접 배포 REVIEW |
| 기존 UI PNG 170개 | 사용자 확인: OpenAI 생성물. OpenAI output rights 확인 | 170개 원본 픽셀 유지. 150 COPY, 브랜드 그림 20 REVIEW. 전체 UI 제작 출처 미확인 차단은 해소 |
| Git / HTML5 그림의 대응 원본 | Jason Long Git Logo / W3C HTML5, CC BY 3.0. Git은 해당 상표 정책도 확인 | AI 스타일 변형·출처·라이선스·비보증 고지 추가 |
| CLOVA MP3 6개 | 사용자 확인: 무료 플랜. 공식 무료 정책의 소프트웨어 탑재 항목은 불허 | 원본 EXCLUDE. 출처 표시만으로 허용된다고 간주하지 않음 |
| 새 한국어 WAV 6개 | 새로 작성한 문장, eSpeak NG ko formant 합성. 엔진/데이터/MBROLA/녹음 원본은 앱에 미포함 | 임시 신호음 대체. GPL 프로그램과 새 입력의 출력물 구분 근거·제작 버전·해시 기록. 청취 품질 NOT VERIFIED |
| 새 synthetic face fixture | 이 작업에서 새로 생성한 가상 성인, OpenAI ImageGen | 실제 인물 사진이나 원본 프로필 데이터와 구분하여 문서화. 실제 추론 검증에 사용 |
| 원본 촬영 스크린샷·내부 PDF | 미승인/불필요 내부 자료 | 이전 제외 유지 |

## 음성의 근거와 재생성

무료 CLOVA의 [공식 허용 범위](https://help.naver.com/service/23823/bookmark/12463?osType=COMMONOS)는 프로그램/애플리케이션 탑재를 허용하지 않는다. 원본을 수정해서 새 라이선스로 선언하지 않고 완전히 다른 합성 출력으로 대체했다.

제작 도구는 [eSpeak NG](https://github.com/espeak-ng/espeak-ng) 및 [Echogarden Emscripten build](https://github.com/echogarden-project/espeak-ng-emscripten)의 0.3.5 배포본, `ko` 음성이다. 엔진 GPL-3.0 전문을 `ThirdPartyLicenses/eSpeak-NG-GPL-3.0.txt`에 보존했다. [GPL 제2절 및 GNU 출력물 FAQ](https://www.gnu.org/licenses/gpl-faq.en.html#WhatCaseIsOutputGPL)에 따라 도구 코드와 새 입력 문장의 출력물을 구분한다. 본 음성은 formant 규칙 합성이며 CLOVA·사람 녹음·MBROLA 샘플을 재사용하지 않았다. 이를 근거로 문장 음성을 제공하며 앱 자체를 GPL이나 다른 라이선스로 선언하지 않는다. 외부 실행 파일을 호출하는 별도 재생성 도구와 텍스트는 `tools/generate_guidance.py`에 있다.

## UI와 상표

[OpenAI Terms of Use](https://openai.com/policies/row-terms-of-use/)의 출력물 권리는 확인했다. 다만 제3자 상표를 부여하는 조항은 아니다. [UI 권리 검토](Docs/UI_RIGHTS_REVIEW.md), [파일별 출처 CSV](Audit/UI_IMAGE_PROVENANCE.csv), [Git/HTML5 출처 고지](ThirdPartyLicenses/UI-Logo-Attribution.md)에 적용 범위를 기록했다. OpenAI 생성이라는 이유만으로 모든 로고를 허용 또는 불허로 일괄 판정하지 않았다.

## 네이티브 배포 경계

[네이티브 분석](Docs/NATIVE_DEPENDENCIES.md)에 실제 payload의 빌드 문자열과 별도 고지가 필요한 구성요소를 정리했다. 현재는 재배포 권리가 완결됐다고 주장하지 않고 공식 설치형 경로를 제공한다. [설치 lock](tools/dependencies.lock.json)과 [복원 기록](Audit/DEPENDENCY_RESTORATION.json)에 해시·URL·검증 범위가 있다.

## 기존 확인 원문


- [YuNet 모델별 LICENSE](https://github.com/opencv/opencv_zoo/blob/main/models/face_detection_yunet/LICENSE), [공식 LFS 포인터](https://github.com/opencv/opencv_zoo/blob/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx). SHA-256 `ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0`, 229,738 bytes. `ThirdPartyLicenses/YuNet-MIT.txt`.
- [SFace 모델별 LICENSE](https://github.com/opencv/opencv_zoo/blob/main/models/face_recognition_sface/LICENSE), [정확한 가중치 확인 문제 #313](https://github.com/opencv/opencv_zoo/issues/313). 공식 가중치 SHA-256 `0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79`, 38,696,353 bytes. 공식 Apache 고지의 존재와 권한·학습 데이터 질문이 미해결인 사실을 구분한다. 불법으로 확정한 것은 아니며 본 후보는 보수적으로 가중치를 격리한다. `ThirdPartyLicenses/SFace-Apache-2.0.txt`.
- [OpenCV+Unity 패키지](https://github.com/neon-izm/OpenCV-plus-Unity/tree/071116d63cde332e5d0d98728d6e9d0747a1a1b9/Unity/OpenCV%2BUnity), [OpenCvSharp 4.11 LICENSE](https://github.com/shimat/opencvsharp/blob/4.11.0.20250507/LICENSE), [OpenCV 4.11 LICENSE](https://github.com/opencv/opencv/blob/4.11.0/LICENSE), [COPYRIGHT](https://github.com/opencv/opencv/blob/4.11.0/COPYRIGHT). `Audit/CV_UPSTREAM_COMPARISON.json`과 `Audit/EXTERNAL_ASSETS.json`에 동일성·파일별 해시 기록. 패키지 JSON과 Windows importer metadata의 기존 차이를 기록했으며 관리 코드를 변경한 것으로 주장하지 않는다.
- [SQLite3 Multiple Ciphers 2.1.3 LICENSE](https://github.com/utelle/SQLite3MultipleCiphers/blob/v2.1.3/LICENSE), [SQLite Unity 1.3.2](https://github.com/gilzoide/unity-sqlite-net/tree/1.3.2), [LICENSE](https://github.com/gilzoide/unity-sqlite-net/blob/1.3.2/LICENSE.txt). `ThirdPartyLicenses/SQLite-net-Unity-MIT.txt`, `SQLite-net-MIT.txt`, `SQLite3-Multiple-Ciphers-MIT.txt`.
- [Pretendard 공식 LICENSE](https://github.com/orioncactus/pretendard/blob/main/LICENSE). `Assets/AppPofo/ThirdPartyNotices/LICENSE.txt`와 `ThirdPartyLicenses/Pretendard-OFL-1.1.txt`에 전체 저작권·예약명·OFL 조건. Liberation Sans는 `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.
- [TextMesh Pro 라이선스](https://docs.unity3d.com/ja/Packages/com.unity.textmeshpro%403.0/license/LICENSE.html), [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license). `ThirdPartyLicenses/Unity-uGUI-NOTICE.md`, `Unity-Companion-License-v1.4.txt`.
- [CLOVA 무료/유료 차이 공식 FAQ](https://www.ncloud.com/support/faq/prod/2610), [무료 사용 허용 범위](https://help.naver.com/service/23823/bookmark/12463?osType=COMMONOS), [클로바더빙 약관](https://clovadubbing.naver.com/term/conditions). 출처 표기만으로 모든 이용·원본 파일 재배포가 허용된다고 추론하지 않는다. 사용자가 무료 플랜임을 확인했고 현재 공식 소프트웨어 탑재 불허 조건에 따라 원본을 제외했다.

원본 MP3는 편집·재라이선스하지 않았다. 현재 WAV는 새 문장 음성이며 임시 신호음이 아니다. 프로젝트 자체 라이선스는 미선택 상태다.

## Publication scope and attribution

The project owner authorized publication of the current source and UI for a personal portfolio with the review notes preserved. This decision does not grant rights held by another party. The 20 generated graphic variants remain REVIEW; model/native/legacy sample payloads remain install-only. See [publication decision](Audit/PUBLICATION_DECISION.json).

AppPoFo is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity and the Unity logo are trademarks or registered trademarks of Unity Technologies or its affiliates in the U.S. and elsewhere. Python and the Python logo are trademarks of the Python Software Foundation; this project is not affiliated with the PSF. GitHub, LinkedIn, AWS, Docker, Java, MySQL, Microsoft product marks and other depicted technology/brand marks belong to their respective holders. Icons identify profile skills or external services and do not assert endorsement, certification or implementation of every depicted technology in this app. These statements are attribution and non-affiliation notices, not substitutes for graphic-use permission.
