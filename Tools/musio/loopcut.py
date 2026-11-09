"""Musio の書き出し（前に PRE 小節・後に POST 小節の写しつき）から、継ぎ目のないループを切り出す。
py -3 loopcut.py IN.wav OUT.wav BPM PRE_BARS MAIN_BARS [BEATS_PER_BAR] [GAIN_DB]
- 本編の頭（PRE+1 小節目）から本編の終わりまでを切り出す。
- 頭の 50ms は「本編の終わりの続き（後の写し）」から本編の頭へ重ねてつなぐので、くり返しても音が途切れない。
- 24bit を 16bit（三角ディザ）にして書く。"""
import array, random, struct, sys, math

src, dst = sys.argv[1], sys.argv[2]
bpm = float(sys.argv[3]); pre = int(sys.argv[4]); main = int(sys.argv[5])
bpb = int(sys.argv[6]) if len(sys.argv) > 6 else 4
gain_db = float(sys.argv[7]) if len(sys.argv) > 7 else 0.0

data = open(src, "rb").read()
pos = 12
fmt = None
while pos < len(data):
    cid = data[pos:pos + 4]
    size = struct.unpack("<I", data[pos + 4:pos + 8])[0]
    if cid == b"fmt ":
        fmt = struct.unpack("<HHIIHH", data[pos + 8:pos + 24])
    elif cid == b"data":
        pcm = data[pos + 8:pos + 8 + size]
    pos += 8 + size + (size & 1)
tag, ch, rate, _, align, bits = fmt
assert tag == 1 and bits == 24 and ch == 2, fmt

# 24bit → int（32bit の上位に詰めて 256 で割る）
n = len(pcm) // 3
padded = bytearray(n * 4)
padded[1::4] = pcm[0::3]
padded[2::4] = pcm[1::3]
padded[3::4] = pcm[2::3]
s = array.array("i")
s.frombytes(bytes(padded))
frames = n // 2


def t2f(bar):
    return round((bar - 1) * bpb * 60.0 / bpm * rate)


start, end = t2f(pre + 1), t2f(pre + main + 1)
length = end - start
xf = int(0.05 * rate)
assert end + xf <= frames, (end, frames)
g = 10 ** (gain_db / 20.0) / 65536.0  # 上位に詰めた 32bit を 16bit の大きさへ
rng = random.Random(7)
out = array.array("h", bytes(length * 4))
peak = 0
for i in range(length):
    a = start + i
    for c in range(2):
        v = s[2 * a + c]
        if i < xf:
            w = 0.5 - 0.5 * math.cos(math.pi * i / xf)  # 0→1
            v = v * w + s[2 * (end + i) + c] * (1.0 - w)
        v = v * g + (rng.random() - rng.random())
        iv = int(round(v))
        if iv > 32767: iv = 32767
        if iv < -32768: iv = -32768
        if abs(iv) > peak: peak = abs(iv)
        out[2 * i + c] = iv

body = out.tobytes()
hdr = b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVE"
hdr += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 2, rate, rate * 4, 4, 16)
hdr += b"data" + struct.pack("<I", len(body))
open(dst, "wb").write(hdr + body)
print(f"loop {length} frames = {length / rate:.3f} s @ {rate} Hz, peak {20 * math.log10(max(peak, 1) / 32768):.2f} dBFS")
