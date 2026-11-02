"""生成 AF 原创共鸣音节库，24kHz/16-bit mono，每段 110ms。仅开发时运行。"""
import math
import random
import struct
from pathlib import Path

rate = 24000
length = 2640
vowels = [(730, 1090, 2440), (270, 2290, 3010), (300, 870, 2240),
          (530, 1840, 2480), (570, 840, 2410)]
samples = bytearray()
random_source = random.Random(2718)
for index in range(40):
    formants = vowels[index % len(vowels)]
    fundamental = 180 + index % 8 * 13
    sound = []
    for frame in range(length):
        t = frame / rate
        phase = 2 * math.pi * fundamental * (t + 0.15 * t * t)
        value = sum(math.sin(phase * harmonic) * (
            0.15 / harmonic + sum(math.exp(-0.5 * ((fundamental * harmonic - f) / 130) ** 2)
                                 for f in formants) / math.sqrt(harmonic))
                    for harmonic in range(1, 23))
        onset = math.exp(-t * 100) * (index % 4) * 0.13
        value += random_source.uniform(-1, 1) * onset
        envelope = min(1, frame / 192, (length - 1 - frame) / 192)
        sound.append(value * envelope)
    peak = max(map(abs, sound))
    samples.extend(b''.join(struct.pack('<h', round(value / peak * 24000)) for value in sound))
target = Path(__file__).resolve().parents[1] / 'src/AFMediaBar/Resources/Audio/PetSyllables.pcm'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(samples)
