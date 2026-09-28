#!/bin/bash
set -eu

FFMPEG_VERSION=4.3.3
FFMPEG_FILE="ffmpeg-$FFMPEG_VERSION.tar.gz"

# Dependencies
OPUS_GIT="https://github.com/xiph/opus.git"
OPUS_RELEASE="v1.5.2"
LIBVPX_GIT="https://chromium.googlesource.com/webm/libvpx.git"
LIBVPX_RELEASE="v1.17.0"

FFMPEG_FLAGS=(
    # General options
    --disable-static
    --enable-shared
    --disable-all
    --disable-autodetect
    --enable-lto

    # Libraries
    --enable-avcodec
    --enable-avformat
    --enable-swscale

    # Legacy video formats
    --enable-demuxer='avi,flv,asf'
    --enable-parser='mpeg4video'
    --enable-decoder='flv,msmpeg4v1,msmpeg4v2,msmpeg4v3,mpeg4,vp6,vp6f,wmv2'

    # Modern video formats
    --enable-demuxer='mov,matroska' # mov = mp4, matroska = mkv & webm
    --enable-parser='h264,hevc,vp8,vp9'
    --enable-decoder='h264,hevc,vp8,vp9'

    # Encoding (webm, mkv)
    --enable-libvpx
    --enable-libopus
    --enable-swresample
    --enable-encoder='libvpx_vp9,libopus'
    --enable-muxer=matroska
    --enable-protocol='pipe,file'
)

# Variables to set in each OS script BEFORE calling build_deps:
#   DEPS_HOST        autotools triplet for opus (empty = native build)
#   VPX_TARGET       libvpx target (e.g. x86_64-linux-gcc, arm64-win64-gcc)
#   VPX_CROSS        tool prefix for libvpx (e.g. x86_64-w64-mingw32-), empty if native
#   VPX_EXTRA_ARGS   extra libvpx options (e.g. --enable-pic on Linux)
#   DEPS_CFLAGS      shared C/link flags (e.g. -fPIC, -arch arm64)

# Helper Methods
function do_git_checkout () {
    local repo_url="$1"
    local tag="$2"
    local to_dir="$3"

    if [ ! -d $to_dir ]; then
        echo "Cloning $repo_url@$tag to $to_dir"
        git clone -b $tag $repo_url $to_dir
    else
        echo "Skipping clone as $to_dir is already present."
    fi
}

# build_deps <target-name>   e.g. build_deps linux-x64
# Sources are cloned into "$PWD/<name>-packages", installed into "$PWD/<name>-deps"
function build_deps() {
    local name="$1"
    DEPS_PREFIX="$PWD/$name-deps"
    mkdir -p "$DEPS_PREFIX"

    local cflags="${DEPS_CFLAGS:-}"
    local configure_params=(--prefix="$DEPS_PREFIX" --enable-static --disable-shared)
    if [ -n "${DEPS_HOST:-}" ]; then
        configure_params+=(--host="$DEPS_HOST")
    fi

    mkdir -p "$name-packages"
    pushd "$name-packages" > /dev/null || exit 1

        # opus: audio codec
        do_git_checkout "$OPUS_GIT" "$OPUS_RELEASE" opus
        pushd opus > /dev/null || exit 1
        ./autogen.sh
        CFLAGS="$cflags" LDFLAGS="$cflags" ./configure "${configure_params[@]}" \
            --with-pic --disable-doc --disable-extra-programs
        make -j"$CORES"
        make install
        popd > /dev/null || exit 1

        # libvpx: VP8/VP9 video codec
        do_git_checkout "$LIBVPX_GIT" "$LIBVPX_RELEASE" vpx
        pushd vpx > /dev/null || exit 1
        mkdir -p build
        cd build || exit 1
        # shellcheck disable=SC2086
        CROSS="${VPX_CROSS:-}" ../configure \
            --target="$VPX_TARGET" \
            --prefix="$DEPS_PREFIX" \
            --enable-static --disable-shared \
            --disable-examples --disable-tools --disable-docs --disable-unit-tests \
            --extra-cflags="$cflags" \
            ${VPX_EXTRA_ARGS:-}
        make -j"$CORES"
        make install
        popd > /dev/null || exit 1

    popd > /dev/null || exit 1
}

function prep_ffmpeg() {
    FFMPEG_FLAGS+=(
        --prefix="$PWD/$1"
        --shlibdir="$PWD/$1"
    )

    # Use the libs built by build_deps (opus, libvpx)
    if [ -n "${DEPS_PREFIX:-}" ]; then
        export PKG_CONFIG_PATH="$DEPS_PREFIX/lib/pkgconfig"
        FFMPEG_FLAGS+=(
            --pkg-config-flags=--static
            --extra-cflags="-I$DEPS_PREFIX/include ${DEPS_CFLAGS:-}"
            --extra-ldflags="-L$DEPS_PREFIX/lib ${DEPS_CFLAGS:-}"
        )
    fi

    local build_dir="$1-build"
    if [ ! -e "$FFMPEG_FILE" ]; then
        echo "-> Downloading $FFMPEG_FILE..."
        curl -o "$FFMPEG_FILE" "https://ffmpeg.org/releases/$FFMPEG_FILE"
    else
        echo "-> $FFMPEG_FILE already exists, not re-downloading."
    fi

    if [ ! -d "$build_dir" ]; then
        echo "-> Unpacking source to $build_dir..."
        mkdir "$build_dir"
        tar xzf "$FFMPEG_FILE" --strip 1 -C "$build_dir"
    else
        echo "-> $build_dir already exists, skipping unpacking."
    fi

    echo "-> Configuring..."
    cd "$build_dir"
    ./configure "${FFMPEG_FLAGS[@]}"
}

function build_ffmpeg() {
    echo "-> Building using $CORES threads..."

    make -j$CORES
    make install-libs
}

CORES=0
if [[ "$OSTYPE" == "darwin"* ]]; then
    CORES=$(sysctl -n hw.ncpu)
else
    CORES=$(nproc)
fi
