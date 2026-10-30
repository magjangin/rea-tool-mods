# 녹스(NoxPlayer)로 Unity APK 분석하기

녹스에 설치한 앱의 APK를 꺼내 REA Tool Mods로 판별하고, 에이전트에 분석을 맡기는 순서입니다.

> 본인이 설치한 앱의 로컬 분석과 모드 제작 용도로만 사용하세요.

---

## 0. 준비 상태 확인

`bat\check_android_emulator_env.bat`을 실행해 아래가 모두 `[ OK ]`인지 확인합니다. 앱의 **🧰 환경 점검** 카드에서도 같은 내용을 볼 수 있습니다.

| 항목 | 기대 상태 |
|---|---|
| CPU 가상화 (VT-x / AMD-V) | `VT is enabled in BIOS/UEFI and free for Nox.` |
| 녹스 설치 | `C:\Program Files (x86)\Nox\bin` |
| 녹스 실행 | `Nox is running.` |
| ADB 연결 | `127.0.0.1:62001 - <모델>, Android 9` |
| ABI | `arm64-v8a`가 포함되어야 ARM 전용 Unity 빌드를 설치할 수 있음 |

Hyper-V 계열(WSL2 · 메모리 무결성 · Windows Sandbox)이 하이퍼바이저를 점유하고 있으면 녹스가 VT를 못 쓰거나 느려질 수 있습니다. 점검 bat이 해결 방법(`bcdedit /set hypervisorlaunchtype off` → 재부팅)과 부작용을 안내합니다. 다시 켜려면 관리자 권한으로 아래를 실행하고 재부팅하세요.

```powershell
bcdedit /set hypervisorlaunchtype auto
```

---

## 1. 분석할 앱 설치 (스마트폰 또는 녹스)

### A. USB 연결 스마트폰(실기기) 사용 시 (권장)
- 스마트폰의 **설정 → 휴대전화 정보 → 소프트웨어 정보 → '빌드번호'를 7번 연타**하여 개발자 옵션을 켭니다.
- **설정 → 개발자 옵션 → 'USB 디버깅'**을 활성화한 후 PC와 USB로 연결합니다 (팝업 창에서 '항상 허용').
- 기기에서 평소처럼 Play 스토어나 원스토어 등을 통해 분석할 게임/앱을 설치합니다.
- 프롬프트 지침에 따라 에이전트가 `adb devices`로 실기기를 우선 감지하여 APK 추출과 실행 검증을 수행합니다.

### B. 녹스(NoxPlayer) 에뮬레이터 사용 시
- 녹스 안의 Play 스토어로 설치하거나, APK 파일을 녹스 창에 끌어다 놓습니다.
- 앱을 한 번 실행해 첫 실행 다운로드(추가 리소스)가 끝나게 해 두면 이후 로그 확인이 편합니다.

---

## 2. APK 꺼내기 (`nox_adb` 사용)

> **`adb` 대신 녹스의 `nox_adb`를 쓰세요.** PATH에 다른 버전의 adb(예: platform-tools 1.0.41)가 있으면 녹스의 adb 서버(1.0.36)와 서로 재시작시켜 연결이 끊깁니다. REA Tool Mods와 생성되는 프롬프트도 녹스가 설치되어 있으면 `nox_adb`를 씁니다.

설치된 앱의 패키지명 확인:

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s 127.0.0.1:62001 shell pm list packages -3
```

APK 경로 확인 (`package:/data/app/<패키지명>-.../base.apk` 형식으로 출력):

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s 127.0.0.1:62001 shell pm path <패키지명>
```

PC로 가져오기 — **파일 이름을 `<패키지명>.apk`로 바꿔서 저장**합니다:

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s 127.0.0.1:62001 pull "<pm path 결과 경로>" "H:\source\repos\apks\<패키지명>.apk"
```

- 그대로 `base.apk`로 두면 REA Tool Mods의 작업 폴더 이름이 `base-unity-android`가 되어 앱끼리 구분이 안 됩니다.
- 저장할 폴더(`H:\source\repos\apks`)가 없으면 먼저 만듭니다.
- **분할 APK:** `pm path` 결과가 여러 줄(`base.apk` + `split_config.arm64_v8a.apk` 등)이면 모두 같은 폴더에 받습니다. 네이티브 라이브러리(`libil2cpp.so`)는 보통 `split_config.<abi>.apk` 쪽에 있습니다.

---

## 3. REA Tool Mods에서 판별하고 전송

1. **📁 파일 찾아보기**로 받은 `<패키지명>.apk`를 선택합니다.
2. 앱이 APK 내용을 검사해 자동으로 판별합니다:
   - Unity 여부와 스크립팅 백엔드 (IL2CPP / Mono)
   - `global-metadata.dat` 버전, Unity 버전, ABI 목록, 분할 패키지 여부
   - 판별 결과에 맞춰 **📲 Unity APK** 버튼이 선택됩니다 (Unity가 아니면 📱 APK).
3. 작업 폴더가 `H:\source\repos\<패키지명>-unity-android`로 정해집니다. 이미 그 폴더에서 작업한 적이 있으면 "이어서 진행" 프롬프트가 만들어집니다.
4. 경고(주황색 글씨)가 없는지 확인한 뒤 **🤖 Claude 앱에 보내기**(또는 안티그래비티)를 누릅니다.

---

## 4. 에이전트가 진행하는 순서

프롬프트에 들어 있는 단계입니다. 각 단계가 끝날 때마다 작업 폴더의 `PROGRESS.md`에 기록되므로 중간에 멈춰도 다음 세션에서 이어갈 수 있습니다.

| 단계 | 내용 | 산출물 |
|---|---|---|
| 1. 압축 해제 | Windows 내장 bsdtar(`C:\Windows\System32\tar.exe`)로 APK를 풂 (Git Bash의 GNU tar는 zip을 못 풂) | `apk_unzipped\` |
| 2-A. IL2CPP | `Il2CppDumper <libil2cpp.so> <global-metadata.dat> <out>` → 실패 시 Cpp2IL (force 옵션 + 숫자 형식 Unity 버전) → DummyDll을 ilspycmd로 열어 시그니처 확인 | `il2cpp_dump\`, `src_dummy\` |
| 2-B. Mono | `ilspycmd -p`로 `assets\bin\Data\Managed\Assembly-CSharp.dll` 디컴파일 | `src\` |
| 3. Java 쪽 | JADX로 `UnityPlayerActivity` 확장, 네이티브 플러그인·SDK 연결부 확인 | `jadx_src\`, `docs\analysis.md` |
| 4. 실행 검증 | 녹스에 설치(`install -r`, 분할은 `install-multiple`) 후 `logcat -s Unity`로 런타임 로그 확인 | `logs\` |
| 5. 리패키징 · Frida (선택) | Smali/라이브러리 수정 또는 비루팅 Frida Gadget 주입 시, Apktool 빌드 후 `uber-apk-signer`로 zipalign + v2 서명 | `dist\`, `rebuilt-aligned-debugSigned.apk` |

`global-metadata.dat`의 시그니처가 표준(`0xFAB11BAF`)이 아니면 보호된 메타데이터이므로, 에이전트는 복호화를 시도하지 않고 보고 후 멈춥니다.

---

## 문제 해결

| 증상 | 원인 | 해결 |
|---|---|---|
| `nox_adb devices`에 아무것도 없음 | 녹스가 아직 부팅 중이거나 연결 전 | 안드로이드 홈 화면이 뜬 뒤 `nox_adb connect 127.0.0.1:62001` |
| 기기가 `offline`으로 표시 | adb 서버 상태 꼬임 | `nox_adb kill-server` → `nox_adb connect 127.0.0.1:62001` |
| `adb server version (41) doesn't match this client (36); killing...` | PATH의 adb와 nox_adb를 섞어 씀 | 한쪽(nox_adb)만 사용. 위 2번 안내 참고 |
| 다중 인스턴스 기기에 연결이 안 됨 | 포트가 다름 | 첫 인스턴스 `62001`, 이후 `62025`, `62026`… (`check_android_emulator_env.bat -Ports 62001,62025`) |
| `INSTALL_FAILED_NO_MATCHING_ABIS` | 인스턴스에 ARM 변환이 없음 | ABI 목록에 `arm64-v8a`가 있는 인스턴스(Android 9 64bit 등) 사용 |
| `INSTALL_FAILED_MISSING_SPLIT` | 분할 APK를 base만 설치 | 받은 APK를 모두 `install-multiple`로 설치 |
| 녹스가 느리거나 시작되지 않음 | Hyper-V 계열이 VT 점유 | 0번의 점검 bat 안내 참고 |

---

## 관련 문서
- [CODE_SNIPPETS.md 12장](CODE_SNIPPETS.md#12-배치-스크립트-bat--ps1-코드-스니펫) — 점검 bat의 VT 판별, 녹스 경로 찾기, ADB 파싱 코드
- [PROMPT_TEST.md](PROMPT_TEST.md) — 프롬프트 명령 검증 결과 (adb 버전 충돌 수정 포함)
