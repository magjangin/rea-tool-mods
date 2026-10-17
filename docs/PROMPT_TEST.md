# 프롬프트 테스트

생성된 프롬프트가 **상황에 맞는 지시를 담는지**, 그리고 프롬프트에 적힌 **명령이 실제 도구에서 그대로 동작하는지**를 검증합니다.

- 테스트 코드: [`tests/PromptTests/Program.cs`](../tests/PromptTests/Program.cs)
- 실행: `bat\run_prompt_tests.bat` 또는

```bash
dotnet run --project tests/PromptTests
```

- 결과 표(`report.md`)와 타겟별 샘플 프롬프트는 `%TEMP%\reatool-prompttest\out`에 저장됩니다.
- 픽스처는 테스트가 직접 만드는 **가짜 바이너리**입니다 (시스템의 `where.exe` · `version.dll` 복사본, 헤더만 맞춘 `global-metadata.dat`, 직접 만든 APK/IPA zip). 그래서 덤프 자체는 실패하는 것이 정상이고, **인자 순서 · 옵션 파싱 · 단계 간 경로 일치**를 확인합니다.
- 픽스처는 `Games With Space` 폴더에 두어 **공백이 들어간 경로의 따옴표 처리**도 함께 검증합니다.

---

## 결과 요약 (2026-10-17, `pe_slice` · 타임아웃 규칙 추가 후 재실행)

| 영역 | 내용 | 결과 |
|---|---|---|
| A. 판별 | 8종 픽스처 + 앱 실행 별칭이 올바른 타겟으로 판별되는지 | ✅ 8 / 8 + 별칭 1 |
| A. 생성 | 8종 타겟 × 5가지 작업 폴더 상태 = 40개 프롬프트 검증 | ✅ 40 / 40 |
| B. 실행 | 프롬프트의 명령을 그대로 실행 | ✅ 16 통과 · ⏭️ 1 건너뜀 |
| C. ViewModel | 앱 흐름 (판별 → 폴더 → 경고 → 이어하기 → 재사용) | ✅ 12 통과 · ⏭️ 1 건너뜀 |
| **합계** | | **77 통과 · 0 실패 · 2 건너뜀** |

---

## A. 생성 매트릭스 — 40개 프롬프트

작업 폴더 상태 5가지를 만들어 놓고 8종 타겟 각각의 프롬프트를 생성해 검사했습니다.

| 상태 | 준비한 작업 폴더 | 프롬프트에 있어야 하는 것 |
|---|---|---|
| `new` | 없음 | "폴더가 아직 없습니다" + PROGRESS.md 생성 지시, 이어하기 문구 없음 |
| `empty` | 빈 폴더 | "폴더가 비어 있습니다" |
| `resume` | `il2cpp_dump\` + `PROGRESS.md` + 같은 대상의 `workspace.json` | "기존 작업에 이어서", PROGRESS.md 먼저 읽기, 최근 기록 첨부, `src_v2` 덮어쓰기 금지, 변경 경고는 **없어야 함** |
| `changed` | resume + 기록된 파일 크기가 현재와 다름 | "대상 파일이 바뀌었습니다" |
| `differs` | resume + 기록된 대상 경로가 다른 파일 | "분석용으로 만들어졌습니다" (진행 전 확인 요청) |

모든 프롬프트에 공통으로 검사한 것:
- 머리말 `# [타겟명]`, 대상 파일 · 작업 디렉터리 경로, 필수 섹션 5개, 범위 규칙
- 치환되지 않은 `{변수}`나 객체 이름(`ReaToolMods.…`)이 노출되지 않음
- 타겟의 필요 도구가 "사용할 도구"에 모두 나오고, **단계에서 쓰는 도구는 반드시 필요 도구 목록에 있음** (경로 안내 누락 방지)
- Unity PC 타겟은 `<게임…>` 자리표시자가 남지 않고, Unity APP은 덤프 명령에 실제 `.app` 경로가 들어감
- Unity APK는 Cpp2IL에 숫자 형식 Unity 버전(`2022.3.10`)이 전달됨
- 모든 타겟: 타임아웃은 "같은 오류 2번이면 멈춤" 규칙에서 빠지고, 다시 하라고 하면 거절하지 말고 나눠서 다시 하라는 문구가 있음
- EXE · Unity IL2CPP: "10MB가 넘는 PE는 통째로 열지 마" 안내가 있고(다른 타겟엔 없어야 함), IL2CPP는 `REA로 GameAssembly.dll을 열고` 지시가 없고 `pe_slice`가 1단계 덤프의 `script.json`을 가리킴
- 모든 타겟: 덤프·디컴파일처럼 오래 걸리는 명령은 로그를 남기며 백그라운드로 실행하라는 규칙이 있음
- Unity Mono: 전체 디컴파일 동안 `ilspycmd -l c`(클래스 목록) · `ilspycmd -t <클래스>`(한 클래스만)로 바로 볼 수 있다는 안내
- Unity APK · APP: "REA는 PE만 열 수 있어" 안내가 있고(다른 타겟엔 없어야 함), REA로 네이티브 라이브러리를 열라는 지시(`REA MCP 도구(open_binary…`, `REA inspect_macho로`, `REA로 UnityFramework`)가 없고, `il2cpp_asm`이 덤프 산출물(`dump.cs` · `script.json`)을 가리킴. APP은 `il2cpp_asm --check-encryption` 명령이 있음

---

## B. 프롬프트 명령 실제 실행

프롬프트에서 `` `도구이름 …` `` 형태의 명령을 찾아 도구 이름만 실제 경로로 바꾼 뒤 `cmd /c`로 그대로 실행했습니다.

| 타겟 | 실행한 명령 | 확인한 것 | 결과 |
|---|---|---|---|
| DLL | `ilspycmd -p -o src -r <dir> <dll>` | 종료 코드 0, `.csproj` 1개 + `.cs` 56개 생성 | ✅ |
| Unity Mono | `ilspycmd -p -o src -r <Managed> <Managed>\Assembly-CSharp.dll` | 종료 코드 0, `.cs` 생성 | ✅ |
| Unity APK | `"C:\Windows\System32\tar.exe" -xf <apk> -C apk_unzipped` | 압축 해제 성공 | ✅ |
| Unity APK | (경로 검사) | 다음 단계 덤프 명령이 가리키는 `libil2cpp.so` · `global-metadata.dat`가 압축 해제 결과에 실제로 존재 | ✅ |
| Unity APK | `Il2CppDumper <libil2cpp.so> <metadata> <out>` | 출력에 `Initializing metadata` + `Metadata Version: 31` → 인자 순서 맞음 | ✅ |
| Unity APK | `Cpp2IL --game-path … --force-binary-path … --force-metadata-path … --force-unity-version … --skip-analysis --disable-registration-prompts --output-root …` | 옵션 파싱 통과 (`Using force options` 출력, 이후 가짜 바이너리라 LibCpp2IL 초기화 실패) | ✅ |
| Unity APK | `il2cpp_asm --binary <libil2cpp.so> --dump <dump.cs> --script-json <script.json> --class Fake --out <logs\Fake.asm>` | 픽스처 `libil2cpp.so`는 손으로 인코딩한 AArch64 ELF(adrp/ldr · adrp/add · bl · ret). 테스트가 만든 dump.cs·script.json으로 `→ Fake$$Helper`, `[Fake_TypeInfo]`, `StringLiteral "hello"` 주석 확인 | ✅ |
| Unity IL2CPP | (경로 검사) | `GameAssembly.dll` · `global-metadata.dat` 경로 존재 | ✅ |
| Unity IL2CPP | `Il2CppDumper <GameAssembly.dll> <metadata> <out>` | `Initializing metadata` → `Metadata Version: 29` → `Initializing il2cpp file` | ✅ |
| Unity IL2CPP | `Cpp2IL --game-path … --exe-name … --skip-analysis --disable-registration-prompts --output-root …` | 옵션 파싱 통과 후 본 처리 단계에서 실패 | ✅ |
| Unity IL2CPP | `pe_slice --dll <GameAssembly.dll> --script-json <il2cpp_dump\script.json> --class Fake --out <slices\Fake.dll>` | 테스트가 만든 script.json(엔트리 RVA에 `Fake$$Run`)으로 조각 PE 생성, 종료 코드 0, `MZ` 헤더 + export 이름 포함 | ✅ |
| APP | `asar extract <resources\app.asar> extracted` | `asar pack`으로 만든 실제 asar에서 `main.js` 추출 | ✅ |
| Unity APP | `tar.exe -xf <ipa> -C unpacked` + 경로 검사 | iOS 덤프 명령의 `Payload\Game.app\Frameworks\UnityFramework.framework\UnityFramework`와 metadata가 실제로 존재 | ✅ |
| Unity APP | `il2cpp_asm --check-encryption <UnityFramework>` | 덤프 명령과 같은 파일을 가리키고, 픽스처 Mach-O(arm64 + `LC_ENCRYPTION_INFO_64`)에서 `cryptid=0` · 종료 코드 0 | ✅ |
| APK | `jadx -d <out> <apk>` | JADX 1.5.6이 인자를 받아 입력을 처리 (가짜 dex라 `No classes to decompile`) | ✅ |
| APK | `apktool d -f -o <out> <apk>` | Apktool 3.0.3이 인자를 받아 디코딩 시작 (가짜 매니페스트에서 중단) | ✅ |
| APK · Unity APK | `adb install` · `pm path` · `logcat` | — | ⏭️ 녹스·기기 연결 필요 |

REA MCP 도구 이름(`open_binary`, `binary_overview`, `list_procedures`, `procedure_pseudo_code`, `inspect_macho`)은 연결된 REA 서버의 실제 도구 목록과 대조해 확인했습니다.

---

## C. ViewModel 흐름

`MainViewModel`을 UI 없이 생성해 사용자가 하는 동작을 그대로 흉내 냈습니다.

| 동작 | 기대 결과 | 결과 |
|---|---|---|
| 대상 경로 입력 (IL2CPP 게임 exe) | Unity IL2CPP로 자동 판별, 해당 버튼 선택 + 🔎 표시 | ✅ |
| 〃 | 작업 폴더 = `H:\source\repos\IlGame-unity-il2cpp` | ✅ |
| 〃 | 프롬프트에 판별된 `GameAssembly.dll` 경로 | ✅ |
| Mono 버튼 클릭 | "자동 판별 결과는 [Unity IL2CPP]" 경고, 폴더 접미사 `-unity-mono` | ✅ |
| Unity APK 버튼 클릭 (.exe 상태) | "'.exe' 파일이 맞지 않습니다" 경고 | ✅ |
| 작업 폴더 직접 입력 (PROGRESS.md 있는 폴더) | 이어하기 프롬프트 + "기존 작업 폴더" 상태 표시 | ✅ |
| 이후 EXE 버튼 클릭 | 직접 고른 작업 폴더 유지 | ✅ |
| 프롬프트를 직접 고친 뒤 환경 재점검 | 고친 내용 유지 | ✅ |
| 🔄 다시 생성 | 새로 생성 | ✅ |
| v1.3 폴더(`<이름>-windows` + `claude_prompt.md`)가 있을 때 | 그 폴더를 이어서 사용 | ✅ |
| 다른 접미사 폴더에 같은 대상의 `workspace.json`이 있을 때 | 그 폴더를 이어서 사용 | ✅ |
| 작업 폴더 선택 → 이전 대상 복원 | — | ⏭️ 폴더 선택 대화상자(UI) 필요. 같은 `LoadMetadata` 로직은 위 재사용 테스트로 확인 |

> 재사용 테스트는 `H:\source\repos`에 `ReaToolPromptTest*` 임시 폴더를 만들었다가 끝나면 지웁니다.

---

## 테스트로 발견해 고친 버그

| # | 문제 | 원인 | 수정 |
|---|---|---|---|
| 1 | Unity APK/APP 압축 해제 명령이 실패할 수 있음 | 프롬프트가 `tar`만 썼는데, Git Bash(Claude Code가 Windows에서 쓰는 셸)의 `tar`는 **GNU tar라 zip(APK/IPA)을 못 풂**. 이 PC는 cmd에서도 Git의 tar가 먼저 잡힘 | Windows 내장 bsdtar를 전체 경로(`"C:\Windows\System32\tar.exe"`)로 지정 |
| 2 | Unity APK의 Cpp2IL 재시도 명령이 즉시 크래시 | Cpp2IL 2022.0.7은 `--force-unity-version`을 숫자로만 파싱 → `2022.3.10f1`이면 `FormatException` | `2022.3.10`처럼 숫자 부분만 전달 |
| 3 | Cpp2IL 명령에 출력 위치가 없고, 입력 대기로 멈출 수 있음 | `--output-root` 누락, 주소를 못 찾으면 묻는 프롬프트, 오류 시 Enter 대기 | `--output-root` · `--skip-analysis` · `--disable-registration-prompts` 추가, 입력을 닫고 실행하라는 안내 추가 |
| 4 | APP 언팩 명령이 설치 확인 질문에서 멈출 수 있음 | `npx @electron/asar`는 로컬 패키지가 없으면 설치 여부를 물을 수 있음 | 설치된 `asar`를 직접 사용 (환경 점검으로 설치 확인) |
| 5 | Unity APP 프롬프트에 `<앱>.app` 자리표시자가 남음 | IPA 안의 앱 번들 이름을 쓰지 않음 | 판별된 메타데이터 경로에서 `.app` 위치를 계산해 실제 경로로 채우고, iOS/macOS 중 해당 명령만 표시 |
| 6 | 프롬프트의 `adb`가 녹스 연결을 끊을 수 있음 (녹스 실행 후 환경 점검에서 발견) | `adb`가 PATH의 platform-tools 1.0.41로 잡혔는데 녹스는 `nox_adb` 1.0.36을 씀 → 버전이 다르면 서로 adb 서버를 재시작 | 녹스가 설치되어 있으면 `nox_adb`를 우선 사용, 버전이 다를 때만 환경 점검에 경고 표시 |
| 7 | IL2CPP 3단계에서 에이전트가 타임아웃 후 다시 하기를 거절 (실제 게임 KAMITSUBAKI CITY ENSEMBLE에서 발견) | 프롬프트가 "REA로 GameAssembly.dll을 열고"라고 시킴 → REA(Windows Ghidra)가 70MB 전체를 자동 분석하느라 MCP 시간 제한 초과. 또 "같은 오류 2번이면 멈춤" 규칙 때문에 다시 하라고 해도 거절 | `pe_slice`로 함수 주변만 담은 조각 PE를 만들어 열게 바꿈(실측 1분 이내). 타임아웃은 멈춤 규칙에서 빼고, 나눠서 다시 하라고 명시 |
| 8 | `--method "Class$$Method"`가 셸에서 깨짐 | PowerShell·bash는 큰따옴표 안의 `$$`를 변수로 바꿈 | `Class::Method` 형식을 받게 하고 프롬프트도 `::`로 안내 |
| 9 | Unity APP 프롬프트가 이 PC에서 쓸 수 없는 도구를 시킴 → 에이전트가 "도구 없음"으로 멈추기 쉬움 | `inspect_macho`는 macOS 전용이고, Windows REA(Ghidra)는 PE만 열어 Mach-O를 거부 | 암호화 확인은 `il2cpp_asm --check-encryption`, 로직 분석은 `il2cpp_asm` 주석 어셈블리로 바꿈 |
| 10 | Unity APK 프롬프트에 네이티브 로직 분석 방법이 없음 | `libil2cpp.so`는 ARM64 ELF라 Windows REA로 열 수 없음 | `il2cpp_asm`으로 dump.cs 오프셋에서 바로 디스어셈블, REA로 열지 말라고 명시 |
| 11 | 큰 게임에서 덤프·디컴파일 명령이 셸 시간 제한에 걸림 | Il2CppDumper · `ilspycmd -p` · jadx가 몇 분 걸림 | 로그를 남기며 백그라운드로 실행하라는 공통 규칙 추가, Mono는 `ilspycmd -t`로 클래스 단위 즉시 확인 |

(이번 점검 중 별도로 `download_analysis_tools.bat`의 apktool 래퍼에 다운로드 로그가 섞이는 버그도 수정했습니다 — [CODE_SNIPPETS.md 12.5](CODE_SNIPPETS.md#125-if--블록-안에서-파일-쓰기--apktoolbat-래퍼-버그-수정).)

---

## 아직 검증하지 않은 것

| 항목 | 이유 | 검증 방법 |
|---|---|---|
| ADB 명령 (`pm path`, `pull`, `install`, `logcat`) | 기기 상태에 따라 결과가 달라 자동 테스트에 넣지 않음 | 녹스 연결과 `pm path`는 수동으로 확인함 (`127.0.0.1:62001`, Android 9, ABI에 arm64-v8a 포함). `bat\check_android_emulator_env.bat`으로 언제든 확인 |
| 실제 게임에서의 덤프 결과 | 가짜 바이너리로는 인자·경로까지만 확인 가능 | 본인이 가진 Unity 게임을 대상으로 앱에서 프롬프트를 만들어 에이전트에 보내 보기 |
| 에이전트가 지시를 따르는지 | 사람/에이전트의 실제 실행이 필요 | 위와 같음. 결과는 작업 폴더의 `PROGRESS.md`와 `logs\`로 확인 |

---

## 샘플: Unity IL2CPP · 이어하기 프롬프트

경로 중 사용자 폴더는 `%USERPROFILE%`, 테스트 픽스처 폴더는 `D:\ReaTest`, 저장소 위치는 `<repo>`로 바꿔 표시했습니다.

````markdown
# [Unity IL2CPP] Unity IL2CPP 메타데이터 덤프 및 모드 스켈레톤 요청
- 대상 파일: D:\ReaTest\Games With Space\IlGame\IlGame.exe
- 작업 디렉터리: D:\ReaTest\Work Dirs\UnityIl2Cpp-resume
- 자동 판별: Unity IL2CPP — GameAssembly.dll · global-metadata.dat (v29) · UnityPlayer.dll
- Unity 버전: 2022.3.10f1
- 스크립팅 백엔드: IL2CPP
- 게임 루트: D:\ReaTest\Games With Space\IlGame

## 0. 작업 폴더 상태
- **이미 작업하던 폴더입니다.** 기존 항목: il2cpp_dump\, PROGRESS.md
- 처음부터 다시 하지 말고 **기존 작업에 이어서** 진행해줘:
  1. 먼저 `PROGRESS.md`과 기존 산출물을 읽고, 완료된 단계 / 남은 단계 / 막혔던 지점을 3~5줄로 요약한 뒤 시작해줘.
  2. 기존 산출물 폴더는 삭제하거나 덮어쓰지 마. 다시 생성해야 하면 `src_v2`처럼 새 폴더로 출력하고 이유를 기록해줘.
  3. 아래 '수행 지침' 중 이미 끝난 단계는 산출물이 실제로 있는지만 확인하고 건너뛰어줘.
- `PROGRESS.md` 최근 기록:
```
# Progress
- [완료] 1. 덤프 — il2cpp_dump\
- [진행중] 2. 시그니처 탐색
```

## 사용할 도구
수행 지침의 명령에 쓰인 도구 이름(`ilspycmd`, `adb` 등)은 아래 경로로 실행해줘.
- Il2CppDumper (`Il2CppDumper`): `<repo>\tools\il2cppdumper\Il2CppDumper.exe`
- Cpp2IL (`Cpp2IL`): `<repo>\tools\cpp2il\Cpp2IL.exe`
- ilspycmd (`ilspycmd`): `%USERPROFILE%\.dotnet\tools\ilspycmd.EXE`
- .NET SDK (`dotnet`): `C:\Program Files\dotnet\dotnet.EXE`
- REA CLI (`rea`): `%USERPROFILE%\AppData\Roaming\npm\rea.CMD`
- PE 조각 추출기 (`pe_slice`): `<repo>\bat\pe_slice.bat`
- IL2CPP 주석 디스어셈블러 (`il2cpp_asm`): `<repo>\bat\il2cpp_asm.bat`
- REA MCP 도구(open_binary, binary_overview, procedure_pseudo_code 등)가 연결되어 있으면 정적 분석에 우선 사용해줘.
- ⚠️ 10MB가 넘는 PE(GameAssembly.dll 등)는 REA `open_binary`로 통째로 열지 마. Ghidra가 파일 전체를 자동 분석하느라 응답이 MCP 시간 제한을 넘겨 타임아웃이 나. 보고 싶은 함수만 `pe_slice`로 조각 PE(수백 KB)를 만들어 열면 1분 안에 의사코드가 나와.
- REA가 앞선 호출 때문에 계속 응답이 없으면 `binary_session`으로 상태를 확인하고, 그래도 막혀 있으면 REA MCP 서버를 다시 시작해 달라고 내게 말해줘.
- 경로에 공백이 있으므로 PowerShell에서는 `& "경로" 인자` 형태로 실행해줘.

## 수행 지침
1. IL2CPP 메타데이터 덤프 (metadata v29)
   `Il2CppDumper "D:\ReaTest\Games With Space\IlGame\GameAssembly.dll" "D:\ReaTest\Games With Space\IlGame\IlGame_Data\il2cpp_data\Metadata\global-metadata.dat" "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\il2cpp_dump"`
   - 산출물: DummyDll\, dump.cs, script.json, il2cpp.h
   - Il2CppDumper가 실패하거나 메타데이터 버전을 지원하지 않으면 Cpp2IL로 재시도해줘 (DummyDll만 생성. 오류 시 Enter를 기다리니 입력을 닫고 실행 — bash `< /dev/null`, cmd `< NUL`):
     `Cpp2IL --game-path "D:\ReaTest\Games With Space\IlGame" --exe-name "IlGame" --skip-analysis --disable-registration-prompts --output-root "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\cpp2il_out"`
   - 게임 폴더에 BepInEx\interop 또는 MelonLoader\Il2CppAssemblies가 이미 있으면 그 어셈블리를 우선 사용해줘.
   - global-metadata.dat 시그니처가 표준(0xFAB11BAF)이 아니면 보호된 메타데이터이므로 복호화하지 말고 보고 후 멈춰줘.
2. 시그니처 탐색
   `ilspycmd -p -o "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\src_dummy" "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
   - DummyDll은 메서드 본문이 비어 있으므로 클래스/필드/메서드 시그니처와 RVA 주석만 참고해줘.
3. 네이티브 로직 분석 (GameAssembly.dll 원본은 REA로 통째로 열지 마 — 타임아웃 남)
   - 관심 클래스(또는 메서드)만 조각 PE로 만들어줘. script.json 이름이 export로 들어가서 의사코드에 메서드·문자열 이름이 그대로 보여:
     `pe_slice --dll "D:\ReaTest\Games With Space\IlGame\GameAssembly.dll" --script-json "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\il2cpp_dump\script.json" --class <클래스명> --out "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\slices\<클래스명>.dll"`
   - 메서드 하나만 볼 땐 `--class` 대신 `--method "<클래스명>::<메서드명>"`(셸이 `$$`를 변수로 바꾸니 `::`로 써줘), 주소로는 `--rva 0x<dump.cs의 RVA>`. 여러 번 써서 한 조각에 묶어도 돼.
   - 출력 끝의 'REA 다음 단계'대로 REA `open_binary`(조각 PE) → `procedure_pseudo_code`(출력된 VA)로 의사코드를 받아 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\logs\<메서드명>.rea.c'에 저장해줘.
   - 조각 PE에는 호출 대상 함수의 앞부분만 들어 있어. 호출 대상이 궁금하면 그 함수로 조각을 새로 만들어줘.
   - REA가 연결되어 있지 않으면 같은 클래스를 주석 달린 어셈블리로 봐줘 (몇 초, 의사코드 대신 어셈블리):
     `il2cpp_asm --binary "D:\ReaTest\Games With Space\IlGame\GameAssembly.dll" --dump "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\il2cpp_dump\dump.cs" --script-json "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\il2cpp_dump\script.json" --class <클래스명> --out "D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\logs\<클래스명>.asm"`
   - 후킹 후보(메서드명, RVA, 역할)를 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\docs\hooks.md'에 표로 정리해줘.
4. 모드 스켈레톤
   - 게임에 모드 로더가 없습니다. BepInEx 6 (IL2CPP) 또는 MelonLoader 중 무엇을 쓸지 제안만 하고, 게임 폴더에 설치하는 건 내 확인을 받은 뒤 진행해줘.
   - 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\mod'에 플러그인 프로젝트를 만들고 interop 어셈블리를 참조해 `dotnet build` 성공까지 확인해줘.
5. 실행 검증
   - 게임 실행 후 BepInEx\LogOutput.log 또는 MelonLoader\Latest.log에서 플러그인이 로드되는지 확인해줘.

## 공통 규칙
- 원본 파일과 게임 설치 폴더는 수정하지 마. 필요하면 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\input'에 복사본을 만들어 작업해줘.
- 단계를 마칠 때마다 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\PROGRESS.md'에 `[완료|진행중|막힘] 단계명 — 산출물 경로 — 메모` 형식으로 기록해줘. 다음 세션이 이 파일만 보고 이어갈 수 있어야 해.
- 실행한 명령과 핵심 출력은 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\logs\'에 남기고, 확인된 사실과 추정을 구분해서 써줘.
- 도구가 없거나 같은 오류가 2번 이상 반복되면 우회책을 계속 시도하지 말고 원인과 필요한 조치를 정리한 뒤 멈춰줘.
- 단, **타임아웃은 위 규칙에 넣지 마.** 도구가 고장 난 게 아니라 한 번에 맡긴 일이 너무 크다는 뜻이야. 같은 호출을 그대로 반복하지 말고 더 작게 나눠서 바로 이어가줘 (큰 PE는 `pe_slice`로 함수 단위 조각을 만들어 REA로 열고, 오래 걸리는 명령은 백그라운드로 돌리고 로그로 확인). 내가 다시 하라고 하면 타임아웃을 이유로 거절하지 말고 이렇게 나눠서 다시 해줘.
- 덤프 · 디컴파일 · 압축 해제(Il2CppDumper, Cpp2IL, `ilspycmd -p`, jadx, apktool 등)는 큰 게임에서 몇 분 걸릴 수 있어. 셸 도구 시간 제한에 걸리지 않게 출력을 'D:\ReaTest\Work Dirs\UnityIl2Cpp-resume\logs\' 아래 로그 파일로 남기면서 백그라운드로 실행하고, 끝났는지는 로그 마지막 줄과 산출물로 확인해줘.
- 범위: 로컬·싱글플레이 분석과 모드 제작만. 온라인 게임의 안티치트/DRM/서버 검증 우회, 결제·재화 조작, 암호화된 바이너리나 메타데이터의 복호화는 하지 마.

## 최종 보고
1. 이번에 완료한 단계  2. 산출물 경로  3. 핵심 발견 (진입점, 후킹 후보 등)  4. 다음 세션에서 이어서 할 일
````

## 샘플: Unity APK · 수행 지침 부분

````markdown
## 수행 지침
0. APK 확보 (파일이 없을 때만)
   - `bat\check_android_emulator_env.bat`로 CPU 가상화(VT)·녹스 설치/실행·ADB 연결을 먼저 확인해줘.
   - 녹스에 직접 설치한 앱이라면 `adb shell pm list packages -3`로 패키지명을 찾고, `adb shell pm path <패키지명>`에 나온 base.apk와 split APK를 모두 `adb pull`로 'D:\ReaTest\Work Dirs\UnityApk-new\apk'에 가져와줘.
1. 압축 해제
   `"C:\WINDOWS\system32\tar.exe" -xf "D:\ReaTest\Games With Space\unity game.apk" -C "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped"` (폴더를 먼저 만들 것. Windows 내장 bsdtar — Git Bash의 GNU tar는 zip을 풀지 못함)
2. IL2CPP 덤프
   `Il2CppDumper "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped\lib\arm64-v8a\libil2cpp.so" "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped\assets\bin\Data\Managed\Metadata\global-metadata.dat" "D:\ReaTest\Work Dirs\UnityApk-new\il2cpp_dump"`
   - global-metadata.dat 시그니처가 표준이 아니면 복호화하지 말고 보고 후 멈춰줘.
   - Il2CppDumper가 실패하면 Cpp2IL로 재시도해줘 (APK는 force 옵션 3개를 함께 지정, 버전은 f1 같은 접미사 없이 숫자만. 오류 시 Enter를 기다리니 입력을 닫고 실행 — bash `< /dev/null`, cmd `< NUL`):
     `Cpp2IL --game-path "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped" --force-binary-path "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped\lib\arm64-v8a\libil2cpp.so" --force-metadata-path "D:\ReaTest\Work Dirs\UnityApk-new\apk_unzipped\assets\bin\Data\Managed\Metadata\global-metadata.dat" --force-unity-version "2022.3.10" --skip-analysis --disable-registration-prompts --output-root "D:\ReaTest\Work Dirs\UnityApk-new\cpp2il_out"`
   `ilspycmd -p -o "D:\ReaTest\Work Dirs\UnityApk-new\src_dummy" "D:\ReaTest\Work Dirs\UnityApk-new\il2cpp_dump\DummyDll\Assembly-CSharp.dll"`
3. Java/네이티브 브리지 확인
   `jadx -d "D:\ReaTest\Work Dirs\UnityApk-new\jadx_src" "D:\ReaTest\Games With Space\unity game.apk"`
   - UnityPlayerActivity 확장 여부, 네이티브 플러그인/SDK 브리지를 확인해 'D:\ReaTest\Work Dirs\UnityApk-new\docs\analysis.md'에 정리해줘.
4. 녹스 실행 검증
   - `adb connect 127.0.0.1:62001` (녹스 기본 포트, 다중 인스턴스는 62025부터) 후
     `adb install -r "D:\ReaTest\Games With Space\unity game.apk"` (분할 패키지는 `install-multiple`) → `adb logcat -s Unity`로 런타임 로그를 확인해줘.
````
