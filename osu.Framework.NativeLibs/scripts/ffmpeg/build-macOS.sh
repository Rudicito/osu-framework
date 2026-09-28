#!/bin/bash
set -eu

pushd "$(dirname "$0")" > /dev/null
SCRIPT_PATH=$(pwd)
popd > /dev/null
source "$SCRIPT_PATH/common.sh"

brew install pkg-config autoconf automake libtool

if [ -z "${arch-}" ]; then
    PS3='Build for which arch? '
    select arch in "arm64" "x86_64"; do
        if [ -z "$arch" ]; then
            echo "invalid option"
        else
            break
        fi
    done
fi

# Dependencies (opus, libvpx) built statically for the same target
darwin="$(uname -r | cut -d. -f1)"   # e.g. 23 on macOS 14
if [ "$arch" = "x86_64" ]; then
    DEPS_HOST="x86_64-apple-darwin"
    VPX_TARGET="x86_64-darwin${darwin}-gcc"
else
    DEPS_HOST="aarch64-apple-darwin"
    VPX_TARGET="arm64-darwin${darwin}-gcc"
fi
VPX_CROSS=""
VPX_EXTRA_ARGS=""
DEPS_CFLAGS="-arch $arch"
build_deps "macOS-$arch"

FFMPEG_FLAGS+=(
    --enable-videotoolbox
    --enable-hwaccel=h264_videotoolbox
    --enable-hwaccel=hevc_videotoolbox
    --enable-hwaccel=vp9_videotoolbox

    --enable-cross-compile
    --target-os=darwin
    --arch=$arch
    --extra-cflags="-arch $arch"
    --extra-ldflags="-arch $arch"
    
    --install-name-dir='@loader_path'
)

pushd . > /dev/null
prep_ffmpeg "macOS-$arch"
build_ffmpeg
popd > /dev/null

# Rename dylibs that have a full version string (A.B.C) in their filename
# Example: avcodec.58.10.72.dylib -> avcodec.58.dylib
pushd . > /dev/null
cd "macOS-$arch"
for f in *.*.*.*.dylib; do
    [ -f "$f" ] || continue
    mv -v "$f" "${f%.*.*.*}.dylib"
done
popd > /dev/null
