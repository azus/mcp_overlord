// Writes a full-resolution stored-block PNG from a top-down BGRA buffer.
// One zlib stored block per row, one running IDAT CRC. Row bands can be encoded
// while later bands are still being copied. fpng supplies the CRC and Adler.

#include "fpng.h"

#include <cstdint>
#include <cstring>
#include <intrin.h>
#include <mutex>

#if defined(_M_X64) || defined(_M_IX86)
#include <immintrin.h>
#define SNAP_HAS_SSE 1
#else
#define SNAP_HAS_SSE 0
#endif

// Keep this layout in step with SnapState in NativeStoredPng.cs.
struct SnapState
{
    const uint8_t* bgra;
    uint8_t* png;
    uint8_t* cursor;
    int32_t width;
    int32_t height;
    int32_t stride;
    int32_t rowBytes;
    int32_t zlibLength;
    int32_t length;
    uint32_t adler;
    uint32_t crc;
    int32_t nextY;
};

static bool g_ssse3 = false;

static void EnsureInit()
{
    static std::once_flag once;
    std::call_once(once, []
    {
        fpng::fpng_init();
#if SNAP_HAS_SSE
        int info[4] = {};
        __cpuid(info, 1);
        g_ssse3 = (info[2] & (1 << 9)) != 0;
#else
        g_ssse3 = false;
#endif
    });
}

static void WriteBe32(uint8_t* dest, uint32_t value)
{
    dest[0] = (uint8_t)(value >> 24);
    dest[1] = (uint8_t)(value >> 16);
    dest[2] = (uint8_t)(value >> 8);
    dest[3] = (uint8_t)value;
}

static int PngLength(int width, int height)
{
    if (width <= 0 || height <= 0)
    {
        return 0;
    }

    int rowBytes = width * 3 + 1;
    if (rowBytes > 65535)
    {
        return 0;
    }

    long long raw = (long long)rowBytes * height;
    long long zlib = 2 + raw + (long long)height * 5 + 4;
    long long png = 57 + zlib;
    if (png <= 0 || png > 0x7fffffff)
    {
        return 0;
    }

    return (int)png;
}

static void SwizzleScalar(const uint8_t* bgra, uint8_t* rgb, int width)
{
    for (int x = 0; x < width; x++)
    {
        rgb[0] = bgra[2];
        rgb[1] = bgra[1];
        rgb[2] = bgra[0];
        bgra += 4;
        rgb += 3;
    }
}

static void SwizzleRow(const uint8_t* bgra, uint8_t* rgb, int width)
{
#if SNAP_HAS_SSE
    if (g_ssse3)
    {
        const __m128i shuffle = _mm_setr_epi8(2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, -1, -1, -1, -1);
        int x = 0;
        for (; x + 4 <= width; x += 4)
        {
            __m128i pixels = _mm_loadu_si128(reinterpret_cast<const __m128i*>(bgra + (x * 4)));
            __m128i packed = _mm_shuffle_epi8(pixels, shuffle);
            _mm_storeu_si128(reinterpret_cast<__m128i*>(rgb + (x * 3)), packed);
        }

        for (; x < width; x++)
        {
            const uint8_t* pixel = bgra + (x * 4);
            uint8_t* dest = rgb + (x * 3);
            dest[0] = pixel[2];
            dest[1] = pixel[1];
            dest[2] = pixel[0];
        }

        return;
    }
#endif

    SwizzleScalar(bgra, rgb, width);
}

static int Begin(const uint8_t* bgra, int width, int height, int stride, uint8_t* png, int capacity, SnapState* state)
{
    EnsureInit();
    int length = PngLength(width, height);
    if (state == nullptr || length == 0 || bgra == nullptr || png == nullptr || capacity < length || stride < width * 4)
    {
        return 0;
    }

    int rowBytes = width * 3 + 1;
    long long raw = (long long)rowBytes * height;
    int zlibLength = (int)(2 + raw + (long long)height * 5 + 4);

    static const uint8_t signature[8] = { 137, 80, 78, 71, 13, 10, 26, 10 };
    std::memcpy(png, signature, 8);
    WriteBe32(png + 8, 13);
    std::memcpy(png + 12, "IHDR", 4);
    WriteBe32(png + 16, (uint32_t)width);
    WriteBe32(png + 20, (uint32_t)height);
    png[24] = 8;
    png[25] = 2;
    png[26] = 0;
    png[27] = 0;
    png[28] = 0;
    WriteBe32(png + 29, fpng::fpng_crc32(png + 12, 17, 0));

    WriteBe32(png + 33, (uint32_t)zlibLength);
    std::memcpy(png + 37, "IDAT", 4);
    png[41] = 0x78;
    png[42] = 0x01;

    state->bgra = bgra;
    state->png = png;
    state->cursor = png + 43;
    state->width = width;
    state->height = height;
    state->stride = stride;
    state->rowBytes = rowBytes;
    state->zlibLength = zlibLength;
    state->length = length;
    state->adler = 1;
    state->crc = fpng::fpng_crc32(png + 37, 6, 0);
    state->nextY = 0;
    return 1;
}

static int Rows(SnapState* state, int y, int count)
{
    if (state == nullptr || y != state->nextY || count <= 0 || y + count > state->height)
    {
        return 0;
    }

    uint8_t* start = state->cursor;
    int rowBytes = state->rowBytes;
    int width = state->width;
    int stride = state->stride;
    const uint8_t* bgra = state->bgra;
    uint32_t adler = state->adler;
    for (int row = 0; row < count; row++)
    {
        int absolute = y + row;
        uint8_t* cursor = state->cursor;
        cursor[0] = (uint8_t)(absolute == state->height - 1 ? 1 : 0);
        cursor[1] = (uint8_t)(rowBytes & 0xff);
        cursor[2] = (uint8_t)((rowBytes >> 8) & 0xff);
        unsigned nlen = (~(unsigned)rowBytes) & 0xffffu;
        cursor[3] = (uint8_t)(nlen & 0xff);
        cursor[4] = (uint8_t)((nlen >> 8) & 0xff);
        cursor[5] = 0;
        SwizzleRow(bgra + ((long long)absolute * stride), cursor + 6, width);
        adler = fpng::fpng_adler32(cursor + 5, (size_t)rowBytes, adler);
        state->cursor = cursor + 5 + rowBytes;
    }

    state->adler = adler;
    state->crc = fpng::fpng_crc32(start, (size_t)(state->cursor - start), state->crc);
    state->nextY = y + count;
    return 1;
}

static int End(SnapState* state)
{
    if (state == nullptr || state->nextY != state->height || state->png == nullptr || state->cursor == nullptr)
    {
        return 0;
    }

    WriteBe32(state->cursor, state->adler);
    uint32_t crc = fpng::fpng_crc32(state->cursor, 4, state->crc);
    state->cursor += 4;
    WriteBe32(state->cursor, crc);
    state->cursor += 4;
    if (state->cursor != state->png + (state->length - 12))
    {
        return 0;
    }

    WriteBe32(state->cursor, 0);
    std::memcpy(state->cursor + 4, "IEND", 4);
    WriteBe32(state->cursor + 8, 0xAE426082u);
    return state->length;
}

extern "C" __declspec(dllexport) void snap_init()
{
    EnsureInit();
}

extern "C" __declspec(dllexport) int snap_begin(
    const uint8_t* bgra,
    int width,
    int height,
    int stride,
    uint8_t* dest,
    int capacity,
    SnapState* state)
{
    return Begin(bgra, width, height, stride, dest, capacity, state);
}

extern "C" __declspec(dllexport) int snap_rows(SnapState* state, int y, int count)
{
    return Rows(state, y, count);
}

extern "C" __declspec(dllexport) int snap_end(SnapState* state)
{
    return End(state);
}

extern "C" __declspec(dllexport) int snap_stored_into(
    const uint8_t* bgra,
    int width,
    int height,
    int stride,
    uint8_t* dest,
    int capacity)
{
    SnapState state = {};
    if (!Begin(bgra, width, height, stride, dest, capacity, &state))
    {
        return 0;
    }

    if (!Rows(&state, 0, height))
    {
        return 0;
    }

    return End(&state);
}
