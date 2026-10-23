#!/usr/bin/env python3
"""同步 MVI 包内置的第三方依赖到上游版本。

用法：
    python tools/sync-thirdparty.py --list
    python tools/sync-thirdparty.py --check
    python tools/sync-thirdparty.py fairygui
    python tools/sync-thirdparty.py loxodon
    python tools/sync-thirdparty.py loxodon --ref v2.6.7
    python tools/sync-thirdparty.py r3
    python tools/sync-thirdparty.py all

说明：
    - fairygui 上游仓库不含 package.json，无法作为 UPM 依赖引用，只能复制源码。
    - loxodon 只同步 Runtime（Framework + Log），不同步 Editor/Docs/Samples。
    - r3 只同步运行所需程序集（含 NuGet 依赖），不包含 R3.Unity 适配层。
"""

from __future__ import annotations

import argparse
import io
import shutil
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ROOT = REPO_ROOT
THIRD_PARTY = PACKAGE_ROOT / "ThirdParty"

FAIRYGUI_URL = "https://codeload.github.com/fairygui/FairyGUI-unity/zip/refs/heads/{ref}"
FAIRYGUI_ARCHIVE_PREFIX = "FairyGUI-unity-{ref}/"
FAIRYGUI_MAP = {
    "Assets/Scripts": "FairyGUI/Scripts",
    "Assets/Editor": "FairyGUI/Editor",
    "Assets/Resources": "FairyGUI/Resources",
}
FAIRYGUI_MARKER = "FairyGUI/Scripts/Core"

LOXODON_URL = "https://codeload.github.com/vovgou/loxodon-framework/zip/refs/tags/{ref}"
LOXODON_PACKAGE = "Loxodon.Framework/Assets/LoxodonFramework"
LOXODON_MAP = {
    f"{LOXODON_PACKAGE}/Runtime/Framework": "Loxodon/Framework",
    f"{LOXODON_PACKAGE}/Runtime/Log": "Loxodon/Log",
}
LOXODON_EXTRA = {
    f"{LOXODON_PACKAGE}/LICENSE.md": "Loxodon/LICENSE.md",
}
LOXODON_MARKER = "Loxodon/Framework/Loxodon.Framework.asmdef"

R3_NUGET = [
    ("r3", "1.3.0", "lib/netstandard2.1/R3.dll"),
    ("microsoft.bcl.asyncinterfaces", "6.0.0", "lib/netstandard2.1/Microsoft.Bcl.AsyncInterfaces.dll"),
    ("microsoft.bcl.timeprovider", "8.0.0", "lib/netstandard2.0/Microsoft.Bcl.TimeProvider.dll"),
    ("system.componentmodel.annotations", "5.0.0", "lib/netstandard2.1/System.ComponentModel.Annotations.dll"),
    ("system.runtime.compilerservices.unsafe", "6.0.0", "lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll"),
    ("system.threading.channels", "8.0.0", "lib/netstandard2.1/System.Threading.Channels.dll"),
]
R3_TARGET = "R3/Plugins"
R3_MARKER = "R3/Plugins/R3.dll"

R3_UPM_URL = "https://codeload.github.com/Cysharp/R3/zip/refs/tags/{ref}"
R3_UPM_PREFIX = "R3-{ref}/src/R3.Unity/Assets/R3.Unity/Runtime/"
R3_UPM_TARGET = "R3/Unity"
R3_UPM_EXCLUDE = ("External/", "External.meta")
R3_UPM_MARKER = "R3/Unity/ObserveOnExtensions.cs"


def download(url: str, timeout: int = 180) -> bytes:
    print(f"  下载 {url}")
    request = urllib.request.Request(url, headers={"User-Agent": "mvi-sync"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read()


def extract_prefix(zip_bytes: bytes, archive_prefix: str, mapping: dict[str, str], target_root: Path) -> int:
    """把 zip 中指定前缀的目录内容复制到目标目录，返回复制的文件数。"""
    copied = 0
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        for member in archive.namelist():
            if member.endswith("/"):
                continue
            for source_prefix, target_suffix in mapping.items():
                prefix = archive_prefix + source_prefix + "/"
                if not member.startswith(prefix):
                    continue
                relative = member[len(prefix):]
                destination = target_root / target_suffix / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(member) as source, open(destination, "wb") as output:
                    shutil.copyfileobj(source, output)
                copied += 1
                break
    return copied


def extract_files(zip_bytes: bytes, archive_prefix: str, mapping: dict[str, str], target_root: Path) -> int:
    copied = 0
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        for source_path, target_path in mapping.items():
            member = archive_prefix + source_path
            destination = target_root / target_path
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, open(destination, "wb") as output:
                shutil.copyfileobj(source, output)
            copied += 1
    return copied


PROTECTED_SUFFIXES = {".meta", ".asmdef"}


def staged_files(root: Path) -> set[Path]:
    return {path.relative_to(root) for path in root.rglob("*") if path.is_file()}


def unchanged(source: Path, destination: Path) -> bool:
    """内容相同（忽略行尾差异）时视为无需写入，避免制造无意义的 diff。"""
    if not destination.exists():
        return False
    current = source.read_bytes()
    existing = destination.read_bytes()
    if current == existing:
        return True
    if b"\x00" in current or b"\x00" in existing:
        return False
    return current.replace(b"\r\n", b"\n") == existing.replace(b"\r\n", b"\n")


def copy_over(source: Path, target: Path) -> tuple[list[Path], list[Path]]:
    """覆盖写入，跳过 .meta（保持 GUID 稳定）与 .asmdef（保留本地集成补丁）。

    返回：(上游已不存在的残留文件, 被保护的 asmdef 列表)。
    """
    target.mkdir(parents=True, exist_ok=True)
    stale = sorted(staged_files(target) - staged_files(source))
    protected: list[Path] = []
    for path in source.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if relative.suffix in PROTECTED_SUFFIXES:
            if relative.suffix == ".asmdef":
                protected.append(relative)
            continue
        destination = target / relative
        if unchanged(path, destination):
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
    return stale, protected


def print_stale(stale: list[Path], protected: list[Path]) -> None:
    if protected:
        print(f"  已跳过 {len(protected)} 个 .asmdef（保留本地集成补丁）：")
        for path in protected[:10]:
            print(f"    - {path}")
    if not stale:
        return
    print(f"  提示：{len(stale)} 个文件在上游已不存在，未自动清理（确认无用后请手动删除）：")
    for path in stale[:10]:
        print(f"    - {path}")
    if len(stale) > 10:
        print(f"    ... 另有 {len(stale) - 10} 个")


def cleanup(staging: Path) -> None:
    shutil.rmtree(staging, ignore_errors=True)


def sync_fairygui(ref: str) -> None:
    print(f"[FairyGUI] ref={ref}")
    payload = download(FAIRYGUI_URL.format(ref=ref))
    archive_prefix = FAIRYGUI_ARCHIVE_PREFIX.format(ref=ref)
    staging = Path(tempfile.mkdtemp(prefix="mvi-sync-fairygui-"))
    count = extract_prefix(payload, archive_prefix, FAIRYGUI_MAP, staging)
    if not (staging / FAIRYGUI_MARKER).exists():
        print("  校验失败：上游结构与预期不符，未修改包内容")
        cleanup(staging)
        return
    stale: list[Path] = []
    protected: list[Path] = []
    for relative in FAIRYGUI_MAP.values():
        found_stale, found_protected = copy_over(staging / relative, THIRD_PARTY / relative)
        stale.extend(Path(relative) / path for path in found_stale)
        protected.extend(Path(relative) / path for path in found_protected)
    print_stale(stale, protected)
    cleanup(staging)
    print(f"  完成，共 {count} 个文件")


def sync_loxodon(ref: str) -> None:
    print(f"[Loxodon] ref={ref}")
    payload = download(LOXODON_URL.format(ref=ref))
    archive_prefix = f"loxodon-framework-{ref.lstrip('v')}/"
    staging = Path(tempfile.mkdtemp(prefix="mvi-sync-loxodon-"))
    count = extract_prefix(payload, archive_prefix, LOXODON_MAP, staging)
    count += extract_files(payload, archive_prefix, LOXODON_EXTRA, staging)
    if not (staging / LOXODON_MARKER).exists():
        print(f"  校验失败：未找到 {LOXODON_MARKER}，未修改包内容")
        cleanup(staging)
        return
    stale: list[Path] = []
    protected: list[Path] = []
    for relative in LOXODON_MAP.values():
        found_stale, found_protected = copy_over(staging / relative, THIRD_PARTY / relative)
        stale.extend(Path(relative) / path for path in found_stale)
        protected.extend(Path(relative) / path for path in found_protected)
    for relative in LOXODON_EXTRA.values():
        target = THIRD_PARTY / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(staging / relative, target)
    print_stale(stale, protected)
    cleanup(staging)
    print(f"  完成，共 {count} 个文件")


# R3.Unity.asmdef 使用 overrideReferences + precompiledReferences，只认列表内的 DLL，
# 看不到 Unity 内置版本，因此下列 6 个程序集必须全部随包提供。
R3_NUGET = [
    ("r3", "1.3.0", "lib/netstandard2.1/R3.dll"),
    ("microsoft.bcl.asyncinterfaces", "6.0.0", "lib/netstandard2.1/Microsoft.Bcl.AsyncInterfaces.dll"),
    ("microsoft.bcl.timeprovider", "8.0.0", "lib/netstandard2.0/Microsoft.Bcl.TimeProvider.dll"),
    ("system.componentmodel.annotations", "5.0.0", "lib/netstandard2.1/System.ComponentModel.Annotations.dll"),
    ("system.runtime.compilerservices.unsafe", "6.0.0", "lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll"),
    ("system.threading.channels", "8.0.0", "lib/netstandard2.1/System.Threading.Channels.dll"),
]


def sync_r3(r3_version: str | None, upm_version: str) -> None:
    print(f"[R3] nuget={r3_version or '默认'} upm={upm_version}")
    target = THIRD_PARTY / R3_TARGET
    target.mkdir(parents=True, exist_ok=True)
    for package, version, member in R3_NUGET:
        current = r3_version if package == "r3" and r3_version else version
        url = f"https://api.nuget.org/v3-flatcontainer/{package}/{current}/{package}.{current}.nupkg"
        payload = download(url)
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            name = member.rsplit("/", 1)[-1]
            with archive.open(member) as source, open(target / name, "wb") as output:
                shutil.copyfileobj(source, output)
        print(f"  {name} <- {current}")
    if not (THIRD_PARTY / R3_MARKER).exists():
        print("  校验失败：R3.dll 未写入")
        return

    payload = download(R3_UPM_URL.format(ref=upm_version))
    prefix = R3_UPM_PREFIX.format(ref=upm_version)
    staging = Path(tempfile.mkdtemp(prefix="mvi-sync-r3-"))
    count = 0
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        for member in archive.namelist():
            if not member.startswith(prefix) or member.endswith("/"):
                continue
            relative = member[len(prefix):]
            if any(relative.startswith(item) or relative == item for item in R3_UPM_EXCLUDE):
                continue
            destination = staging / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, open(destination, "wb") as output:
                shutil.copyfileobj(source, output)
            count += 1
    if not (staging / "ObserveOnExtensions.cs").exists():
        print("  校验失败：R3.Unity 适配层结构不符，未修改包内容")
        cleanup(staging)
        return
    stale, protected = copy_over(staging, THIRD_PARTY / R3_UPM_TARGET)
    print_stale([Path(R3_UPM_TARGET) / path for path in stale], [Path(R3_UPM_TARGET) / path for path in protected])
    cleanup(staging)
    print(f"  R3.Unity 适配层 {count} 个文件")


def check_sources(ref_fairygui: str, ref_loxodon: str) -> None:
    targets = [
        FAIRYGUI_URL.format(ref=ref_fairygui),
        LOXODON_URL.format(ref=ref_loxodon),
    ]
    for url in targets:
        request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "mvi-sync"})
        with urllib.request.urlopen(request, timeout=60) as response:
            print(f"  {response.status} {url}")


def main() -> int:
    parser = argparse.ArgumentParser(description="同步 MVI 包内置的第三方依赖")
    parser.add_argument("target", nargs="?", choices=["fairygui", "loxodon", "r3", "all"], help="要同步的依赖")
    parser.add_argument("--ref", default=None, help="FairyGUI 分支/提交，或 Loxodon tag（默认 master / v2.6.7）")
    parser.add_argument("--r3-version", default=None, help="R3 NuGet 版本（默认 1.3.0）")
    parser.add_argument("--r3-upm-version", default="1.3.1", help="R3 Unity 适配层版本（默认 1.3.1）")
    parser.add_argument("--list", action="store_true", help="列出同步目标")
    parser.add_argument("--check", action="store_true", help="仅检查上游地址可达性")
    args = parser.parse_args()

    if args.list:
        print("目标：fairygui / loxodon / r3 / all")
        print(f"包根：{PACKAGE_ROOT}")
        return 0

    ref_fairygui = args.ref if args.ref else "master"
    ref_loxodon = args.ref if args.ref else "v2.6.7"

    if args.check:
        check_sources(ref_fairygui, ref_loxodon)
        return 0

    if not args.target:
        parser.print_help()
        return 1
    if not PACKAGE_ROOT.exists():
        print(f"找不到包目录：{PACKAGE_ROOT}", file=sys.stderr)
        return 1

    if args.target in ("fairygui", "all"):
        sync_fairygui(ref_fairygui)
    if args.target in ("loxodon", "all"):
        sync_loxodon(ref_loxodon)
    if args.target in ("r3", "all"):
        sync_r3(args.r3_version, args.r3_upm_version)

    print("同步完成，请回到 Unity 等待重新导入后检查编译结果。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
