"""Checks shared by the content-free and personal iOS package audits."""
from pathlib import Path


def audit_audio(archive, prefix, exported):
    repo = Path(__file__).resolve().parents[2]
    for source, name in (
        ('gameframe-wma/LICENSE-APACHE', 'gameframe-wma.txt'),
        ('qoaudio/LICENSE', 'qoaudio.txt'),
        ('gameframe-wma/THIRD_PARTY.md', 'gameframe-wma-THIRD-PARTY.txt'),
    ):
        member = prefix + 'notices/' + name
        if member not in archive.namelist() or archive.read(member) != (repo / 'tools/audio-converter/vendor' / source).read_bytes():
            raise SystemExit('Missing or changed converter notice: ' + name)
    for name in archive.namelist():
        relative = name.removeprefix(prefix).lower()
        if 'ffmpeg' in relative or Path(relative).name.startswith(('libavcodec', 'libavformat', 'libavutil')):
            raise SystemExit('Retired FFmpeg payload: ' + name)
    for symbol in exported:
        if symbol.lstrip('_').startswith(('av_', 'avcodec_', 'avformat_', 'avutil_', 'swr_', 'sws_')):
            raise SystemExit('Retired FFmpeg symbol: ' + symbol)
