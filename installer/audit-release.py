"""Audit repository inputs and the one-file Windows installer before release."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import mmap
import re
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TEXT_SUFFIXES = {".cs", ".csproj", ".json", ".md", ".props", ".ps1", ".py", ".iss", ".svg", ".xml", ".yml", ".yaml"}
GAME_SUFFIXES = {".xap", ".xnb", ".wma"}
RUNTIME_FILES = {
    "Z0MB1ES.exe", "Port.Bridge.dll", "Port.Hooks.dll", "FNA.dll",
    "FNA.dll.config", "SDL3.dll", "FNA3D.dll", "FAudio.dll",
    "libtheorafile.dll", "D3D12/D3D12Core.dll", "notices/LICENSE",
    "notices/lzxdecoder.LICENSE", "notices/monoxna.LICENSE",
    "notices/FAudio-LICENSE", "notices/FNA3D-LICENSE",
    "notices/SDL3-CS-LICENSE", "notices/MojoShader-LICENSE.txt",
    "notices/Theorafile-LICENSE", "notices/libogg-LICENSE",
    "notices/libtheora-LICENSE", "notices/libvorbis-LICENSE",
    "notices/Mono.Cecil-NOTICE.txt", "notices/xnb-parse-LICENSE",
    "notices/Python-LICENSE.txt", "notices/Pillow-LICENSE", "notices/NumPy-LICENSE.txt",
    "notices/PyInstaller-COPYING.txt", "notices/dotnet-LICENSE.TXT",
    "notices/dotnet-THIRD-PARTY-NOTICES.TXT", "notices/FFmpeg-LICENSE.txt",
    "notices/FFmpeg-COPYING.LGPLv2.1", "notices/FFmpeg-LICENSE.md",
    "notices/FFmpeg-libogg-COPYING", "notices/FFmpeg-libvorbis-COPYING",
    "notices/FFmpeg-SOURCE.txt",
}
FFMPEG_SOURCES = {
    "ffmpeg-239f2c733d.tar.gz": "11b3259addbc18cfb5cf4b198a746e8bd34828419f4284a3d00599abaadd0244",
    "libogg-1.3.6.tar.gz": "83e6704730683d004d20e21b8f7f55dcb3383cdf84c0daedf30bde175f774638",
    "libvorbis-1.3.7.tar.xz": "b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b",
}
FFMPEG_LICENSE_MEMBERS = {
    "FFmpeg-COPYING.LGPLv2.1": ("ffmpeg-239f2c733d.tar.gz", "FFmpeg-239f2c733d/COPYING.LGPLv2.1"),
    "FFmpeg-LICENSE.md": ("ffmpeg-239f2c733d.tar.gz", "FFmpeg-239f2c733d/LICENSE.md"),
    "FFmpeg-libogg-COPYING": ("libogg-1.3.6.tar.gz", "libogg-1.3.6/COPYING"),
    "FFmpeg-libvorbis-COPYING": ("libvorbis-1.3.7.tar.xz", "libvorbis-1.3.7/COPYING"),
}
SENSITIVE = {
    "developer path": re.compile(rb"(?:[A-Za-z]:[/\\](?:Users|ProgramData|Gabriel)[/\\]|/home/[A-Za-z0-9_-]+/)", re.I),
    "private key": re.compile(rb"-----BEGIN (?:RSA |OPENSSH |EC |DSA )?PRIVATE KEY-----"),
    "access token": re.compile(rb"(?:gh[pousr]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|AKIA[A-Z0-9]{16})"),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def scan_bytes(data: bytes | mmap.mmap, label: str, findings: list[str], *, game_data: bool = False) -> None:
    for name, pattern in SENSITIVE.items():
        if pattern.search(data):
            findings.append(f"{label}: {name}")
    if game_data and (b"XNBm" in data or b"XNBw" in data):
        findings.append(f"{label}: possible original XNB data")


def source_files(root: Path) -> list[Path]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        check=True, capture_output=True,
    )
    return [root / name.decode("utf-8", "surrogateescape") for name in result.stdout.split(b"\0") if name]


def audit_sources(root: Path, game_hashes: set[str], findings: list[str]) -> int:
    files = source_files(root)
    for path in files:
        if not path.is_file():
            continue
        rel = path.relative_to(root).as_posix()
        if path.suffix.lower() in GAME_SUFFIXES or path.name.lower() == "zombieswp7.dll":
            findings.append(f"{rel}: game file in repository inputs")
        if path.suffix.lower() == ".pdb":
            findings.append(f"{rel}: symbol file in repository inputs")
        if sha256(path) in game_hashes:
            findings.append(f"{rel}: original game hash")
        if path.suffix.lower() in TEXT_SUFFIXES and path.stat().st_size < 8_000_000:
            scan_bytes(path.read_bytes(), rel, findings)
    return len(files)


def allowed_payload_file(rel: str) -> bool:
    parts = rel.split("/")
    if rel == "helper/Port.SetupHelper.exe" or rel == "config/supported-versions.json":
        return True
    if parts[0] == "runtime":
        return "/".join(parts[1:]) in RUNTIME_FILES
    if parts[0] == "tools":
        if rel in {"tools/ffmpeg.exe", "tools/inspect_assembly.exe", "tools/decode_360_xnb.exe", "tools/Port.AssetImporter.exe"}:
            return True
        return len(parts) == 3 and parts[1] == "abi" and Path(parts[2]).suffix.lower() in {".dll", ".exe", ".json"}
    return False


def audit_payload(payload: Path, game_hashes: set[str], findings: list[str]) -> int:
    if not payload.is_dir():
        findings.append("payload: missing")
        return 0
    files = sorted(path for path in payload.rglob("*") if path.is_file())
    for path in files:
        rel = path.relative_to(payload).as_posix()
        label = f"payload/{rel}"
        if not allowed_payload_file(rel):
            findings.append(f"{label}: outside payload allow-list")
        if path.suffix.lower() in GAME_SUFFIXES or path.name.lower() == "zombieswp7.dll":
            findings.append(f"{label}: original game filename")
        if path.suffix.lower() == ".pdb":
            findings.append(f"{label}: symbols included")
        if sha256(path) in game_hashes:
            findings.append(f"{label}: original game hash")
        if path.stat().st_size <= 8_000_000 and (path.suffix.lower() in TEXT_SUFFIXES or path.suffix.lower() in {".txt", ".config"}):
            scan_bytes(path.read_bytes(), label, findings, game_data=True)
        if path.suffix.lower() in {".exe", ".dll"}:
            with path.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as view:
                if SENSITIVE["developer path"].search(view):
                    findings.append(f"{label}: developer path")
    required = {"helper/Port.SetupHelper.exe", "tools/ffmpeg.exe", "tools/decode_360_xnb.exe",
                "tools/Port.AssetImporter.exe", "config/supported-versions.json"}
    required.update(f"runtime/{name}" for name in RUNTIME_FILES if name.startswith("notices/FFmpeg-"))
    present = {path.relative_to(payload).as_posix() for path in files}
    for rel in sorted(required - present):
        findings.append(f"payload/{rel}: required file missing")
    return len(files)


def audit_ffmpeg_source_bundle(bundle: Path, payload: Path, findings: list[str]) -> str:
    if not bundle.is_file():
        findings.append("FFmpeg corresponding-source ZIP: missing")
        return ""
    pin = json.loads((ROOT / "config/content-tools.json").read_text())["ffmpeg"]["executableSha256"]
    converter = payload / "tools/ffmpeg.exe"
    if converter.is_file() and sha256(converter) != pin:
        findings.append("payload/tools/ffmpeg.exe: differs from source package pin")
    if converter.is_file() and b"libwinpthread-1.dll" in converter.read_bytes().lower():
        findings.append("payload/tools/ffmpeg.exe: unexpected libwinpthread dependency")
    expected = {"README.txt", "installer/ffmpeg/Dockerfile", "installer/ffmpeg/build.sh"}
    expected.update("source/" + name for name in FFMPEG_SOURCES)
    try:
        with zipfile.ZipFile(bundle) as archive:
            if set(archive.namelist()) != expected:
                findings.append("FFmpeg corresponding-source ZIP: unexpected or missing members")
            for name, digest in FFMPEG_SOURCES.items():
                member = "source/" + name
                if member in archive.namelist() and hashlib.sha256(archive.read(member)).hexdigest() != digest:
                    findings.append(f"FFmpeg corresponding-source ZIP: wrong {name}")
            for name in ("Dockerfile", "build.sh"):
                member = "installer/ffmpeg/" + name
                if member in archive.namelist() and archive.read(member) != (ROOT / member).read_bytes():
                    findings.append(f"FFmpeg corresponding-source ZIP: stale {name}")
            if "README.txt" in archive.namelist() and pin.encode() not in archive.read("README.txt"):
                findings.append("FFmpeg corresponding-source ZIP: binary pin missing")
            for notice, (source_name, source_member) in FFMPEG_LICENSE_MEMBERS.items():
                source_path = "source/" + source_name
                if source_path not in archive.namelist():
                    continue
                with tarfile.open(fileobj=io.BytesIO(archive.read(source_path)), mode="r:*") as source:
                    file = source.extractfile(source_member)
                    if file is None:
                        findings.append(f"FFmpeg corresponding-source ZIP: missing {source_member}")
                        continue
                    installed = payload / "runtime/notices" / notice
                    if installed.is_file() and installed.read_bytes() != file.read():
                        findings.append(f"payload/runtime/notices/{notice}: differs from matching source")
            source_notice = payload / "runtime/notices/FFmpeg-SOURCE.txt"
            if source_notice.is_file():
                content = source_notice.read_bytes()
                if b"FFmpeg-8.1.1-source.zip" not in content or pin.encode() not in content:
                    findings.append("payload/runtime/notices/FFmpeg-SOURCE.txt: wrong source or binary pin")
    except (OSError, zipfile.BadZipFile, tarfile.TarError, KeyError) as error:
        findings.append(f"FFmpeg corresponding-source ZIP: unreadable ({error})")
    return sha256(bundle)


def audit_setup(setup: Path, original_prefixes: list[bytes], findings: list[str]) -> str:
    if not setup.is_file():
        findings.append("setup: missing")
        return ""
    with setup.open("rb") as stream:
        header = stream.read(2)
    if header != b"MZ":
        findings.append("setup: not a Windows executable")
    with setup.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as view:
        if SENSITIVE["developer path"].search(view):
            findings.append("setup: developer path")
        for prefix in original_prefixes:
            if prefix and view.find(prefix) >= 0:
                findings.append("setup: embedded original game byte sequence")
    return sha256(setup)


def original_inputs(xap: Path | None) -> tuple[set[str], list[bytes]]:
    if xap is None:
        return set(), []
    hashes = {sha256(xap)}
    prefixes = [xap.read_bytes()[:128]]
    with zipfile.ZipFile(xap) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            hashes.add(hashlib.sha256(archive.read(entry)).hexdigest())
        dll = archive.read("ZombiesWP7.dll")
    hashes.add(hashlib.sha256(dll).hexdigest())
    prefixes.append(dll[:128])
    return hashes, prefixes


def negative_controls() -> None:
    with tempfile.TemporaryDirectory() as temp:
        base = Path(temp)
        payload = base / "payload"
        for rel in ("helper/Port.SetupHelper.exe", "tools/ffmpeg.exe", "config/supported-versions.json"):
            path = payload / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"clean")
        injections = {
            "game filename": ("runtime/ZombiesWP7.dll", b"game", "original game filename"),
            "original XNB": ("runtime/FNA.dll.config", b"XNBm" + b"\0" * 16, "possible original XNB data"),
            "developer path": ("runtime/FNA.dll.config", b"C:\\Users\\Example\\private", "developer path"),
            "private key": ("runtime/FNA.dll.config", b"-----BEGIN " + b"PRIVATE KEY-----", "private key"),
        }
        for label, (rel, data, expected) in injections.items():
            path = payload / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            findings: list[str] = []
            audit_payload(payload, set(), findings)
            path.unlink()
            if not any(expected in finding for finding in findings):
                raise RuntimeError(f"negative control was not rejected: {label}")
        exe = base / "test.exe"
        exe.write_bytes(b"MZ" + b"\0" * 20 + b"C:\\Users\\Example\\private")
        findings = []
        audit_setup(exe, [], findings)
        if not any("developer path" in finding for finding in findings):
            raise RuntimeError("setup negative control was not rejected")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, default=ROOT / "installer/out/payload")
    parser.add_argument("--setup", type=Path, default=ROOT / "installer/out/Z0MB1ES-1.0.0.exe")
    parser.add_argument("--xap", type=Path, help="local supported XAP for exact-byte checks")
    args = parser.parse_args()
    negative_controls()
    game_hashes, prefixes = original_inputs(args.xap)
    findings: list[str] = []
    source_count = audit_sources(ROOT, game_hashes, findings)
    payload_count = audit_payload(args.payload, game_hashes, findings)
    setup_hash = audit_setup(args.setup, prefixes, findings)
    source_hash = audit_ffmpeg_source_bundle(ROOT / "installer/out/FFmpeg-8.1.1-source.zip", args.payload, findings)
    print(f"AUDIT source_files={source_count} payload_files={payload_count} setup_sha256={setup_hash}")
    print(f"FFMPEG_SOURCE sha256={source_hash}")
    print("NEGATIVE_CONTROLS passed=5")
    for finding in findings:
        print(f"FAIL {finding}")
    if findings:
        print(f"AUDIT_FAILED findings={len(findings)}")
        return 1
    print("AUDIT_CLEAN")
    return 0


if __name__ == "__main__":
    sys.exit(main())
