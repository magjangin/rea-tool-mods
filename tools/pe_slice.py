#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
pe_slice — 큰 PE(x64/x86, GameAssembly.dll 등)에서 보고 싶은 함수가 쓰는 페이지만 담은 작은 PE를 만듭니다.

REA의 Windows Ghidra는 PE만 받고, 열 때 파일 전체를 자동 분석합니다. 수십 MB짜리 GameAssembly.dll을
통째로 열면 분석이 MCP 호출 시간 제한보다 길어 타임아웃이 납니다. 이 도구로 만든 조각 PE(수백 KB)는
1분 안에 열리고, Il2CppDumper의 script.json 이름이 export로 들어가 의사코드에 메서드 이름이 그대로 보입니다.

담는 것: 대상 함수 전체(.pdata 체인으로 끝 주소 계산) + 호출 대상마다 앞부분 최대 0x2000바이트
         + 읽는 데이터(RIP 상대 주소)마다 1페이지. 바이트는 원본 그대로 복사합니다.

사용 예:
  pe_slice --dll GameAssembly.dll --script-json il2cpp_dump\\script.json --class TileDataLoader --out slices\\TileDataLoader.dll
  pe_slice --dll GameAssembly.dll --script-json il2cpp_dump\\script.json --method "TileDataLoader::GenerateNote" --out slices\\GenerateNote.dll
  (script.json 이름은 TileDataLoader$$GenerateNote지만, 셸이 $$를 바꾸지 않게 :: 로 써도 됩니다)
  pe_slice --dll Some.exe --rva 0x1234 --out slices\\sub_1234.dll          (script.json 없이 주소로)
"""

import argparse
import bisect
import json
import os
import re
import struct
import sys

VERSION = "1.0"
PAGE = 0x1000
FALIGN = 0x200
MAX_SECTIONS = 90          # 헤더가 첫 섹션(0x1000)을 넘지 않도록
CALLEE_BYTES = 0x2000      # 호출 대상은 앞부분만 (Ghidra가 인자 개수를 추정할 만큼)


def die(msg):
    print(f"[pe_slice] 오류: {msg}", file=sys.stderr)
    sys.exit(1)


# ───────────────────────── PE 읽기 ─────────────────────────

class Pe:
    def __init__(self, path):
        self.blob = open(path, "rb").read()
        b = self.blob
        if b[:2] != b"MZ":
            die(f"PE 파일이 아닙니다: {path}")
        pe = struct.unpack_from("<I", b, 0x3C)[0]
        if b[pe:pe + 4] != b"PE\0\0":
            die(f"PE 시그니처가 없습니다: {path}")
        machine, nsec = struct.unpack_from("<HH", b, pe + 4)
        optsz = struct.unpack_from("<H", b, pe + 20)[0]
        opt = pe + 24
        magic = struct.unpack_from("<H", b, opt)[0]
        if machine == 0x8664 and magic == 0x20B:
            self.is64 = True
            self.base = struct.unpack_from("<Q", b, opt + 24)[0]
            ndirs_off, dirs_off = opt + 108, opt + 112
        elif machine == 0x14C and magic == 0x10B:
            self.is64 = False
            self.base = struct.unpack_from("<I", b, opt + 28)[0]
            ndirs_off, dirs_off = opt + 92, opt + 96
        else:
            die("x64(PE32+) 또는 x86(PE32) PE만 지원합니다.")
        ndirs = struct.unpack_from("<I", b, ndirs_off)[0]
        self.exc_rva, self.exc_size = struct.unpack_from("<II", b, dirs_off + 8 * 3) if ndirs > 3 else (0, 0)
        self.secs = []
        s = opt + optsz
        for i in range(nsec):
            name, vs, va, rs, rp, _, _, _, _, ch = struct.unpack_from("<8sIIIIIIHHI", b, s + i * 40)
            self.secs.append(dict(name=name.rstrip(b"\0"), va=va, vs=vs, rs=rs, rp=rp, ch=ch))
        self._load_pdata()

    def sec_of(self, rva):
        for s in self.secs:
            if s["va"] <= rva < s["va"] + max(s["vs"], s["rs"]):
                return s
        return None

    def read(self, rva, size):
        s = self.sec_of(rva)
        if s is None:
            return b""
        off = rva - s["va"]
        avail = max(0, min(size, s["rs"] - off))
        return self.blob[s["rp"] + off:s["rp"] + off + avail]

    def page(self, rva_page):
        out = bytearray(PAGE)
        s = self.sec_of(rva_page)
        if s is not None:
            lo, hi = max(rva_page, s["va"]), min(rva_page + PAGE, s["va"] + s["rs"])
            if hi > lo:
                fo = s["rp"] + (lo - s["va"])
                out[lo - rva_page:hi - rva_page] = self.blob[fo:fo + (hi - lo)]
        return bytes(out)

    def is_exec(self, rva):
        s = self.sec_of(rva)
        return bool(s and s["ch"] & 0x20000000)

    def _load_pdata(self):
        data = self.read(self.exc_rva, self.exc_size) if self.exc_size else b""
        ents = []
        for i in range(0, len(data) - 11, 12):
            begin, end, unwind = struct.unpack_from("<III", data, i)
            if begin:
                ents.append((begin, end, unwind))
        ents.sort()
        self.pdata = ents
        self.pdata_begins = [e[0] for e in ents]

    def _chained(self, unwind):
        if unwind & 1:          # 다른 RUNTIME_FUNCTION을 직접 가리키는 형식 = 체인
            return True
        b = self.read(unwind, 1)
        return bool(b) and bool((b[0] >> 3) & 0x4)   # UNW_FLAG_CHAININFO

    def func_end(self, start, known_starts):
        """함수 끝(배타). .pdata에 있으면 이어지는 체인 조각까지 합치고, 없으면 다음 함수 시작까지."""
        i = bisect.bisect_left(self.pdata_begins, start)
        if i < len(self.pdata) and self.pdata[i][0] == start:
            end = self.pdata[i][1]
            j = i + 1
            while j < len(self.pdata) and 0 <= self.pdata[j][0] - end <= 0x10 and self._chained(self.pdata[j][2]):
                end = self.pdata[j][1]
                j += 1
            return end
        k = bisect.bisect_right(known_starts, start)
        nxt = known_starts[k] if k < len(known_starts) else start + PAGE
        return min(nxt, start + 0x10000)


# ───────────────────────── 이름 (script.json) ─────────────────────────

def ghidra_name(text, limit=200):
    return (re.sub(r"[^0-9A-Za-z_.$<>,\[\]`]", "_", text) or "_")[:limit]


def string_name(value):
    parts = []
    for ch in value[:32]:
        if ch.isascii() and (ch.isalnum() or ch == "_"):
            parts.append(ch)
        elif ch == " ":
            parts.append("_")
        elif ch.isascii():
            parts.append(f"x{ord(ch):02X}")
        else:
            parts.append("_")
    return "str_" + ("".join(parts) or "empty")


class Names:
    def __init__(self, script_json):
        self.methods = {}       # rva -> name
        self.by_name = {}       # name -> [rva]
        self.data = {}          # rva -> (export name, 설명)
        if not script_json:
            return
        if not os.path.isfile(script_json):
            die(f"script.json이 없습니다: {script_json} (Il2CppDumper 덤프를 먼저 만들어 주세요)")
        d = json.load(open(script_json, encoding="utf-8"))
        for x in d.get("ScriptMethod", []):
            self.methods.setdefault(x["Address"], x["Name"])
            self.by_name.setdefault(x["Name"], []).append(x["Address"])
        for x in d.get("ScriptString", []):
            self.data.setdefault(x["Address"], (string_name(x["Value"]), x["Value"]))
        for key in ("ScriptMetadata", "ScriptMetadataMethod"):
            for x in d.get(key, []):
                self.data.setdefault(x["Address"], (ghidra_name(x["Name"]), x["Name"]))
        self.method_addrs = sorted(self.methods)
        self.data_addrs = sorted(self.data)

    def find_methods(self, query):
        # PowerShell·bash는 큰따옴표 안의 $$를 변수로 바꾸므로 Class::Method 도 받음 (cmd가 넘긴 작은따옴표도 제거)
        query = query.strip("'").replace("::", "$$")
        if "$$" in query:
            hits = [query] if query in self.by_name else []
        else:
            hits = [n for n in self.by_name if n.split("$$", 1)[-1] == query]
        if not hits:
            similar = [n for n in self.by_name if query.lower() in n.lower()][:10]
            die(f"메서드를 찾지 못했습니다: {query}" + ("\n  비슷한 이름: " + ", ".join(similar) if similar else ""))
        return [(a, n) for n in hits for a in self.by_name[n]]

    def find_class(self, cls):
        hits = [(a, n) for n, addrs in self.by_name.items()
                if n.startswith(cls + "$$") or n.startswith(cls + ".<") for a in addrs]
        if not hits:
            similar = sorted({n.split("$$")[0] for n in self.by_name if cls.lower() in n.split("$$")[0].lower()})[:10]
            die(f"클래스를 찾지 못했습니다: {cls}" + ("\n  비슷한 클래스: " + ", ".join(similar) if similar else ""))
        return hits


# ───────────────────────── 참조 수집 ─────────────────────────

def collect_refs(pe, ranges, known_starts):
    calls, datas = set(), set()
    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_64, CS_MODE_32
        from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP
    except ImportError:
        # capstone이 없으면 E8/E9 rel32 중 알려진 함수 시작을 가리키는 것만 호출로 봄 (데이터 이름은 없음)
        starts = set(known_starts)
        for start, end in ranges:
            code = pe.read(start, end - start)
            for i in range(len(code) - 4):
                if code[i] in (0xE8, 0xE9):
                    t = start + i + 5 + struct.unpack_from("<i", code, i + 1)[0]
                    if t in starts and not (start <= t < end):
                        calls.add(t)
        return calls, datas, False

    md = Cs(CS_ARCH_X86, CS_MODE_64 if pe.is64 else CS_MODE_32)
    md.detail = True
    for start, end in ranges:
        for ins in md.disasm(pe.read(start, end - start), start):
            for op in ins.operands:
                if op.type == X86_OP_IMM and ins.mnemonic in ("call", "jmp") and not (start <= op.imm < end):
                    calls.add(op.imm)
                elif op.type == X86_OP_MEM:
                    if pe.is64:
                        if op.mem.base == X86_REG_RIP:
                            datas.add(ins.address + ins.size + op.mem.disp)
                    else:
                        # x86엔 RIP가 없음: 절대주소 모드([disp]만, base/index 없음)는 코드에 VA가 그대로 박혀있음
                        if op.mem.base == 0 and op.mem.index == 0 and op.mem.disp:
                            rva = op.mem.disp - pe.base
                            if 0 < rva < 0x7FFFFFFF:
                                datas.add(rva)
    return calls, datas, True


def merge(ranges, pe, gap):
    out = []
    for lo, hi in sorted(ranges):
        if out and lo <= out[-1][1] + gap and pe.sec_of(lo) is pe.sec_of(out[-1][0]):
            out[-1][1] = max(out[-1][1], hi)
        else:
            out.append([lo, hi])
    return out


# ───────────────────────── PE 쓰기 ─────────────────────────

def write_pe(path, pe, ranges, exports):
    sections = []
    for lo, hi in ranges:
        src = pe.sec_of(lo)
        raw = b"".join(pe.page(p) for p in range(lo, hi, PAGE))
        sections.append(dict(name=src["name"][:8], va=lo, vs=hi - lo, raw=raw, ch=src["ch"]))

    edata_va = (ranges[-1][1] + PAGE - 1) & ~(PAGE - 1)
    items = sorted(exports.items(), key=lambda kv: kv[1].encode())
    n = len(items)
    eat, npt = 40, 40 + 4 * n
    ot = npt + 4 * n
    str_off = ot + 2 * n
    strblob = bytearray(b"slice.dll\0")
    name_rvas = []
    for _, nm in items:
        name_rvas.append(edata_va + str_off + len(strblob))
        strblob += nm.encode() + b"\0"
    ed = bytearray(str_off) + strblob
    struct.pack_into("<IIHHIIIIIII", ed, 0, 0, 0, 0, 0, edata_va + str_off, 1, n, n,
                     edata_va + eat, edata_va + npt, edata_va + ot)
    for i, (rva, _) in enumerate(items):
        struct.pack_into("<I", ed, eat + 4 * i, rva)
        struct.pack_into("<I", ed, npt + 4 * i, name_rvas[i])
        struct.pack_into("<H", ed, ot + 2 * i, i)
    sections.append(dict(name=b".edata", va=edata_va, vs=len(ed), raw=bytes(ed), ch=0x40000040))

    nsec = len(sections)
    opt_size = 0xF0 if pe.is64 else 0xE0
    size_headers = (0x40 + 4 + 20 + opt_size + 40 * nsec + FALIGN - 1) & ~(FALIGN - 1)
    if size_headers > sections[0]["va"]:
        die("섹션이 너무 많습니다. 대상 함수를 줄여 주세요.")
    fo = size_headers
    for s in sections:
        s["rs"] = (len(s["raw"]) + FALIGN - 1) & ~(FALIGN - 1)
        s["rp"] = fo
        fo += s["rs"]
    size_image = (sections[-1]["va"] + sections[-1]["vs"] + PAGE - 1) & ~(PAGE - 1)

    out = bytearray(fo)
    out[0:2] = b"MZ"
    struct.pack_into("<I", out, 0x3C, 0x40)
    out[0x40:0x44] = b"PE\0\0"
    machine = 0x8664 if pe.is64 else 0x14C
    characteristics = 0x2022 if pe.is64 else 0x2102   # DLL | EXECUTABLE_IMAGE | (x64: LARGE_ADDRESS_AWARE / x86: 32BIT_MACHINE)
    struct.pack_into("<HHIIIHH", out, 0x44, machine, nsec, 0, 0, 0, opt_size, characteristics)
    o = 0x58
    if pe.is64:
        struct.pack_into("<HBBIIIII", out, o, 0x20B, 14, 0, 0, 0, 0, 0, 0)
        struct.pack_into("<Q", out, o + 24, pe.base)
        struct.pack_into("<IIHHHHHHIIIIHH", out, o + 32, PAGE, FALIGN, 6, 0, 0, 0, 6, 0, 0,
                         size_image, size_headers, 0, 2, 0)
        struct.pack_into("<QQQQII", out, o + 72, 0x100000, 0x1000, 0x100000, 0x1000, 0, 16)
        struct.pack_into("<II", out, o + 112, edata_va, len(ed))     # export directory
    else:
        struct.pack_into("<HBBIIIII", out, o, 0x10B, 14, 0, 0, 0, 0, 0, 0)
        struct.pack_into("<I", out, o + 24, 0)                        # BaseOfData (안 씀)
        struct.pack_into("<I", out, o + 28, pe.base)
        struct.pack_into("<IIHHHHHHIIIIHH", out, o + 32, PAGE, FALIGN, 6, 0, 0, 0, 6, 0, 0,
                         size_image, size_headers, 0, 2, 0)
        struct.pack_into("<IIIIII", out, o + 72, 0x100000, 0x1000, 0x100000, 0x1000, 0, 16)
        struct.pack_into("<II", out, o + 96, edata_va, len(ed))      # export directory
    so = o + opt_size
    for i, s in enumerate(sections):
        struct.pack_into("<8sIIIIIIHHI", out, so + 40 * i, s["name"], s["vs"], s["va"],
                         s["rs"], s["rp"], 0, 0, 0, 0, s["ch"])
        out[s["rp"]:s["rp"] + len(s["raw"])] = s["raw"]
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, "wb").write(out)
    return len(out), nsec


# ───────────────────────── main ─────────────────────────

def parse_rva(text):
    try:
        if "-" in text:
            a, b = text.split("-", 1)
            return int(a, 16), int(b, 16)
        return int(text, 16), None
    except ValueError:
        die(f"--rva 형식이 잘못됐습니다: {text} (예: 0x66F670 또는 0x66F670-0x66FC90)")


def main():
    ap = argparse.ArgumentParser(description="큰 x64 PE에서 함수 주변 페이지만 담은 조각 PE를 만듭니다 (REA 타임아웃 회피).")
    ap.add_argument("--version", action="store_true", help="버전과 capstone 설치 여부 출력")
    ap.add_argument("--dll", help="원본 PE (예: GameAssembly.dll)")
    ap.add_argument("--script-json", help="Il2CppDumper script.json (이름 · --class · --method에 필요)")
    ap.add_argument("--class", dest="classes", action="append", default=[], help="클래스의 모든 메서드 (반복 가능)")
    ap.add_argument("--method", dest="methods", action="append", default=[], help="Class::Method(= Class$$Method) 또는 메서드 이름 (반복 가능)")
    ap.add_argument("--rva", dest="rvas", action="append", default=[], help="RVA 또는 RVA-끝RVA, 16진수 (반복 가능)")
    ap.add_argument("--out", help="만들 조각 PE 경로 (.dll)")
    ap.add_argument("--max-functions", type=int, default=80, help="대상 함수 최대 개수 (기본 80)")
    a = ap.parse_args()

    if a.version:
        try:
            import capstone
            cap = f"capstone {capstone.__version__}"
        except ImportError:
            cap = "capstone 없음 — pip install capstone 하면 문자열·메타데이터 이름도 붙음"
        print(f"pe_slice {VERSION} (Python {sys.version.split()[0]}, {cap})")
        return
    if not a.dll or not a.out:
        die("--dll 과 --out 이 필요합니다. 예: --dll GameAssembly.dll --script-json script.json --class TileDataLoader --out slice.dll")
    if not (a.classes or a.methods or a.rvas):
        die("--class / --method / --rva 중 하나 이상을 지정해 주세요.")
    if (a.classes or a.methods) and not a.script_json:
        die("--class / --method 는 --script-json 이 필요합니다.")

    pe = Pe(a.dll)
    names = Names(a.script_json)
    known_starts = sorted(set(pe.pdata_begins) | set(names.methods))

    targets = {}                                   # rva -> name
    for cls in a.classes:
        for rva, nm in names.find_class(cls):
            targets.setdefault(rva, nm)
    for m in a.methods:
        for rva, nm in names.find_methods(m):
            targets.setdefault(rva, nm)
    explicit_end = {}
    for r in a.rvas:
        start, end = parse_rva(r)
        targets.setdefault(start, names.methods.get(start, f"sub_{start:X}"))
        if end:
            explicit_end[start] = end
    targets = {r: n for r, n in targets.items() if r > 0 and pe.is_exec(r)}
    if not targets:
        die("코드 섹션 안에 있는 대상 함수가 없습니다 (RVA가 -1이거나 코드가 아닌 주소).")
    if len(targets) > a.max_functions:
        die(f"대상 함수가 {len(targets)}개로 너무 많습니다 (최대 {a.max_functions}). --method로 좁히거나 --max-functions를 올려 주세요.")

    funcs = []
    for rva in sorted(targets):
        end = explicit_end.get(rva) or pe.func_end(rva, known_starts)
        funcs.append((rva, end, targets[rva]))

    calls, datas, precise = collect_refs(pe, [(s, e) for s, e, _ in funcs], known_starts)
    calls = {t for t in calls if pe.is_exec(t)}
    datas = {t for t in datas if pe.sec_of(t) is not None}

    ranges = [(s & ~(PAGE - 1), (e + PAGE - 1) & ~(PAGE - 1)) for s, e, _ in funcs]
    for t in calls:
        end = min(pe.func_end(t, known_starts), t + CALLEE_BYTES)
        ranges.append((t & ~(PAGE - 1), (end + PAGE - 1) & ~(PAGE - 1)))
    ranges += [(t & ~(PAGE - 1), (t & ~(PAGE - 1)) + PAGE) for t in datas]
    gap = 0
    merged = merge(ranges, pe, gap)
    while len(merged) > MAX_SECTIONS:
        gap = max(gap * 2, PAGE)
        merged = merge(ranges, pe, gap)

    # 이름: 대상 함수 → 매핑된 코드 범위 안의 메서드 이름 전부 → 대상 함수가 읽는 데이터(문자열 · 메타데이터)
    exports, used = {}, set()

    def add(rva, nm):
        if rva in exports:
            return
        base, k = nm, 2
        while nm in used:
            nm = f"{base}_{k}"
            k += 1
        exports[rva] = nm
        used.add(nm)

    for s, _, nm in funcs:
        add(s, ghidra_name(nm))
    method_addrs = getattr(names, "method_addrs", [])
    for lo, hi in merged:
        i = bisect.bisect_left(method_addrs, lo)
        while i < len(method_addrs) and method_addrs[i] < hi:
            add(method_addrs[i], ghidra_name(names.methods[method_addrs[i]]))
            i += 1
    for r in sorted(datas):
        if r in names.data:
            add(r, names.data[r][0])

    size, nsec = write_pe(a.out, pe, merged, exports)

    out_abs = os.path.abspath(a.out)
    unnamed_calls = sorted(t for t in calls if t not in exports)
    report = {
        "source": os.path.abspath(a.dll),
        "image_base": f"{pe.base:#x}",
        "functions": [{"va": f"{pe.base + s:#x}", "rva": f"{s:#x}", "size": f"{e - s:#x}", "name": nm} for s, e, nm in funcs],
        "unnamed_call_targets": [f"{pe.base + t:#x}" for t in unnamed_calls],
        "names": {f"{pe.base + r:#x}": nm for r, nm in sorted(exports.items())},
        "data_values": {f"{pe.base + r:#x}": names.data[r][1] for r in sorted(exports) if r in names.data},
    }
    json.dump(report, open(a.out + ".names.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    print(f"조각 PE: {out_abs}  ({size:,} 바이트, 섹션 {nsec}개, 이름 {len(exports)}개)")
    if not precise:
        print("  ※ capstone이 없어 호출 대상만 대략 찾았습니다(문자열·메타데이터 이름 없음). pip install capstone 권장.")
    print(f"대상 함수 {len(funcs)}개:")
    for s, e, nm in funcs[:40]:
        print(f"  {pe.base + s:#x}  {nm}  ({e - s:#x} 바이트)")
    if len(funcs) > 40:
        print(f"  … 외 {len(funcs) - 40}개 (전체는 names.json)")
    if unnamed_calls:
        print(f"이름 없는 호출 대상 {len(unnamed_calls)}개 (il2cpp 런타임 등): " +
              ", ".join(f"{pe.base + t:#x}" for t in unnamed_calls[:12]) + (" …" if len(unnamed_calls) > 12 else ""))
    print("REA 다음 단계:")
    print(f'  1) open_binary {json.dumps({"path": out_abs}, ensure_ascii=False)}')
    print(f'  2) procedure_pseudo_code {{"procedure": "{pe.base + funcs[0][0]:#x}"}}   ← 위 함수 VA마다')
    print(f"이름 목록: {out_abs}.names.json")


if __name__ == "__main__":
    main()
