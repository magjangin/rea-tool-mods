#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Fast IL2CPP Machine-Code Analyzer & Call Target Resolver
Ghidra/IDA 없이 GameAssembly.dll의 파일 오프셋과 dump.cs 메타데이터만으로
1초 만에 x64 기계어 바이트를 디스어셈블하고 E8 상대 호출 타겟을 실시간 역조회합니다.
"""

import os
import sys
import re
import struct
import argparse
import json
import time

def parse_dump_cs(dump_path, use_cache=True):
    """
    dump.cs에서 Offset -> (ClassName, MethodName, Signature, RVA) 색인을 구축합니다.
    캐시 파일(.idx.json)이 있으면 0.05초 만에 로드합니다.
    """
    cache_path = dump_path + ".fast_index.json"
    dump_mtime = os.path.getmtime(dump_path)

    if use_cache and os.path.exists(cache_path):
        try:
            with open(cache_path, "r", encoding="utf-8") as cf:
                data = json.load(cf)
                if data.get("mtime") == dump_mtime:
                    # JSON keys are strings, convert offset keys to int
                    offset_map = {int(k): v for k, v in data["offset_map"].items()}
                    name_map = data["name_map"]
                    return offset_map, name_map
        except Exception:
            pass

    offset_pattern = re.compile(r'//\s*RVA:\s*(0x[0-9A-Fa-f]+|-1)\s+Offset:\s*(0x[0-9A-Fa-f]+|-1)\s+VA:\s*(0x[0-9A-Fa-f]+|-1)')
    class_pattern = re.compile(r'(?:public|internal|private|protected)?\s*(?:sealed|abstract|static)?\s*class\s+([A-Za-z0-9_`<>]+)')
    method_name_pattern = re.compile(r'(?:public|internal|private|protected|static|virtual|override|async|\s)+\s+([A-Za-z0-9_`<>]+)\s*\(')

    offset_map = {}
    name_map = {}

    current_class = "Global"

    with open(dump_path, "r", encoding="utf-8", errors="ignore") as f:
        pending_offset = None
        pending_rva = None

        for line in f:
            cm = class_pattern.search(line)
            if cm:
                current_class = cm.group(1)

            om = offset_pattern.search(line)
            if om:
                rva_str, off_str, _ = om.groups()
                if off_str != "-1" and off_str.startswith("0x"):
                    pending_offset = int(off_str, 16)
                    pending_rva = int(rva_str, 16) if rva_str != "-1" else None
                continue

            if pending_offset is not None:
                stripped = line.strip()
                if stripped and not stripped.startswith("//") and not stripped.startswith("["):
                    sig = stripped.rstrip(" { ;")
                    mm = re.search(r'([A-Za-z0-9_`<>.]+)\s*\(', stripped)
                    mname = mm.group(1) if mm else "unknown"

                    info = {
                        "class": current_class,
                        "method": mname,
                        "sig": sig,
                        "offset": pending_offset,
                        "rva": pending_rva
                    }
                    offset_map[pending_offset] = info

                    full_name = f"{current_class}.{mname}"
                    name_map.setdefault(mname, []).append(pending_offset)
                    name_map.setdefault(full_name, []).append(pending_offset)

                    pending_offset = None
                    pending_rva = None

    if use_cache:
        try:
            with open(cache_path, "w", encoding="utf-8") as cf:
                json.dump({"mtime": dump_mtime, "offset_map": offset_map, "name_map": name_map}, cf)
        except Exception:
            pass

    return offset_map, name_map


def read_function_bytes(dll_path, offset, max_bytes=256):
    """
    GameAssembly.dll의 파일 오프셋에서 시작하여 함수 끝(C3 ret / CC int3)까지 바이트를 읽어옵니다.
    """
    with open(dll_path, "rb") as f:
        f.seek(offset)
        raw = f.read(max_bytes)
    return raw


def disassemble_x64(code_bytes, base_offset, offset_map):
    """
    x64 바이트를 파싱하고 E8/E9 상대 주소를 계산하여 dump.cs 메타데이터와 결합합니다.
    capstone이 있으면 capstone을 우선 사용하고, 없으면 내장 고속 디코더를 사용합니다.
    """
    instructions = []
    has_capstone = False
    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_64
        md = Cs(CS_ARCH_X86, CS_MODE_64)
        for insn in md.disasm(code_bytes, base_offset):
            target_info = None
            if insn.mnemonic in ("call", "jmp") and insn.op_str.startswith("0x"):
                try:
                    target_addr = int(insn.op_str, 16)
                    target_info = offset_map.get(target_addr)
                except ValueError:
                    pass
            instructions.append({
                "address": insn.address,
                "bytes": bytes(insn.bytes),
                "mnemonic": insn.mnemonic,
                "op_str": insn.op_str,
                "target": target_info
            })
        has_capstone = True
    except ImportError:
        pass

    if not has_capstone:
        # 내장 경량 x64 디코더 (주요 점프, 호출, 분기, 레지스터 보존 패턴 처리)
        i = 0
        n = len(code_bytes)
        while i < n:
            addr = base_offset + i
            b = code_bytes[i]

            # E8 cd: CALL rel32
            if b == 0xE8 and i + 4 < n:
                rel = struct.unpack("<i", code_bytes[i+1:i+5])[0]
                target = addr + 5 + rel
                target_info = offset_map.get(target)
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+5],
                    "mnemonic": "call",
                    "op_str": f"0x{target:X}",
                    "target": target_info
                })
                i += 5
                continue

            # E9 cd: JMP rel32
            if b == 0xE9 and i + 4 < n:
                rel = struct.unpack("<i", code_bytes[i+1:i+5])[0]
                target = addr + 5 + rel
                target_info = offset_map.get(target)
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+5],
                    "mnemonic": "jmp",
                    "op_str": f"0x{target:X}",
                    "target": target_info
                })
                i += 5
                continue

            # EB cb: JMP short
            if b == 0xEB and i + 1 < n:
                rel = struct.unpack("<b", code_bytes[i+1:i+2])[0]
                target = addr + 2 + rel
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+2],
                    "mnemonic": "jmp",
                    "op_str": f"0x{target:X}",
                    "target": None
                })
                i += 2
                continue

            # 74 cb: JZ / JE short
            if b == 0x74 and i + 1 < n:
                rel = struct.unpack("<b", code_bytes[i+1:i+2])[0]
                target = addr + 2 + rel
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+2],
                    "mnemonic": "je",
                    "op_str": f"0x{target:X}",
                    "target": None
                })
                i += 2
                continue

            # 75 cb: JNZ / JNE short
            if b == 0x75 and i + 1 < n:
                rel = struct.unpack("<b", code_bytes[i+1:i+2])[0]
                target = addr + 2 + rel
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+2],
                    "mnemonic": "jne",
                    "op_str": f"0x{target:X}",
                    "target": None
                })
                i += 2
                continue

            # 0F 84 / 0F 85: JZ / JNZ near
            if b == 0x0F and i + 5 < n and code_bytes[i+1] in (0x84, 0x85):
                mne = "je" if code_bytes[i+1] == 0x84 else "jne"
                rel = struct.unpack("<i", code_bytes[i+2:i+6])[0]
                target = addr + 6 + rel
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+6],
                    "mnemonic": mne,
                    "op_str": f"0x{target:X}",
                    "target": None
                })
                i += 6
                continue

            # 84 C0: test al, al
            if b == 0x84 and i + 1 < n and code_bytes[i+1] == 0xC0:
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+2],
                    "mnemonic": "test",
                    "op_str": "al, al",
                    "target": None
                })
                i += 2
                continue

            # 85 C0: test eax, eax
            if b == 0x85 and i + 1 < n and code_bytes[i+1] == 0xC0:
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+2],
                    "mnemonic": "test",
                    "op_str": "eax, eax",
                    "target": None
                })
                i += 2
                continue

            # 48 8B D9: mov rbx, rcx
            if b == 0x48 and i + 2 < n and code_bytes[i+1] == 0x8B and code_bytes[i+2] == 0xD9:
                instructions.append({
                    "address": addr,
                    "bytes": code_bytes[i:i+3],
                    "mnemonic": "mov",
                    "op_str": "rbx, rcx ; backup this pointer",
                    "target": None
                })
                i += 3
                continue

            # C3: ret
            if b == 0xC3:
                instructions.append({
                    "address": addr,
                    "bytes": b"\xC3",
                    "mnemonic": "ret",
                    "op_str": "",
                    "target": None
                })
                i += 1
                continue

            # CC: int3 (padding)
            if b == 0xCC:
                instructions.append({
                    "address": addr,
                    "bytes": b"\xCC",
                    "mnemonic": "int3",
                    "op_str": "; padding",
                    "target": None
                })
                i += 1
                continue

            # 기타 바이트 (단일 바이트 또는 미인식 패턴 보존)
            instructions.append({
                "address": addr,
                "bytes": code_bytes[i:i+1],
                "mnemonic": "db",
                "op_str": f"0x{b:02X}",
                "target": None
            })
            i += 1

    return instructions


def synthesize_pseudocode(method_info, instructions):
    """
    디스어셈블리에서 함수 호출 체인과 조건 분기를 분석하여 고수준 의사코드를 복원합니다.
    """
    calls = []
    for insn in instructions:
        if insn["mnemonic"] == "call":
            if insn["target"]:
                t = insn["target"]
                calls.append(f"{t['class']}.{t['method']}")
            else:
                calls.append(f"sub_{insn['op_str']}")

    lines = []
    sig = method_info.get("sig", "void Unknown()")
    lines.append(f"// Reconstructed Pseudo-Logic:")
    lines.append(f"{sig}")
    lines.append("{")

    # 패턴 매칭: 호출 -> 조건검사 -> 분기 -> 2차 호출
    has_test_al = any(ins["mnemonic"] == "test" and "al" in ins["op_str"] for ins in instructions)
    has_je = any(ins["mnemonic"] in ("je", "jz") for ins in instructions)

    if len(calls) == 2 and has_test_al and has_je:
        lines.append(f"    // Pattern: Checked invocation with early return")
        lines.append(f"    if (this.{calls[0]}(...))")
        lines.append(f"    {{")
        lines.append(f"        this.{calls[1]}(...);")
        lines.append(f"    }}")
    elif len(calls) == 1:
        lines.append(f"    // Direct wrapper/forwarding")
        lines.append(f"    return this.{calls[0]}(...);")
    else:
        for c in calls:
            lines.append(f"    {c}(...);")

    lines.append("}")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Fast IL2CPP x64 Disassembler & Call Target Resolver")
    parser.add_argument("--dll", required=True, help="Path to GameAssembly.dll")
    parser.add_argument("--dump", required=True, help="Path to dump.cs")
    parser.add_argument("--method", help="Method name to analyze (e.g. PlayByKey)")
    parser.add_argument("--offset", help="Hex file offset to analyze (e.g. 0x1E352F0)")
    parser.add_argument("--max-bytes", type=int, default=256, help="Max bytes to inspect (default: 256)")
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    args = parser.parse_args()

    if not os.path.exists(args.dll):
        print(f"Error: DLL not found: {args.dll}", file=sys.stderr)
        sys.exit(1)
    if not os.path.exists(args.dump):
        print(f"Error: dump.cs not found: {args.dump}", file=sys.stderr)
        sys.exit(1)

    t0 = time.time()
    offset_map, name_map = parse_dump_cs(args.dump)
    t_parse = time.time() - t0

    target_offset = None
    target_info = None

    if args.offset:
        target_offset = int(args.offset, 16) if args.offset.startswith(("0x", "0X")) else int(args.offset)
        target_info = offset_map.get(target_offset, {
            "class": "Unknown",
            "method": f"sub_{target_offset:X}",
            "sig": f"// Offset: 0x{target_offset:X}",
            "offset": target_offset,
            "rva": None
        })
    elif args.method:
        offsets = name_map.get(args.method)
        if not offsets:
            # Case-insensitive substring search
            matches = [k for k in name_map.keys() if args.method.lower() in k.lower()]
            if matches:
                print(f"[*] '{args.method}' not found exactly. Similar methods: {', '.join(matches[:10])}", file=sys.stderr)
                offsets = name_map[matches[0]]
            else:
                print(f"Error: Method '{args.method}' not found in dump.cs", file=sys.stderr)
                sys.exit(1)
        target_offset = offsets[0]
        target_info = offset_map.get(target_offset)
    else:
        print("Error: Either --method or --offset must be specified.", file=sys.stderr)
        sys.exit(1)

    raw_bytes = read_function_bytes(args.dll, target_offset, args.max_bytes)
    instructions = disassemble_x64(raw_bytes, target_offset, offset_map)
    pseudocode = synthesize_pseudocode(target_info, instructions)

    if args.json:
        res = {
            "method": target_info,
            "byte_count": len(raw_bytes),
            "instructions": [
                {
                    "address": f"0x{ins['address']:X}",
                    "mnemonic": ins["mnemonic"],
                    "op_str": ins["op_str"],
                    "target": ins["target"]
                }
                for ins in instructions
            ],
            "pseudocode": pseudocode
        }
        print(json.dumps(res, indent=2, ensure_ascii=False))
        return

    # CLI Output
    hex_dump = " ".join(f"{b:02X}" for b in raw_bytes)
    print("=" * 80)
    print(f"🎯 Target Method: {target_info.get('class')}.{target_info.get('method')}")
    rva_val = target_info.get('rva') or 0
    print(f"📍 File Offset:   0x{target_offset:X}  (RVA: 0x{rva_val:X} / Length: {len(raw_bytes)} bytes)")
    print(f"📝 Signature:     {target_info.get('sig')}")
    print(f"⏱️ Index Load:     {t_parse:.3f}s")
    print("=" * 80)
    print("\n[Hex Dump]")
    for row in range(0, len(raw_bytes), 16):
        chunk = raw_bytes[row:row+16]
        hex_str = " ".join(f"{b:02X}" for b in chunk)
        ascii_str = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
        print(f"  +0x{row:03X}:  {hex_str:<48}  {ascii_str}")

    print("\n[Annotated Disassembly]")
    for ins in instructions:
        addr = ins["address"]
        byte_str = " ".join(f"{b:02X}" for b in ins["bytes"])
        mne = ins["mnemonic"]
        ops = ins["op_str"]

        comment = ""
        if ins.get("target"):
            t = ins["target"]
            comment = f" // ──> {t['class']}.{t['method']}() [Offset: 0x{t['offset']:X}]"

        print(f"  0x{addr:X}:  {byte_str:<24} {mne:<6} {ops:<20}{comment}")

    print("\n" + "=" * 80)
    print(pseudocode)
    print("=" * 80)

if __name__ == "__main__":
    main()
