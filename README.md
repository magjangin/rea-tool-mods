# 🛠️ REA Tool Mods

> **Reverse Engineer Anything (REA) & Game Modding Workflow Helper**  
> Avalonia UI (.NET 10) 기반의 8종 타겟(EXE · DLL · APK · APP + Unity IL2CPP · Mono · APK · APP) 분석 및 AI 에이전트 연동 도우미

[![.NET Version](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-12.1.3-9A4993?style=flat-square&logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![REA Compatibility](https://img.shields.io/badge/REA-Compatible-1F6FEB?style=flat-square)](https://github.com/morluto/rea)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

---

## 📖 소개 (Introduction)

**REA Tool Mods**는 바이너리 분석, 안드로이드 모바일 리버스 엔지니어링, C# .NET/Mono 어셈블리 디컴파일, Unity IL2CPP 메타데이터 덤프, Electron 패키지 추출 작업을 손쉽게 수행할 수 있도록 돕는 데스크톱 GUI 유틸리티입니다.

대상 파일을 불러오면 **파일 내용을 검사해 타겟 종류를 자동 판별**하고, 적절한 **작업 폴더(Work Directory)**를 구성한 뒤 **디컴파일 도구 경로 · 단계별 지침 · 진행 기록 규칙**이 포함된 프롬프트를 생성합니다. 이미 작업하던 폴더가 있으면 **이어서 진행하는 프롬프트**를 만들고, 현재 PC 화면에 실행 중인 **Claude 앱** 및 **Antigravity 앱**으로 프롬프트를 원클릭 전송(자동 포커스 + 붙여넣기 + 전송)할 수 있습니다.

> ⚖️ 본인이 소유한 프로그램의 로컬·싱글플레이 분석과 모드 제작 용도로 사용하세요. 생성되는 프롬프트에는 안티치트/DRM 우회, 암호화된 바이너리·메타데이터 복호화를 하지 않도록 하는 규칙이 포함되어 있습니다.

## ⚠️ 사용 전에 꼭 읽어 주세요

> **🎮 가급적 게임 모딩에 쓰는 게 좋습니다.**
>
> **REA 자체에는 "이건 분석하면 안 된다"를 막는 장치가 없습니다.** 범위는 사용자가 정하고, 이 툴은 그 범위를 프롬프트 규칙으로 에이전트에게 전달할 뿐입니다.
> EXE · DLL · APK · APP도 다룰 수 있지만, 목적이 분명하지 않은 상용 소프트웨어 분석은 AI가 거절할 수 있고 법적·계정·보안 위험이 사용자에게 돌아옵니다.

- 🧪 **현재 상태:** 이 툴은 지금 **AI 모델에게 REA를 써 보게 하는 테스트 목적**으로 쓰고 있습니다. REA를 써도 되는 **정당한 목적을 찾는 즉시 바로 업데이트하겠습니다.** 지금은 아예 새 앱 만들기, 남의 기능을 참고해 내 앱에 붙이기처럼 허용되는 쪽을 빨리 찾으려고 노력 중입니다.
- 💭 **만든 사람의 가설:** 이런 리버싱이 정당해지려면 **관행이 확실하게 굳어지거나**, **AI 회사들의 코드 학습 관련 법적 문제**가 해결되어야 한다고 봅니다. 하지만 둘 다 현실적으로 어려워서, **다른 방법을 찾는 게 좋겠다**고 판단하고 지금 찾고 있습니다. ([자세히](docs/USAGE_SCOPE.md))
- 🔁 **수정과 클로닝:** 모딩은 원본 위에 얹는 **"수정"** 에 가깝고, 지금 말하는 리버싱은 원본과 같은 것을 다시 만드는 **"클로닝"** 에 가깝습니다. 정확히 엄청 위험한 것은 **REA를 이용한 클로닝**입니다. 반대로 원본 코드가 아닌 **아예 새 앱**을 만들거나, 남의 기능을 **참고만 해서** 내 앱에 직접 구현하는 것은 허용되는 쪽입니다 (디컴파일한 코드 · 리소스는 옮기지 않음). ([자세히](docs/USAGE_SCOPE.md#1-모딩은-수정-지금-말하는-리버싱은-클로닝))
- 🧭 **먼저 할 일:** 정말 REA를 쓰고 싶다면 **목적부터 확인**하고, 그 목적이 받아들여지는 구분에 들어가는지 **근거를 먼저 찾으세요.** 내가 구매한 게임을 개조하는 모딩도 법적으로는 회색지대지만, 개발사의 모드 정책과 커뮤니티 관행 덕분에 받아들여지는 쪽에 가깝습니다. 만든 사람인 저 역시 이 구분을 먼저 찾는 것이 가장 중요하다고 생각합니다. ([자세히](docs/USAGE_SCOPE.md#0-rea를-쓰기-전에-먼저-할-일))
- ✅ **해도 됨:** 본인이 구매·소유한 게임의 싱글플레이 모드 제작, 본인이 만든 프로그램·오픈소스 분석, 학습용 크랙미/CTF
- ⚠️ **목적을 밝히면 대체로 가능:** 구매한 상용 소프트웨어의 호환성·상호운용(파일 형식 읽기, 플러그인), 내 PC의 오류 원인 찾기 — 결과물은 배포하지 않음
- ❌ **하지 않음:** 라이선스·시리얼 제거(크랙), DRM·안티치트·서버 검증 우회, 온라인 게임 핵, 디컴파일 코드 재배포, 남의 계정·결제·개인정보에 닿는 분석
- 🤖 AI 에이전트(Claude 등)는 목적이 불분명하거나 위험한 리버싱 요청을 **거절하도록 설계**되어 있습니다. 거절은 버그가 아니라 정상 동작이므로, 우회하려고 하지 말고 **목적 · 소유 여부 · 결과물 사용처**를 밝히거나 작업을 멈추세요.
- 📋 **자세한 범위 안내** (REA가 하는 것과 하지 않는 것, 목적 문장 예시, 체크리스트): [docs/USAGE_SCOPE.md](docs/USAGE_SCOPE.md)
- 📚 **왜 그런지** 비유로 쉽게 풀어 쓴 설명: [docs/WHY_GAME_MODDING.md](docs/WHY_GAME_MODDING.md)

---

## ✨ 핵심 기능 (Key Features)

### 1. 🎯 8종 분석 타겟
| 구분 | 타겟 | 내용 | 작업 폴더 접미사 |
|---|---|---|---|
| 일반 | 🖥️ **EXE** | 네이티브 PE 분석(REA), 의사코드 덤프, 스켈레톤 프로젝트 | `-windows` |
| 일반 | ⚙️ **DLL** | `ilspycmd -p` C# 프로젝트 복원, `dotnet build`, 후킹 포인트 정리 | `-dotnet` |
| 일반 | 📱 **APK** | `JADX` Java 디컴파일 + `Apktool` 리소스 추출, ADB 실행 | `-android` |
| 일반 | 📦 **APP** | `@electron/asar` 언팩, IPC 분석, `npx electron .` 실행 | `-electron` |
| Unity | ⚡ **IL2CPP** | `Il2CppDumper`(실패 시 `Cpp2IL`) 덤프 → DummyDll 시그니처 → REA로 GameAssembly 분석 → 모드 스켈레톤 | `-unity-il2cpp` |
| Unity | 🐵 **Mono** | `Managed\Assembly-CSharp.dll` 디컴파일(읽기용) → BepInEx/MelonLoader + HarmonyX 모드 프로젝트 빌드 | `-unity-mono` |
| Unity | 📲 **Unity APK** | APK/XAPK 압축 해제 → `libil2cpp.so` + `global-metadata.dat` 덤프 또는 Mono 디컴파일 → 녹스 실행 검증 | `-unity-android` |
| Unity | 🍎 **Unity APP** | iOS IPA / macOS .app — FairPlay 암호화 확인 후 UnityFramework·GameAssembly.dylib 덤프 | `-unity-app` |

### 2. 🔎 파일 내용 기반 자동 판별 + 확장자 검사
확장자만 보지 않고 **파일 시그니처 · 주변 폴더 구조 · 압축 엔트리**를 확인합니다 (`Services/TargetDetector.cs`).
- `Game.exe` 옆에 `GameAssembly.dll` / `Game_Data\il2cpp_data\Metadata\global-metadata.dat` → **Unity IL2CPP** (메타데이터 버전 표시)
- `Game_Data\Managed\Assembly-CSharp.dll` → **Unity Mono**
- `.apk` / `.xapk` / `.apks` / `.aab` 안에 `lib/<abi>/libil2cpp.so` · `libunity.so` → **Unity APK** (ABI, 분할 패키지 여부 표시)
- `.exe` 옆에 `resources\app.asar` → **APP(Electron)**, .NET 관리 어셈블리 EXE → **DLL**
- Unity 버전(UnityPlayer.dll / globalgamemanagers), 설치된 **BepInEx · MelonLoader** 버전과 플러그인 수도 함께 표시
- 선택한 타겟 버튼과 파일이 맞지 않거나(예: Unity APK에 `.exe`), 확장자와 실제 시그니처가 다르거나(예: PE를 `.apk`로 저장), `global-metadata.dat`가 비표준(암호화/보호) 헤더면 **경고**를 표시합니다.

### 3. ♻️ 기존 작업 폴더 이어하기
- 작업 폴더가 이미 있고 산출물이 있으면, 프롬프트가 **"PROGRESS.md를 먼저 읽고 끝난 단계는 건너뛰고 이어서 진행"** 모드로 생성됩니다 (최근 기록 20줄 포함).
- 기존 산출물은 덮어쓰지 않고 `src_v2`처럼 새 폴더로 출력하도록 지시합니다.
- 전송 시 `.reatool/workspace.json`(대상 경로 · 크기 · 수정 시각)을 남겨, 다음 번에 **대상 파일이 바뀌었으면 버전 비교부터** 하도록, **다른 대상의 폴더면 멈추고 확인**하도록 알려 줍니다.
- 접미사가 바뀌어도(예: 예전에 `-windows`로 만든 Unity 게임 폴더) 같은 대상을 다루던 `<이름>-*` 폴더를 찾아 이어서 사용합니다.
- `작업 폴더 선택...`으로 기존 폴더를 고르면 이전 대상 파일과 타겟 종류를 복원합니다.
- 전송한 프롬프트는 `.reatool/prompts/yyyyMMdd-HHmmss-<agent>.md`로 시간순 보관됩니다.

### 4. 🧰 환경 점검 (도구 · 에뮬레이터 · REA 연동 · 게임 모드 로더)
앱 시작 시 자동 실행되며 `🔍 다시 점검`으로 갱신합니다 (`Services/EnvironmentChecker.cs`).
- **분석 도구:** .NET SDK, ilspycmd, Java(11+ 확인, 잘못된 `JAVA_HOME` 감지), JADX, Apktool, Node.js, asar, Il2CppDumper, Cpp2IL — 실제 실행해 버전 확인
- **Android · 에뮬레이터:** CPU 가상화(VT-x/AMD-V, Hyper-V 점유 여부), 녹스 설치/실행, ADB, PATH adb ↔ nox_adb 버전 충돌 주의
- **REA · 에이전트:** REA CLI, REA 스킬, REA MCP 등록(Claude Code / Claude 앱 설정), Claude 앱 · Antigravity 설치
- **대상 게임 플러그인:** Unity 빌드 정보, BepInEx / MelonLoader 설치 여부
- 현재 타겟에 필요한 도구 중 빠진 것을 요약하고, 프롬프트에도 실제 도구 경로와 누락 도구 안내가 들어갑니다.

### 5. 🤖 열려있는 AI 앱으로 직접 전송 (Auto-Focus & Send)
Win32 창 제어(`WindowsAutomation`)로 현재 열린 앱을 찾아 `Ctrl + V` ➔ `Enter`를 자동 입력합니다. 창이 없으면 앱을 실행하고 최대 10초간 창을 기다립니다.

### 6. ⚡ 자동화 배치 스크립트 모음 (`bat/`)
- `download_analysis_tools.bat`: `ilspycmd`, `@electron/asar`, `JADX`, `Apktool`, **`Il2CppDumper`**(비대화식 실행 설정 포함), **`Cpp2IL`** 다운로드
- `check_android_emulator_env.bat`: **CPU 가상화(VT) · Hyper-V 계열 기능 · 녹스 설치/실행 · ADB 연결 · 기기 ABI** 점검 (종료 코드 0=준비됨, 1=문제, 2=경고)
- `setup_rea_workflows.bat`: [morluto/rea](https://github.com/morluto/rea) 워크플로우 스킬 동기화 및 MCP 에이전트 연동
- `run_tool.bat`: Avalonia UI 앱 원클릭 실행기
- `run_prompt_tests.bat`: 8종 타겟 × 작업 폴더 상태별 프롬프트 생성 검증 + 프롬프트 속 명령을 실제 도구로 실행 ([결과](docs/PROMPT_TEST.md))

---

## 📁 프로젝트 구조 (Project Structure)

```text
rea-tool-mods/
├── ReaToolMods.slnx                      # 루트 솔루션 파일 (.NET 10)
├── README.md
├── docs/
│   ├── CODE_SNIPPETS.md                  # 핵심 코드 스니펫 (C# · bat · ps1)
│   ├── PROMPT_TEST.md                    # 프롬프트 테스트 결과 · 발견한 버그 · 샘플 프롬프트
│   ├── NOX_WORKFLOW.md                   # 녹스로 Unity APK 꺼내기 · 판별 · 분석 순서 · 문제 해결
│   ├── USAGE_SCOPE.md                    # 사용 범위 안내 (REA 동작 · 용도별 허용 범위 · 목적 문장 예시)
│   └── WHY_GAME_MODDING.md               # 게임 모딩을 권장하는 이유 (비유 설명)
│
├── bat/
│   ├── download_analysis_tools.bat       # 분석 도구 자동 다운로더 (Unity IL2CPP 도구 포함)
│   ├── check_android_emulator_env.bat    # VT · 녹스 · ADB 점검 (→ .ps1 실행)
│   ├── check_android_emulator_env.ps1
│   ├── setup_rea_workflows.bat           # REA 워크플로우 동기화 & MCP 에이전트 설정
│   ├── run_prompt_tests.bat              # 프롬프트 테스트 실행
│   └── run_tool.bat                      # 앱 원클릭 실행 런처
│
├── ReaToolMods/                          # Avalonia MVVM 프로젝트
│   ├── Models/
│   │   ├── TargetProfile.cs              # 8종 타겟 정의 (라벨 · 접미사 · 허용 확장자 · 필요 도구)
│   │   ├── DetectionResult.cs            # 자동 판별 결과 (Unity 버전 · 백엔드 · ABI · 모드 로더)
│   │   └── ToolLocation.cs
│   ├── Services/
│   │   ├── TargetDetector.cs             # 시그니처 · 폴더 구조 · 압축 엔트리 기반 판별 및 검증
│   │   ├── PromptBuilder.cs              # 머리말 + 작업 폴더 상태 + 도구 + 타겟별 단계 + 공통 규칙
│   │   ├── WorkspaceInspector.cs         # 기존 작업 폴더 / PROGRESS.md / .reatool 메타데이터
│   │   ├── EnvironmentChecker.cs         # 도구 · VT · 녹스 · REA · 모드 로더 점검
│   │   ├── ToolLocator.cs                # 도구 경로 탐색 (tools/, PATH, 녹스 설치 경로)
│   │   ├── AgentLauncher.cs              # Claude / Antigravity 전송 공통 흐름
│   │   ├── ClipboardService.cs
│   │   └── WindowsAutomation.cs          # Win32 창 검색 및 자동 붙여넣기/전송
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── TargetButtonViewModel.cs
│   └── Views/
│       └── MainWindow.axaml
│
├── tests/PromptTests/                    # 프롬프트 생성 · 명령 실행 · ViewModel 흐름 테스트 (콘솔)
│
└── .agents/skills/                       # REA 에이전트 분석 스킬
```

---

## 🚀 빠른 시작 (Getting Started)

### 요구 사양
- **OS:** Windows 10 / 11 (x64)
- **.NET SDK:** .NET 10 SDK
- **Node.js:** v22+ (REA 에이전트 연동용)
- **Java:** JDK 17+ (JADX · Apktool 실행용)
- **(Unity APK)** CPU 가상화(VT) 활성화 + 녹스(NoxPlayer)

### 실행 방법

1. **저장소 클론:**
   ```bash
   git clone https://github.com/magjangin/rea-tool-mods.git
   cd rea-tool-mods
   ```

2. **도구 다운로드:** `bat/download_analysis_tools.bat` 실행 (또는 앱의 `⬇️ 분석 도구 설치` 버튼)

3. **(Unity APK 작업 시) 에뮬레이터 점검:** `bat/check_android_emulator_env.bat` 실행 (또는 앱의 `📱 VT · 녹스 점검` 버튼). APK 꺼내기부터 분석까지의 순서는 [docs/NOX_WORKFLOW.md](docs/NOX_WORKFLOW.md) 참고

4. **앱 실행:** `bat/run_tool.bat`을 더블 클릭하거나:
   ```bash
   dotnet run --project ReaToolMods
   ```

---

## 🔗 관련 프로젝트 (Related Links)
- **REA (Reverse Engineer Anything):** [morluto/rea](https://github.com/morluto/rea)
- **Avalonia UI:** [avaloniaui.net](https://avaloniaui.net/)
- **ICSharpCode.Decompiler (ilspycmd):** [icsharpcode/ILSpy](https://github.com/icsharpcode/ILSpy)
- **JADX Dex Decompiler:** [skylot/jadx](https://github.com/skylot/jadx)
- **Il2CppDumper:** [Perfare/Il2CppDumper](https://github.com/Perfare/Il2CppDumper)
- **Cpp2IL:** [SamboyCoding/Cpp2IL](https://github.com/SamboyCoding/Cpp2IL)
