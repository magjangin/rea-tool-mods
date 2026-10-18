# REA Specialized Binary & IL2CPP Slicing Tools

This directory contains standalone slicing and disassembling tools designed to optimize reverse engineering workflows for large binaries and Unity IL2CPP games.

## Background & Problem
When analyzing huge Windows PE binaries (such as Unity's `GameAssembly.dll`, often 50MB–100MB+) with REA's Ghidra provider, full-binary auto-analysis often exceeds MCP/agent request timeouts (1–2 minutes) and exhausts system memory.

Additionally, Windows hosts cannot directly run REA Ghidra analysis on Android ELF (`libil2cpp.so`) or iOS/macOS Mach-O (`UnityFramework` / `GameAssembly.dylib`).

## Tools Overview

### 1. `pe_slice.py` (`pe-slice.mjs`)
- **Purpose**: Creates a minimal slice PE (a few hundred KB) containing only the requested function(s), their unwind `.pdata` chains, direct callee stubs, and referenced RIP-relative data pages.
- **Benefits**:
  - Drops analysis time from 10+ minutes (or timeout) to under **1 minute**.
  - Embeds Il2CppDumper `script.json` method and string names into the sliced PE export table, allowing Ghidra pseudocode to display real method and string names.
- **Usage**:
  ```bash
  # Via Node wrapper:
  node tools/pe-slice.mjs --dll GameAssembly.dll --script-json script.json --class TargetClass --out slices/TargetClass.dll
  node tools/pe-slice.mjs --dll GameAssembly.dll --script-json script.json --method "TargetClass::TargetMethod" --out slices/TargetMethod.dll
  node tools/pe-slice.mjs --dll Game.exe --rva 0x1234 --out slices/sub_1234.dll

  # Via Python directly:
  python tools/pe_slice.py --dll GameAssembly.dll ...
  ```

### 2. `il2cpp_asm.py` (`il2cpp-asm.mjs`)
- **Purpose**: Direct instruction-level disassembly with symbol annotation for Unity IL2CPP binaries (ARM64 & x64, ELF / Mach-O / PE).
- **Benefits**:
  - Works on Android `libil2cpp.so` and iOS `UnityFramework` directly on Windows without requiring heavy emulators or platform-specific Ghidra loaders.
  - Automatically annotates `bl`/`call` targets and ADRP+LDR string/type info from Il2CppDumper `dump.cs` and `script.json`.
  - Includes `--check-encryption` to check iOS FairPlay Mach-O `cryptid`.
- **Usage**:
  ```bash
  node tools/il2cpp-asm.mjs --binary libil2cpp.so --dump dump.cs --script-json script.json --class PlayerController --out logs/PlayerController.asm
  node tools/il2cpp-asm.mjs --check-encryption Payload/Game.app/Frameworks/UnityFramework.framework/UnityFramework
  ```

## Agent Routing Guideline
When encountering a PE binary larger than **10MB** or any Unity `GameAssembly.dll`:
1. **NEVER** blindly call `open_binary` on the entire large PE.
2. Identify the target class or method name from `dump.cs` or user query.
3. Call `tools/pe_slice.py` to create a lightweight slice DLL for that function/class.
4. Call `open_binary` on the generated slice DLL and inspect with `procedure_pseudo_code`.
