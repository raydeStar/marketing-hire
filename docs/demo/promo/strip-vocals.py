"""Removes stray vocals from a music take with Demucs (htdemucs): keeps drums + bass + other, drops the vocal stem.

Run with a Python that has torch and soundfile, with Demucs on PYTHONPATH and TORCH_HOME pointing at a disposable cache:
    python docs/demo/promo/strip-vocals.py in.wav out.wav
"""
import sys

import numpy as np
import soundfile as sf
import torch
from demucs.apply import apply_model
from demucs.pretrained import get_model

src, dst = sys.argv[1], sys.argv[2]
model = get_model("htdemucs")
model.eval()
audio, rate = sf.read(src, dtype="float32", always_2d=True)
if rate != model.samplerate:
    raise SystemExit(f"resample to {model.samplerate} Hz first (got {rate})")
mix = torch.from_numpy(audio.T.copy())
ref = mix.mean(0)
mix = (mix - ref.mean()) / (ref.std() + 1e-8)
device = "cuda" if torch.cuda.is_available() else "cpu"
with torch.no_grad():
    stems = apply_model(model, mix[None], device=device, shifts=2, split=True, overlap=0.25, progress=False)[0]
stems = stems * (ref.std() + 1e-8) + ref.mean()
names = model.sources
vocal = stems[names.index("vocals")]
music = sum(stems[i] for i, name in enumerate(names) if name != "vocals")
sf.write(dst, music.cpu().numpy().T, rate, subtype="PCM_24")


def rms(x):
    return float(20 * np.log10(np.sqrt(np.mean(x.cpu().numpy() ** 2)) + 1e-12))


print({"device": device, "sources": names, "vocal_rms_db": round(rms(vocal), 1), "music_rms_db": round(rms(music), 1)})
