#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
il2cpp_asm — IL2CPP 메서드를 이름 주석이 달린 어셈블리로 출력합니다 (ARM64 · x64 / ELF · Mach-O · PE).

Windows의 REA(Ghidra)는 PE만 열 수 있어서 안드로이드 libil2cpp.so(ELF)나 iOS UnityFramework·macOS
GameAssembly.dylib(Mach-O)는 REA로 볼 수 없습니다. 이 도구는 Il2CppDumper의 dump.cs(RVA → 파일 오프셋)와
script.json(이름)만으로 함수 바이트를 바로 읽어 capstone으로 디스어셈블하고, 호출 대상 메서드와
읽는 문자열·TypeInfo·MethodInfo 이름을 주석으로 붙입니다. 몇 초 안에 끝나며 게임을 실행하지 않습니다.

사용 예:
  il2cpp_asm --binary libil2cpp.so --dump il2cpp_dump\\dump.cs --script-json il2cpp_dump\\script.json --class PlayerController --out logs\\PlayerController.asm
  il2cpp_asm --binary libil2cpp.so --dump il2cpp_dump\\dump.cs --script-json il2cpp_dump\\script.json --method "PlayerController::Jump"
  il2cpp_asm --check-encryption Payload\\Game.app\\Frameworks\\UnityFramework.framework\\UnityFramework
      (iOS FairPlay 암호화 여부. LC_ENCRYPTION_INFO의 cryptid가 0이 아니면 종료 코드 2)

함수 끝은 dump.cs에서 다음 메서드가 시작하는 곳으로 추정합니다(메타데이터에 없는 함수가 사이에 있으면 함께 보일 수 있음).
"""

import argparse
import bisect
import json
import os
import re
import struct
import sys

VERSION = "1.0"
MAX_FUNC = 0x4000
ADRP_WINDOW = 12          # ADRP 뒤 이 개수 안의 ADD/LDR만 짝으로 봄


def die(msg, code=1):
    print(f"[il2cpp_asm] 오류: {msg}", file=sys.stderr)
    sys.exit(code)


# ───────────────────────── 바이너리 형식 · 아키텍처 ─────────────────────────

CPU_ARM64, CPU_X86_64 = 0x0100000C, 0x01000007


def fat_slices(blob):
    """유니버설(fat) Mach-O면 [(cputype, offset, size)], 아니면 None"""
    magic = struct.unpack_from(">I", blob, 0)[0]
    if magic not in (0xCAFEBABE, 0xCAFEBABF):
        return None
    n = struct.unpack_from(">I", blob, 4)[0]
    if n == 0 or n > 32:          # 0xCAFEBABE는 Java class와 겹침
        return None
    out, pos = [], 8
    for _ in range(n):
        if magic == 0xCAFEBABE:
            cpu, _, off, size, _ = struct.unpack_from(">iiIII", blob, pos)
            pos += 20
        else:
            cpu, _, off, size, _, _ = struct.unpack_from(">iiQQII", blob, pos)
            pos += 32
        out.append((cpu & 0xFFFFFFFF, off, size))
    return out


def detect(blob, want_arch):
    """(arch, 파일 안 시작 오프셋, 형식 이름)"""
    if blob[:2] == b"MZ":
        pe = struct.unpack_from("<I", blob, 0x3C)[0]
        machine = struct.unpack_from("<H", blob, pe + 4)[0]
        arch = {0x8664: "x64", 0xAA64: "arm64"}.get(machine)
        if not arch:
            die(f"지원하지 않는 PE 머신 타입 0x{machine:X} (x64 · ARM64만 지원)")
        return arch, 0, "PE"
    if blob[:4] == b"\x7fELF":
        machine = struct.unpack_from("<H", blob, 0x12)[0]
        arch = {0xB7: "arm64", 0x3E: "x64"}.get(machine)
        if not arch:
            hint = " — APK라면 lib\\arm64-v8a\\libil2cpp.so를 써줘" if machine == 0x28 else ""
            die(f"지원하지 않는 ELF 머신 타입 0x{machine:X} (ARM64 · x64만 지원){hint}")
        return arch, 0, "ELF"
    slices = fat_slices(blob)
    if slices:
        want = CPU_ARM64 if want_arch == "arm64" else CPU_X86_64
        for cpu, off, _ in slices:
            if cpu == want:
                return want_arch, off, f"유니버설 Mach-O ({want_arch} 조각 @0x{off:X})"
        die(f"유니버설 Mach-O에 {want_arch} 조각이 없습니다. --arch 를 바꿔 주세요.")
    if blob[:4] == b"\xcf\xfa\xed\xfe":
        cpu = struct.unpack_from("<I", blob, 4)[0]
        arch = {CPU_ARM64: "arm64", CPU_X86_64: "x64"}.get(cpu)
        if not arch:
            die(f"지원하지 않는 Mach-O CPU 0x{cpu:X}")
        return arch, 0, "Mach-O"
    die("PE · ELF · Mach-O가 아닙니다.")


# ───────────────────────── iOS 암호화 확인 ─────────────────────────

def check_encryption(path):
    blob = open(path, "rb").read()
    slices = fat_slices(blob)
    if slices is None:
        if blob[:4] not in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe"):
            die("Mach-O 파일이 아닙니다.")
        slices = [(struct.unpack_from("<I", blob, 4)[0], 0, len(blob))]
    encrypted = False
    for cpu, off, _ in slices:
        magic = blob[off:off + 4]
        is64 = magic == b"\xcf\xfa\xed\xfe"
        if magic not in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe"):
            print(f"  CPU 0x{cpu:X}: Mach-O 헤더를 읽을 수 없음")
            continue
        ncmds = struct.unpack_from("<I", blob, off + 16)[0]
        pos = off + (32 if is64 else 28)
        found = None
        for _ in range(ncmds):
            cmd, size = struct.unpack_from("<II", blob, pos)
            if cmd in (0x21, 0x2C):               # LC_ENCRYPTION_INFO(_64)
                found = struct.unpack_from("<I", blob, pos + 16)[0]
            if size < 8:
                break
            pos += size
        name = {CPU_ARM64: "arm64", CPU_X86_64: "x86_64"}.get(cpu, f"0x{cpu:X}")
        if found is None:
            print(f"  {name}: LC_ENCRYPTION_INFO 없음 → 암호화되지 않음")
        else:
            print(f"  {name}: cryptid={found} → {'암호화됨 (FairPlay)' if found else '암호화되지 않음'}")
            encrypted |= found != 0
    if encrypted:
        print("결과: 암호화된 바이너리입니다. 복호화하지 말고 여기서 멈춰 주세요.")
        sys.exit(2)
    print("결과: 암호화되지 않았습니다. 분석을 계속해도 됩니다.")


# ───────────────────────── dump.cs · script.json ─────────────────────────

class Symbols:
    def __init__(self, dump_cs, script_json):
        if not os.path.isfile(dump_cs):
            die(f"dump.cs가 없습니다: {dump_cs} (Il2CppDumper 덤프를 먼저 만들어 주세요)")
        if not os.path.isfile(script_json):
            die(f"script.json이 없습니다: {script_json}")
        text = open(dump_cs, encoding="utf-8", errors="ignore").read()
        pairs = {}
        for m in re.finditer(r"// RVA: 0x([0-9A-Fa-f]+) Offset: 0x([0-9A-Fa-f]+) VA: 0x([0-9A-Fa-f]+)", text):
            pairs[int(m.group(1), 16)] = int(m.group(2), 16)
        if not pairs:
            die("dump.cs에서 'RVA: … Offset: …' 주석을 찾지 못했습니다.")
        self.rvas = sorted(pairs)
        self.offsets = pairs

        d = json.load(open(script_json, encoding="utf-8"))
        self.methods, self.by_name, self.data, self.shared = {}, {}, {}, {}
        for x in d.get("ScriptMethod", []):
            self.methods.setdefault(x["Address"], x["Name"])
            self.shared.setdefault(x["Address"], set()).add(x["Name"])
            self.by_name.setdefault(x["Name"], []).append(x["Address"])
        for x in d.get("ScriptString", []):
            self.data.setdefault(x["Address"], "StringLiteral " + json.dumps(x["Value"], ensure_ascii=False)[:80])
        for key in ("ScriptMetadata", "ScriptMetadataMethod"):
            for x in d.get(key, []):
                self.data.setdefault(x["Address"], x["Name"])

    def file_offset(self, rva):
        if rva in self.offsets:
            return self.offsets[rva]
        i = bisect.bisect_right(self.rvas, rva) - 1
        if i < 0:
            return None
        near = self.rvas[i]
        return rva + (self.offsets[near] - near)

    def end_of(self, rva):
        i = bisect.bisect_right(self.rvas, rva)
        nxt = self.rvas[i] if i < len(self.rvas) else rva + 0x400
        return min(nxt, rva + MAX_FUNC)

    def name_at(self, rva):
        if rva in self.methods:
            # 같은 코드를 한 주소로 합친 메서드(예: String.Equals / op_Equality)는 개수를 함께 표시
            more = len(self.shared.get(rva, ())) - 1
            return self.methods[rva] + (f" (+{more}개 같은 주소)" if more > 0 else "")
        return self.data.get(rva)

    def find(self, classes, methods):
        hits = {}
        for cls in classes:
            got = [(a, n) for n, addrs in self.by_name.items()
                   if n.startswith(cls + "$$") or n.startswith(cls + ".<") for a in addrs]
            if not got:
                similar = sorted({n.split("$$")[0] for n in self.by_name if cls.lower() in n.split("$$")[0].lower()})[:10]
                die(f"클래스를 찾지 못했습니다: {cls}" + ("\n  비슷한 클래스: " + ", ".join(similar) if similar else ""))
            hits.update({a: n for a, n in got if a not in hits})
        for q in methods:
            q = q.strip("'").replace("::", "$$")
            names = [q] if q in self.by_name else ([] if "$$" in q else
                                                   [n for n in self.by_name if n.split("$$", 1)[-1] == q])
            if not names:
                similar = [n for n in self.by_name if q.lower() in n.lower()][:10]
                die(f"메서드를 찾지 못했습니다: {q}" + ("\n  비슷한 이름: " + ", ".join(similar) if similar else ""))
            for n in names:
                for a in self.by_name[n]:
                    hits.setdefault(a, n)
        return {a: n for a, n in hits.items() if a > 0}


# ───────────────────────── 디스어셈블 ─────────────────────────

def disasm_arm64(code, rva, start, end, sym):
    import capstone
    arch = getattr(capstone, "CS_ARCH_ARM64", None) or getattr(capstone, "CS_ARCH_AARCH64")
    md = capstone.Cs(arch, capstone.CS_MODE_ARM)
    pages = {}                                   # 레지스터 → (값, 남은 명령 수)
    lines, named = [], 0

    def reg(r):
        return "x" + r[1:] if r[:1] == "w" and r[1:].isdigit() else r

    for ins in md.disasm(code, rva):
        note = ""
        ops = [o.strip() for o in re.split(r",(?![^\[]*\])", ins.op_str)]
        mn = ins.mnemonic
        if mn in ("bl", "b") and ops and ops[0].startswith("#0x"):
            t = int(ops[0][1:], 16)
            if mn == "bl" or not (start <= t < end):
                n = sym.name_at(t)
                note = f"→ {n}" if n else f"→ sub_{t:X}"
                named += bool(n)
        elif mn == "adrp" and len(ops) == 2 and ops[1].startswith("#0x"):
            pages[reg(ops[0])] = (int(ops[1][1:], 16), ADRP_WINDOW)
        elif mn == "add" and len(ops) == 3 and reg(ops[1]) in pages and re.fullmatch(r"#(0x[0-9a-f]+|\d+)", ops[2]):
            v = pages[reg(ops[1])][0] + int(ops[2][1:], 0)
            n = sym.name_at(v)
            if n:
                note, named = f"&{n}", named + 1
            pages[reg(ops[0])] = (v, ADRP_WINDOW)
        elif mn.startswith(("ldr", "str")) and len(ops) >= 2:
            m = re.match(r"\[(\w+)(?:, #(0x[0-9a-f]+|\d+))?\]", ops[1])
            if m and reg(m.group(1)) in pages:
                v = pages[reg(m.group(1))][0] + (int(m.group(2), 0) if m.group(2) else 0)
                n = sym.name_at(v)
                if n:
                    note, named = f"[{n}]", named + 1
            if mn.startswith("ldr"):
                pages.pop(reg(ops[0]), None)
        pages = {k: (v, left - 1) for k, (v, left) in pages.items() if left > 1}
        lines.append(f"{ins.address:#010x}  {mn:<8}{ins.op_str}" + (f"    ; {note}" if note else ""))
    return lines, named


def disasm_x64(code, rva, start, end, sym):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_64
    from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = True
    lines, named = [], 0
    for ins in md.disasm(code, rva):
        note = ""
        for op in ins.operands:
            if op.type == X86_OP_IMM and ins.mnemonic in ("call", "jmp") and (ins.mnemonic == "call" or not (start <= op.imm < end)):
                n = sym.name_at(op.imm)
                note = f"→ {n}" if n else f"→ sub_{op.imm:X}"
                named += bool(n)
            elif op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                n = sym.name_at(ins.address + ins.size + op.mem.disp)
                if n:
                    note, named = f"[{n}]", named + 1
        lines.append(f"{ins.address:#010x}  {ins.mnemonic:<8}{ins.op_str}" + (f"    ; {note}" if note else ""))
    return lines, named


# ───────────────────────── main ─────────────────────────

def main():
    ap = argparse.ArgumentParser(description="IL2CPP 메서드를 이름 주석이 달린 어셈블리로 출력 (ARM64 · x64, ELF · Mach-O · PE)")
    ap.add_argument("--version", action="store_true", help="버전과 capstone 설치 여부 출력")
    ap.add_argument("--check-encryption", metavar="MACHO", help="Mach-O의 FairPlay 암호화 여부(cryptid) 확인")
    ap.add_argument("--binary", help="libil2cpp.so · UnityFramework · GameAssembly.dylib · GameAssembly.dll")
    ap.add_argument("--dump", help="Il2CppDumper dump.cs")
    ap.add_argument("--script-json", help="Il2CppDumper script.json")
    ap.add_argument("--class", dest="classes", action="append", default=[], help="클래스의 모든 메서드 (반복 가능)")
    ap.add_argument("--method", dest="methods", action="append", default=[], help="Class::Method(= Class$$Method) 또는 메서드 이름 (반복 가능)")
    ap.add_argument("--arch", choices=["arm64", "x64"], default="arm64", help="유니버설 Mach-O에서 고를 아키텍처 (기본 arm64)")
    ap.add_argument("--out", help="결과 .asm 파일 (없으면 화면에 출력)")
    ap.add_argument("--max-functions", type=int, default=80)
    a = ap.parse_args()

    if a.version:
        try:
            import capstone
            cap = f"capstone {capstone.__version__}"
        except ImportError:
            cap = "capstone 없음 — pip install capstone 필요"
        print(f"il2cpp_asm {VERSION} (Python {sys.version.split()[0]}, {cap})")
        return
    if a.check_encryption:
        check_encryption(a.check_encryption)
        return
    if not (a.binary and a.dump and a.script_json):
        die("--binary, --dump, --script-json 이 필요합니다 (또는 --check-encryption <파일>).")
    if not (a.classes or a.methods):
        die("--class 또는 --method 를 지정해 주세요.")
    try:
        import capstone  # noqa: F401
    except ImportError:
        die("capstone이 필요합니다: pip install capstone")

    blob = open(a.binary, "rb").read()
    arch, base_off, fmt = detect(blob, a.arch)
    sym = Symbols(a.dump, a.script_json)
    targets = sym.find(a.classes, a.methods)
    if not targets:
        die("주소가 있는 메서드가 없습니다 (추상 메서드 등은 RVA가 -1).")
    if len(targets) > a.max_functions:
        die(f"대상 메서드가 {len(targets)}개로 너무 많습니다 (최대 {a.max_functions}). --method로 좁혀 주세요.")

    out, total_named = [f"; il2cpp_asm {VERSION} — {os.path.abspath(a.binary)} ({fmt}, {arch})",
                        "; 주소는 RVA(dump.cs·script.json과 같은 기준). 함수 끝은 다음 메서드 시작으로 추정.", ""], 0
    for rva in sorted(targets):
        end = sym.end_of(rva)
        fo = sym.file_offset(rva)
        if fo is None or base_off + fo >= len(blob):
            out.append(f"; ===== {targets[rva]}  RVA {rva:#x}: 파일 오프셋을 계산할 수 없음\n")
            continue
        code = blob[base_off + fo: base_off + fo + (end - rva)]
        fn = disasm_arm64 if arch == "arm64" else disasm_x64
        lines, named = fn(code, rva, rva, end, sym)
        total_named += named
        out.append(f"; ===== {targets[rva]}  RVA {rva:#x}  (파일 오프셋 {base_off + fo:#x}, {end - rva:#x} 바이트 추정)")
        out += lines + [""]

    text = "\n".join(out)
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        open(a.out, "w", encoding="utf-8").write(text)
        print(f"어셈블리: {os.path.abspath(a.out)}  ({fmt}, {arch}, 메서드 {len(targets)}개, 이름 주석 {total_named}개)")
        for rva in sorted(targets)[:40]:
            print(f"  {rva:#x}  {targets[rva]}")
        if len(targets) > 40:
            print(f"  … 외 {len(targets) - 40}개")
    else:
        print(text)


if __name__ == "__main__":
    main()
