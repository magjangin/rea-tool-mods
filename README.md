# 🛠️ REA Tool Mods

> **Reverse Engineer Anything (REA) & Game Modding Workflow Helper**  
> Avalonia UI (.NET 10) 기반의 4대 핵심 타겟(EXE · DLL · APK · APP) 정밀 분석 및 AI 에이전트 연동 도우미

[![.NET Version](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-12.1.3-9A4993?style=flat-square&logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![REA Compatibility](https://img.shields.io/badge/REA-Compatible-1F6FEB?style=flat-square)](https://github.com/morluto/rea)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

---

## 📖 소개 (Introduction)

**REA Tool Mods**는 바이너리 분석, 안드로이드 모바일 리버스 엔지니어링, C# .NET/Mono 어셈블리 디컴파일, Electron 패키지 추출 작업을 손쉽게 수행할 수 있도록 돕는 데스크톱 GUI 유틸리티입니다.

대상 파일을 불러오면 적절한 **작업 폴더(Work Directory)**를 자동으로 구성하고, **디컴파일 도구 명령어 및 빌드/실행 검증 지침**이 포함된 최적의 프롬프트를 생성합니다. 또한 현재 PC 화면에 실행 중인 **Claude 앱** 및 **Antigravity 앱**으로 프롬프트를 원클릭으로 직접 전송(자동 포커스 + 붙여넣기 + 전송)할 수 있습니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 🎯 4대 핵심 분석 타겟 완벽 대응
- 🖥️ **EXE (실행 바이너리):** Native PE 분석, 엔트리포인트 및 의사코드(Pseudocode) 덤프, 스켈레톤 테스트 프로젝트 생성
- ⚙️ **DLL (동적 라이브러리 / 어셈블리):** `ilspycmd` 기반 C# 전체 프로젝트 소스 코드 디컴파일(`-p`), `dotnet build` 컴파일 및 후킹 포인트 분석
- 📱 **APK (안드로이드 앱):** Headless `JADX`를 통한 Java 소스 디컴파일 및 `Apktool` 리소스 추출, ADB 실행 준비
- 📦 **APP (Electron / ASAR):** `@electron/asar` 패키지 언팩, 메인-렌더러 IPC 채널 분석 및 `npx electron .` 로컬 구동

### 2. 📂 스마트 작업 디렉터리 자동 네이밍
대상 파일을 선택하면 기본 상위 경로(`H:\source\repos`)를 기준으로 파일명과 타겟 접미사를 결합한 전용 작업 폴더가 자동 생성됩니다:
- `MyGame.exe` ➔ `H:\source\repos\MyGame-windows`
- `Assembly-CSharp.dll` ➔ `H:\source\repos\Assembly-CSharp-dotnet`
- `app.apk` ➔ `H:\source\repos\app-android`
- `app.asar` ➔ `H:\source\repos\app-electron`

### 3. 🤖 열려있는 AI 앱으로 직접 전송 (Auto-Focus & Send)
단순한 텍스트 복사에 그치지 않고, Win32 창 제어(`WindowsAutomation`)를 통해 현재 화면에 열려있는 앱을 찾아 전송합니다:
- **Claude 앱에 보내기:** 현재 열린 Claude 창을 감지하여 전면 활성화 ➔ `Ctrl + V` ➔ `Enter` 자동 입력으로 채팅 전송
- **안티그래비티에 보내기:** 현재 열린 Antigravity 창을 감지하여 전면 활성화 ➔ `Ctrl + V` ➔ `Enter` 자동 입력으로 에이전트 전송

### 4. ⚡ 자동화 배치 스크립트 모음 (`bat/`)
- `download_analysis_tools.bat`: `ilspycmd`, `@electron/asar`, `JADX`, `Apktool` 필수 분석 도구 원클릭 다운로드
- `setup_rea_workflows.bat`: [morluto/rea](https://github.com/morluto/rea) 워크플로우 스킬 동기화 및 MCP 에이전트 연동
- `run_tool.bat`: Avalonia UI 앱 원클릭 실행기

---

## 📁 프로젝트 구조 (Project Structure)

```text
rea-tool-mods/
├── ReaToolMods.slnx                  # 루트 솔루션 파일 (.NET 10)
├── README.md                         # 프로젝트 소개 및 가이드 문서
├── .gitignore                        # 빌드 결과물 및 임시 파일 제외
│
├── bat/                              # 📁 편의용 배치(.bat) 스크립트 모음
│   ├── download_analysis_tools.bat   # 핵심 역공학 도구 자동 다운로더
│   ├── setup_rea_workflows.bat       # REA 워크플로우 동기화 & MCP 에이전트 설정
│   └── run_tool.bat                  # 앱 원클릭 실행 런처
│
├── ReaToolMods/                      # 📁 Avalonia MVVM 프로젝트
│   ├── ReaToolMods.csproj            # Avalonia 12.1.3 + CommunityToolkit.Mvvm
│   ├── App.axaml / App.axaml.cs      # 앱 스타일 및 FluentTheme
│   ├── Program.cs / ViewLocator.cs
│   ├── Services/
│   │   └── WindowsAutomation.cs      # Win32 창 검색 및 자동 타이핑/전송 서비스
│   ├── ViewModels/
│   │   └── MainViewModel.cs          # 타겟 자동 판별, 네이밍 및 프롬프트 생성 로직
│   └── Views/
│       └── MainWindow.axaml          # 와이드 반응형 UI 뷰
│
└── .agents/skills/                   # 📁 REA 에이전트 분석 스킬
    └── reverse-engineer-anything/    # 바이너리, 안드로이드, Electron 분석 명세
```

---

## 🚀 빠른 시작 (Getting Started)

### 요구 사양
- **OS:** Windows 10 / 11 (x64)
- **.NET SDK:** .NET 10 SDK (또는 .NET 8+)
- **Node.js:** v22+ (REA 에이전트 연동용)

### 실행 방법

1. **저장소 클론:**
   ```bash
   git clone https://github.com/magjangin/rea-tool-mods.git
   cd rea-tool-mods
   ```

2. **도구 다운로드 (선택 사항):**
   `bat/download_analysis_tools.bat`을 실행하여 `ilspycmd`, `JADX`, `Apktool`을 한 번에 준비합니다.

3. **앱 실행:**
   `bat/run_tool.bat`을 더블 클릭하거나 콘솔에서 아래 명령어를 실행합니다:
   ```bash
   dotnet run --project ReaToolMods
   ```

---

## 🔗 관련 프로젝트 (Related Links)
- **REA (Reverse Engineer Anything):** [morluto/rea](https://github.com/morluto/rea)
- **Avalonia UI:** [avaloniaui.net](https://avaloniaui.net/)
- **ICSharpCode.Decompiler (ilspycmd):** [icsharpcode/ILSpy](https://github.com/icsharpcode/ILSpy)
- **JADX Dex Decompiler:** [skylot/jadx](https://github.com/skylot/jadx)
