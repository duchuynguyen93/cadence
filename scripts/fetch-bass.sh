#!/usr/bin/env bash
#
# Tải thư viện native của BASS từ un4seen.com về thư mục native/.
#
# Vì sao cần script này: gói ManagedBass trên NuGet CHỈ là lớp wrapper P/Invoke,
# không kèm bass.dll. Và các file .dll/.dylib này là phần mềm độc quyền nên KHÔNG
# được commit vào git (xem .gitignore) — ai clone repo về phải tự chạy script này.
#
# GIẤY PHÉP: BASS miễn phí cho phần mềm phi thương mại / miễn phí. Nếu định BÁN app
# thì phải mua license từ https://www.un4seen.com/bass.html (khoảng €125 trở lên).
# Toàn bộ code gọi BASS nằm sau interface IAudioEngine, nên nếu không muốn trả phí
# thì chỉ cần viết một implementation khác (LibVLCSharp hoặc NAudio) là xong.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NATIVE="$ROOT/native"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT

mkdir -p "$NATIVE/win-x64" "$NATIVE/osx"

download() {
    local name="$1"
    echo "  tải $name.zip"
    curl -fsSL --max-time 120 -o "$TEMP/$name.zip" "https://www.un4seen.com/files/$name.zip"
}

echo "Đang tải BASS cho Windows (x64)…"
for pkg in bass24 bassflac24 basswasapi24 bassmix24; do download "$pkg"; done

unzip -j -o "$TEMP/bass24.zip"       "x64/bass.dll"       -d "$NATIVE/win-x64" > /dev/null
unzip -j -o "$TEMP/bassflac24.zip"   "x64/bassflac.dll"   -d "$NATIVE/win-x64" > /dev/null
unzip -j -o "$TEMP/basswasapi24.zip" "x64/basswasapi.dll" -d "$NATIVE/win-x64" > /dev/null
unzip -j -o "$TEMP/bassmix24.zip"    "x64/bassmix.dll"    -d "$NATIVE/win-x64" > /dev/null

# macOS chỉ cần khi dev trên Mac. Windows build không dùng tới.
if [[ "$(uname -s)" == "Darwin" ]]; then
    echo "Đang tải BASS cho macOS (để dev)…"
    for pkg in bass24-osx bassflac24-osx bassmix24-osx; do download "$pkg"; done

    unzip -j -o "$TEMP/bass24-osx.zip"     "libbass.dylib"     -d "$NATIVE/osx" > /dev/null
    unzip -j -o "$TEMP/bassflac24-osx.zip" "libbassflac.dylib" -d "$NATIVE/osx" > /dev/null
    unzip -j -o "$TEMP/bassmix24-osx.zip"  "libbassmix.dylib"  -d "$NATIVE/osx" > /dev/null
fi

echo ""
echo "Xong. Nội dung native/:"
find "$NATIVE" -type f \( -name '*.dll' -o -name '*.dylib' \) | sort | sed "s|$ROOT/|  |"
