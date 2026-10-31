# 실기기 · 녹스(NoxPlayer)로 Unity APK 분석 및 모드 제작하기

스마트폰(실기기) 또는 녹스에 설치한 앱의 APK를 꺼내 REA Tool Mods로 판별하고, 에이전트에 분석과 **Frida Gadget 복제 앱(`.mod`) + `script` 모드(`mod.js`)** 제작을 맡기는 순서입니다.

> 본인이 설치한 앱의 로컬 분석과 모드 제작 용도로만 사용하세요.

---

## 0. 준비 상태 확인

`bat\check_android_emulator_env.bat`을 실행해 아래가 모두 `[ OK ]`인지 확인합니다. 앱의 **🧰 환경 점검** 카드에서도 같은 내용을 볼 수 있습니다.

| 항목 | 기대 상태 |
|---|---|
| CPU 가상화 (VT-x / AMD-V) | `VT is enabled in BIOS/UEFI and free for Nox.` |
| 녹스 설치 | `C:\Program Files (x86)\Nox\bin` |
| 녹스 실행 | `Nox is running.` |
| ADB 연결 | 실기기 시리얼 또는 `127.0.0.1:62001 - <모델>, Android 9` |
| ABI | `arm64-v8a`가 포함되어야 ARM 전용 Unity 빌드를 설치할 수 있음 |

Hyper-V 계열(WSL2 · 메모리 무결성 · Windows Sandbox)이 하이퍼바이저를 점유하고 있으면 녹스가 VT를 못 쓰거나 느려질 수 있습니다. 점검 bat이 해결 방법(`bcdedit /set hypervisorlaunchtype off` → 재부팅)과 부작용을 안내합니다. 다시 켜려면 관리자 권한으로 아래를 실행하고 재부팅하세요.

```powershell
bcdedit /set hypervisorlaunchtype auto
```

---

## 1. 분석할 앱 설치 및 버전 고정 (스마트폰 우선, 없으면 녹스)

### A. USB 연결 스마트폰(실기기) 사용 시 (권장)
- 스마트폰의 **설정 → 휴대전화 정보 → 소프트웨어 정보 → '빌드번호'를 7번 연타**하여 개발자 옵션을 켭니다.
- **설정 → 개발자 옵션 → 'USB 디버깅'**을 활성화한 후 PC와 USB로 연결합니다 (팝업 창에서 '항상 허용').
- 기기에서 평소처럼 Play 스토어나 원스토어 등을 통해 분석할 게임/앱을 설치합니다.
- ⚠️ **Play 스토어 자동 업데이트 끄기 (필수):** Play 스토어에서 해당 앱 상세 페이지(우측 상단 ⋮ 메뉴)의 **'자동 업데이트 사용' 체크를 반드시 해제**하세요. 앱이 업데이트되면 `libil2cpp.so`가 바뀌어 분석해 둔 RVA(`docs\hooks.md`)가 전부 무효가 됩니다.
- 프롬프트 지침에 따라 에이전트가 `adb devices`로 실기기를 우선 감지하여 APK 추출과 실행 검증을 수행합니다.

### B. 녹스(NoxPlayer) 에뮬레이터 사용 시
- 녹스 안의 Play 스토어로 설치하거나, APK 파일을 녹스 창에 끌어다 놓습니다 (마찬가지로 Play 스토어 자동 업데이트는 꺼 둡니다).
- 앱을 한 번 실행해 첫 실행 다운로드(추가 리소스 · OBB)가 끝나게 해 두면 이후 로그 확인과 복제 앱 구성이 편합니다.

---

## 2. APK 꺼내기 및 OBB 확인 (`nox_adb` 단일 버전 유지)

> **`adb` 대신 녹스의 `nox_adb`만 일관되게 쓰세요 (REA 내장 ADB 도구나 PATH의 다른 `adb`와 혼용 금지).** PATH에 다른 버전의 adb(예: platform-tools 1.0.41)가 있으면 녹스의 adb 서버(1.0.36)와 서로 죽이고 재시작시켜 Frida 세션이 끊기고 기기가 `offline` 상태가 됩니다. REA Tool Mods와 생성되는 프롬프트도 녹스가 설치되어 있으면 `nox_adb`를 씁니다.

설치된 앱의 패키지명 확인 (`<기기ID>`는 실기기 시리얼 또는 `127.0.0.1:62001`):

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s <기기ID> shell pm list packages -3
```

APK 경로 확인 (`package:/data/app/<패키지명>-.../base.apk` 형식으로 출력):

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s <기기ID> shell pm path <패키지명>
```

PC로 가져오기 — **파일 이름을 `<패키지명>.apk`로 바꿔서 저장**합니다 (`nox_adb pull`은 구버전 클라이언트라 PC 저장 경로에 한글이 있으면 `Not a directory` 오류가 날 수 있으니 영문 경로를 권장합니다):

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s <기기ID> pull "<pm path 결과 경로>" "H:\source\repos\apks\<패키지명>.apk"
```

- 그대로 `base.apk`로 두면 REA Tool Mods의 작업 폴더 이름이 `base-unity-android`가 되어 앱끼리 구분이 안 됩니다.
- 저장할 폴더(`H:\source\repos\apks`)가 없으면 먼저 만듭니다.
- **분할 APK:** `pm path` 결과가 여러 줄(`base.apk` + `split_config.arm64_v8a.apk` 등)이면 모두 같은 폴더에 받습니다. 네이티브 라이브러리(`libil2cpp.so`)는 보통 `split_config.<abi>.apk` 쪽에 있습니다.
- **OBB 파일 확인:** 대형 Unity 게임은 `/sdcard/Android/obb/<패키지명>/main.<versionCode>.<패키지명>.obb`에 리소스를 둡니다. 아래 명령으로 유무를 확인해 두고, 나중에 복제 앱(`<패키지명>.mod`)을 만들 때 기기 내부에서 복사합니다.

```powershell
& "C:\Program Files (x86)\Nox\bin\nox_adb.exe" -s <기기ID> shell ls -la /sdcard/Android/obb/<패키지명>
```

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
| 0. APK 확보 · 버전 고정 | 실기기 우선 확인, 단일 `adb`(`nox_adb`) 유지, Play 자동 업데이트 해제 안내, `pm path` → `pull`, OBB 유무 확인 | `apk\` |
| 1. 압축 해제 | Windows 내장 bsdtar(`C:\Windows\System32\tar.exe`)로 APK를 풂 (Git Bash의 GNU tar는 zip을 못 풂) | `apk_unzipped\` |
| 2-A. IL2CPP | `Il2CppDumper` → 실패 시 `Cpp2IL` (force 옵션 + 숫자 형식 Unity 버전) → DummyDll을 `ilspycmd`로 디컴파일 → `il2cpp_asm`으로 ARM64 함수 주석 어셈블리 분석 | `il2cpp_dump\`, `src_dummy\`, `logs\*.asm`, `docs\hooks.md` |
| 2-B. Mono | `ilspycmd -p`로 `assets\bin\Data\Managed\Assembly-CSharp.dll` 디컴파일 | `src\` |
| 3. Java 쪽 | JADX로 패키지명·`versionCode`·`UnityPlayerActivity` 확장·ContentProvider(`authorities`)·SDK 브리지 확인 | `jadx_src\`, `docs\analysis.md` |
| 4. 원본 실행 검증 | 미설치 기기에서만 원본 설치(이미 설치된 정품 위에 덮어쓰기 금지) 후 `logcat -s Unity`로 런타임 로그 확인 | `logs\` |
| 5. Frida Gadget 복제 앱(`.mod`) + `script` 모드 | 매니페스트를 `<패키지명>.mod`로 분리 → `onCreate`에 `frida-gadget` 로드 + `libfrida-gadget.config.so`(`script` 모드) → `apktool b` + `uber-apk-signer` → 복제본 설치 및 OBB 복사 → `mod.js`(`frida-il2cpp-bridge`) push 및 검증 | `build\clone\`, `mod\dist\mod.js` |

---

## 5. 비루팅 기기 Unity IL2CPP 모딩 표준 우회법 (Frida Gadget 복제 앱 + `script` 모드)

안드로이드 IL2CPP 게임에서는 PC용 C# 로더(MelonLoader · LemonLoader 등)가 동작하지 않는 경우가 많고, 원본 APK를 수정해 재서명하면 정품과 서명이 달라져 원본 위에 덮어쓸 수 없습니다(`INSTALL_FAILED_UPDATE_INCOMPATIBLE`). 원본을 지우고 깔면 로컬 세이브(`allowBackup=false`)와 Google Play 로그인이 깨지므로, **원본 앱은 그대로 두고 패키지명을 `.mod`로 바꾼 복제 앱**을 옆에 설치하는 것이 표준입니다.

1. **복제 패키지(`.mod`) 매니페스트 분리:**
   - `apktool d`로 디코드한 `AndroidManifest.xml`에서 `package` 속성, 모든 `android:authorities`, `android:taskAffinity`, 자체 정의 `.permission.` 이름, 푸시/SDK host만 `<패키지명>.mod`로 변경합니다.
   - **주의:** 액티비티/서비스 클래스명(`com.unity3d.player.UnityPlayerActivity` 등)과 인텐트 액션 이름은 바꾸지 않습니다.
2. **Frida Gadget + `script` 모드 설정:**
   - 진입점 액티비티 Smali의 `onCreate` 맨 앞(annotation 직후)에 `System.loadLibrary("frida-gadget")`를 삽입합니다.
   - `tools\frida-gadget\libfrida-gadget-arm64.so`를 `lib\arm64-v8a\libfrida-gadget.so`로 복사하고, 같은 폴더에 `libfrida-gadget.config.so`를 아래 내용으로 만듭니다:
     ```json
     {"interaction":{"type":"script","path":"/storage/emulated/0/Android/data/<패키지명>.mod/files/mod.js","on_change":"reload"}}
     ```
   - PC 세션(`listen`) 모드는 앱이 백그라운드로 가거나 세션이 끝나면 훅이 풀리지만, `script` 모드는 PC 없이 기기 단독으로 매 실행 시 `mod.js`를 올리고 파일을 다시 `adb push`하면 즉시 리로드합니다.
3. **고정 키 서명 및 OBB 복사:**
   - `apktool b` 빌드 후 `uber-apk-signer`(기본 내장 디버그 키 또는 고정 키스토어)로 서명해 설치합니다. 같은 키를 유지하면 이후 복제본을 재빌드해도 세이브를 유지한 채 `install -r`로 업데이트됩니다.
   - 원본 게임에 OBB가 있으면 기기 안에서 `/sdcard/Android/obb/<패키지명>.mod/main.<versionCode>.<패키지명>.mod.obb`로 복사합니다.
4. **`mod.js` 작성 및 실기 주의사항 (`frida-il2cpp-bridge`):**
   - `Process.attachModuleObserver`는 Android 링커(`linker_phdr.cpp`) SIGABRT 크래시를 유발할 수 있으므로 `setInterval`(5ms)로 `Process.findModuleByName("libil2cpp.so")`를 폴링합니다.
   - 원본을 호출한 뒤 반환값만 바꾸는 `Interceptor.attach`에서 원본이 C# 예외(`Il2CppExceptionWrapper`)를 던지면 Frida 프레임을 넘지 못해 앱이 abort되므로, 예외 가능성이 있는 함수는 원본을 부르지 않는 `Interceptor.replace`를 씁니다.
   - `script` 모드에서는 `console.log`가 `logcat`에 나오지 않으며, 앱이 직접 만든 파일은 권한이 `0600`이라 `adb shell`에서 못 읽습니다. 먼저 `adb shell "touch .../files/mod.log && chmod 666 .../files/mod.log"`로 로그 파일을 만들어 두고 파일에 이어 쓰는 로거를 사용하세요 (`/proc/self/cmdline`에서 패키지명을 읽을 때는 끝의 NUL 바이트 때문에 `File.readAllText`가 예외를 던지니 `File.readAllBytes` 사용).

---

## 문제 해결

| 증상 | 원인 | 해결 |
|---|---|---|
| `nox_adb devices`에 아무것도 없음 | 녹스가 아직 부팅 중이거나 연결 전 | 안드로이드 홈 화면이 뜬 뒤 `nox_adb connect 127.0.0.1:62001` |
| 기기가 `offline`으로 표시 / Frida `TransportError` | PATH의 `adb`(41)와 `nox_adb`(36)를 섞어 써서 서버가 재시작됨 | `nox_adb` 단일 버전만 사용 (REA의 ADB 도구 호출 금지). USB 재연결 또는 `nox_adb kill-server` 후 재연결 |
| `adb server version (41) doesn't match this client (36); killing...` | PATH의 adb와 nox_adb를 섞어 씀 | 한쪽(`nox_adb`)만 사용. 위 2번 안내 참고 |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | 원본 앱 위에 서명이 다른 리패키징 APK를 `install -r`로 덮어씀 | 원본은 그대로 두고 패키지명·`authorities`를 `.mod`로 바꾼 복제 앱으로 설치. 복제본 재빌드 시에는 동일한 서명 키 유지 |
| 복제 앱 실행 시 리소스 누락 / 다운로드 화면 멈춤 | 복제 패키지 경로(`.mod`)에 OBB 파일이 없음 | `/sdcard/Android/obb/<패키지명>/main.<ver>.<패키지명>.obb`를 `/sdcard/Android/obb/<패키지명>.mod/main.<ver>.<패키지명>.mod.obb`로 기기 내 복사 |
| 시작 직후 `linker_phdr.cpp` SIGABRT 크래시 | Frida에서 `Process.attachModuleObserver` 사용 | `setInterval`(5ms)로 `Process.findModuleByName("libil2cpp.so")` 폴링으로 변경 |
| `script` 모드에서 `mod.log`를 `adb shell`로 못 읽음 (`Permission denied`) | 앱이 생성한 파일 권한이 `0600` | `adb shell`에서 `touch` + `chmod 666`으로 `mod.log`를 먼저 만든 뒤 앱이 이어 쓰게 함 |
| 기기 안에서 `cp -r`로 복사한 커스텀 에셋을 모드가 못 읽음 | 디렉터리 권한(`2770`) 문제 | 커스텀 에셋 폴더는 반드시 PC에서 `adb push`로 전송 |
| 다중 인스턴스 기기에 연결이 안 됨 | 포트가 다름 | 첫 인스턴스 `62001`, 이후 `62025`, `62026`… (`check_android_emulator_env.bat -Ports 62001,62025`) |
| `INSTALL_FAILED_NO_MATCHING_ABIS` | 인스턴스에 ARM 변환이 없음 | ABI 목록에 `arm64-v8a`가 있는 인스턴스(Android 9 64bit 등) 사용 |
| `INSTALL_FAILED_MISSING_SPLIT` | 분할 APK를 base만 설치 | 받은 APK를 모두 `install-multiple`로 설치 |
| 녹스가 느리거나 시작되지 않음 | Hyper-V 계열이 VT 점유 | 0번의 점검 bat 안내 참고 |

---

## 관련 문서
- [CODE_SNIPPETS.md 12장](CODE_SNIPPETS.md#12-배치-스크립트-bat--ps1-코드-스니펫) — 점검 bat의 VT 판별, 녹스 경로 찾기, ADB 파싱 코드
- [PROMPT_TEST.md](PROMPT_TEST.md) — 프롬프트 명령 검증 결과 (adb 버전 충돌 수정 및 Unity APK Frida Gadget 복제 앱 단계 포함)
