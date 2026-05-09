#!/usr/bin/env python3
"""
gen_presets.py — Pedal invFFT v2.2 preset bank generator.

Emits PedalInvFFT_Presets.prs.xml with 64 presets covering pads, leads,
plucks, bells, bass, formant/vocal patches, animated textures, and FX.

Per Build §3.3, ReBuzz looks up preset parameters by Index, not by Name.
PARAM_INDEX must reflect declaration order in PedalInvFFT.cs. Adding a
new parameter means appending to PARAM_INDEX (and DEFAULTS); existing
preset overrides keep working because their named keys still resolve.

Run from this directory:
    python3 gen_presets.py
"""

from pathlib import Path

# ── Parameter declaration order (must match PedalInvFFT.cs) ─────────────
PARAM_INDEX = {
    "Volume":         0,
    "Amp Attack":     1,
    "Amp Decay":      2,
    "Amp Sustain":    3,
    "Amp Release":    4,
    "Brightness":     5,
    "Tilt":           6,
    "Balance":        7,
    "Bright Attack":  8,
    "Bright Decay":   9,
    "Bright Sustain": 10,
    "Bright Release": 11,
    "Bright Amount":  12,
    "Formant Centre": 13,
    "Formant Width":  14,
    "Formant Amount": 15,
    "Glide":          16,
    "Stretch":        17,
    "Anim Rate":      18,
    "Anim Depth":     19,
    "LFO Rate":       20,
    "LFO Shape":      21,
    "LFO Pitch":      22,
    "LFO Bright":     23,
    "LFO Stretch":    24,
    "LFO Volume":     25,
    "LFO Formant":    26,
    "LFO Anim":       27,
}

DEFAULTS = {
    "Volume":         64,
    "Amp Attack":     24, "Amp Decay":      64, "Amp Sustain":    100, "Amp Release":    48,
    "Brightness":    127, "Tilt":           64, "Balance":         64,
    "Bright Attack":  32, "Bright Decay":   80, "Bright Sustain":   0, "Bright Release": 48, "Bright Amount": 64,
    "Formant Centre": 64, "Formant Width":  64, "Formant Amount":   0,
    "Glide":           0,
    "Stretch":        64, "Anim Rate":      32, "Anim Depth":       0,
    "LFO Rate":       64, "LFO Shape":       0,
    "LFO Pitch":      64, "LFO Bright":     64, "LFO Stretch":     64, "LFO Volume":     64,
    "LFO Formant":    64, "LFO Anim":       64,
}

# ── Preset bank — sparse overrides keyed by display name ────────────────
# Names are prefixed by category for a sensible menu order in ReBuzz.

PRESETS = {
    # ─── Pads (10) ────────────────────────────────────────────────────
    "Pad - Default": {
        "Volume": 56, "Amp Attack": 60, "Amp Release": 80,
        "Brightness": 80, "Bright Decay": 95, "Bright Amount": 92,
        "Anim Depth": 18,
    },
    "Pad - Soft Strings": {
        "Volume": 60, "Amp Attack": 56, "Amp Release": 80,
        "Brightness": 90, "Tilt": 70, "Stretch": 72,
        "Bright Decay": 90, "Bright Amount": 78,
        "Anim Depth": 22, "Anim Rate": 28,
        "LFO Rate": 78, "LFO Pitch": 74,
    },
    "Pad - Dark": {
        "Volume": 60, "Amp Attack": 76, "Amp Release": 84,
        "Brightness": 36, "Tilt": 48,
        "Anim Depth": 12,
    },
    "Pad - Bright Air": {
        "Volume": 50, "Amp Attack": 70, "Amp Release": 76,
        "Brightness": 110, "Tilt": 84, "Balance": 70,
        "Anim Depth": 24,
    },
    "Pad - Vox Choir": {
        "Volume": 56, "Amp Attack": 64, "Amp Release": 80,
        "Brightness": 90, "Formant Centre": 64, "Formant Width": 88, "Formant Amount": 92,
        "Anim Depth": 28, "Anim Rate": 22,
        "LFO Rate": 64, "LFO Pitch": 70,
    },
    "Pad - Slow Drone": {
        "Volume": 56, "Amp Attack": 100, "Amp Release": 90,
        "Brightness": 64, "Tilt": 60,
        "Anim Depth": 80, "Anim Rate": 18,
        "LFO Rate": 12, "LFO Bright": 80,
    },
    "Pad - Glass": {
        "Volume": 52, "Amp Attack": 64, "Amp Release": 78,
        "Brightness": 108, "Tilt": 76,
        "Stretch": 88,
        "Anim Depth": 38, "Anim Rate": 28,
    },
    "Pad - Filter Sweep": {
        "Volume": 56, "Amp Attack": 68, "Amp Release": 76,
        "Brightness": 64,
        "LFO Rate": 22, "LFO Bright": 96,
    },
    "Pad - Vibrato": {
        "Volume": 60, "Amp Attack": 64, "Amp Release": 76,
        "Brightness": 90,
        "LFO Rate": 80, "LFO Pitch": 78,
    },
    "Pad - Wah Wash": {
        "Volume": 56, "Amp Attack": 70, "Amp Release": 80,
        "Brightness": 80,
        "Formant Centre": 56, "Formant Width": 72, "Formant Amount": 80,
        "LFO Rate": 28, "LFO Formant": 96,
    },

    # ─── Leads (8) ────────────────────────────────────────────────────
    "Lead - Saw": {
        "Volume": 60, "Amp Attack": 18, "Amp Release": 36,
        "Brightness": 110,
    },
    "Lead - Sine Pure": {
        "Volume": 64, "Amp Attack": 18, "Amp Release": 40,
        "Brightness": 0, "Tilt": 32,
    },
    "Lead - Square": {
        "Volume": 56, "Amp Attack": 16, "Amp Release": 36,
        "Brightness": 100, "Balance": 28,
    },
    "Lead - Vibrato Sax": {
        "Volume": 58, "Amp Attack": 36, "Amp Release": 42,
        "Brightness": 96, "Tilt": 70,
        "Formant Centre": 60, "Formant Width": 76, "Formant Amount": 76,
        "LFO Rate": 84, "LFO Pitch": 80,
    },
    "Lead - Wah Funk": {
        "Volume": 56, "Amp Attack": 12, "Amp Release": 30,
        "Brightness": 110,
        "Formant Centre": 56, "Formant Width": 56, "Formant Amount": 100,
        "LFO Rate": 88, "LFO Formant": 100,
    },
    "Lead - Glide Mono": {
        "Volume": 60, "Amp Attack": 16, "Amp Release": 36,
        "Brightness": 100, "Glide": 64,
    },
    "Lead - Bell Lead": {
        "Volume": 56, "Amp Attack": 14, "Amp Release": 60,
        "Brightness": 110,
        "Stretch": 90,
    },
    "Lead - Trill": {
        "Volume": 56, "Amp Attack": 16, "Amp Release": 36,
        "Brightness": 100,
        "LFO Rate": 100, "LFO Shape": 70, "LFO Pitch": 92,
    },

    # ─── Plucks / Keys (8) ───────────────────────────────────────────
    "Pluck - Soft": {
        "Volume": 56, "Amp Attack": 4, "Amp Decay": 60, "Amp Sustain": 0, "Amp Release": 36,
        "Brightness": 80, "Tilt": 56,
    },
    "Pluck - Bright": {
        "Volume": 56, "Amp Attack": 2, "Amp Decay": 70, "Amp Sustain": 0, "Amp Release": 32,
        "Brightness": 110,
        "Bright Decay": 60, "Bright Amount": 90,
    },
    "Pluck - Bell": {
        "Volume": 56, "Amp Attack": 2, "Amp Decay": 80, "Amp Sustain": 0, "Amp Release": 56,
        "Brightness": 100,
        "Bright Decay": 70, "Bright Amount": 90,
        "Stretch": 92,
    },
    "Pluck - Mallet": {
        "Volume": 56, "Amp Attack": 2, "Amp Decay": 56, "Amp Sustain": 0, "Amp Release": 32,
        "Brightness": 96,
        "Bright Decay": 50, "Bright Amount": 100,
        "Stretch": 80,
    },
    "Pluck - EP Tine": {
        "Volume": 60, "Amp Attack": 2, "Amp Decay": 80, "Amp Sustain": 0, "Amp Release": 50,
        "Brightness": 88, "Tilt": 56,
        "Bright Decay": 56, "Bright Amount": 88,
        "Stretch": 78,
    },
    "Pluck - Harp": {
        "Volume": 60, "Amp Attack": 2, "Amp Decay": 90, "Amp Sustain": 0, "Amp Release": 48,
        "Brightness": 84,
    },
    "Pluck - Saw": {
        "Volume": 56, "Amp Attack": 2, "Amp Decay": 64, "Amp Sustain": 0, "Amp Release": 32,
        "Brightness": 110,
        "Bright Decay": 50, "Bright Amount": 90,
    },
    "Pluck - Sharp": {
        "Volume": 52, "Amp Attack": 0, "Amp Decay": 50, "Amp Sustain": 0, "Amp Release": 24,
        "Brightness": 120, "Tilt": 80,
        "Bright Decay": 36, "Bright Amount": 100,
    },

    # ─── Bells & Metals (10) ─────────────────────────────────────────
    "Bell - Soft": {
        "Volume": 52, "Amp Attack": 2, "Amp Decay": 100, "Amp Sustain": 0, "Amp Release": 80,
        "Brightness": 96,
        "Bright Decay": 80, "Bright Amount": 88,
        "Stretch": 92,
    },
    "Bell - Tubular": {
        "Volume": 50, "Amp Attack": 2, "Amp Decay": 110, "Amp Sustain": 0, "Amp Release": 90,
        "Brightness": 100,
        "Bright Decay": 90, "Bright Amount": 90,
        "Stretch": 100,
    },
    "Bell - Glass": {
        "Volume": 48, "Amp Attack": 2, "Amp Decay": 96, "Amp Sustain": 0, "Amp Release": 72,
        "Brightness": 108, "Tilt": 76,
        "Bright Decay": 72, "Bright Amount": 90,
        "Stretch": 108,
    },
    "Bell - Crystal SH": {
        "Volume": 48, "Amp Attack": 2, "Amp Decay": 100, "Amp Sustain": 0, "Amp Release": 80,
        "Brightness": 110,
        "Stretch": 96,
        "LFO Rate": 40, "LFO Shape": 110, "LFO Stretch": 90,
    },
    "Bell - Gong Wash": {
        "Volume": 50, "Amp Attack": 6, "Amp Decay": 120, "Amp Sustain": 0, "Amp Release": 100,
        "Brightness": 88, "Tilt": 56,
        "Bright Decay": 110, "Bright Amount": 80,
        "Stretch": 116,
        "Anim Depth": 32, "Anim Rate": 24,
    },
    "Bell - Mallet": {
        "Volume": 52, "Amp Attack": 2, "Amp Decay": 76, "Amp Sustain": 0, "Amp Release": 56,
        "Brightness": 96,
        "Bright Decay": 56, "Bright Amount": 92,
        "Stretch": 84,
    },
    "Bell - Animated": {
        "Volume": 48, "Amp Attack": 2, "Amp Decay": 110, "Amp Sustain": 0, "Amp Release": 90,
        "Brightness": 100,
        "Bright Decay": 90, "Bright Amount": 88,
        "Stretch": 96,
        "Anim Depth": 50, "Anim Rate": 28,
    },
    "Bell - Wind Chime": {
        "Volume": 48, "Amp Attack": 2, "Amp Decay": 90, "Amp Sustain": 0, "Amp Release": 100,
        "Brightness": 112, "Tilt": 76,
        "Stretch": 100,
        "Anim Depth": 40, "Anim Rate": 32,
        "LFO Rate": 30, "LFO Shape": 110, "LFO Pitch": 76,
    },
    "Bell - Breathing": {
        "Volume": 50, "Amp Attack": 2, "Amp Decay": 110, "Amp Sustain": 0, "Amp Release": 96,
        "Brightness": 96,
        "Bright Decay": 90, "Bright Amount": 88,
        "Stretch": 92,
        "LFO Rate": 18, "LFO Stretch": 88,
    },
    "Bell - Detuned": {
        "Volume": 50, "Amp Attack": 2, "Amp Decay": 100, "Amp Sustain": 0, "Amp Release": 84,
        "Brightness": 100,
        "Bright Decay": 80, "Bright Amount": 90,
        "Stretch": 88,
        "LFO Rate": 76, "LFO Pitch": 70,
    },

    # ─── Bass (4) ────────────────────────────────────────────────────
    "Bass - Sub": {
        "Volume": 76, "Amp Attack": 2, "Amp Release": 30,
        "Brightness": 24, "Tilt": 36, "Balance": 50,
    },
    "Bass - Saw": {
        "Volume": 64, "Amp Attack": 2, "Amp Decay": 50, "Amp Sustain": 80, "Amp Release": 32,
        "Brightness": 90, "Tilt": 50,
        "Bright Decay": 40, "Bright Amount": 90,
    },
    "Bass - Pluck": {
        "Volume": 64, "Amp Attack": 2, "Amp Decay": 56, "Amp Sustain": 0, "Amp Release": 28,
        "Brightness": 90, "Tilt": 48,
        "Bright Decay": 40, "Bright Amount": 96,
    },
    "Bass - Wobble": {
        "Volume": 60, "Amp Attack": 2, "Amp Release": 30,
        "Brightness": 80, "Tilt": 50,
        "LFO Rate": 70, "LFO Bright": 96,
    },

    # ─── Vocal / Formant (6) ─────────────────────────────────────────
    "Vox - Ah": {
        "Volume": 60, "Amp Attack": 36, "Amp Release": 56,
        "Brightness": 96,
        "Formant Centre": 70, "Formant Width": 76, "Formant Amount": 100,
    },
    "Vox - Ee": {
        "Volume": 60, "Amp Attack": 36, "Amp Release": 56,
        "Brightness": 100, "Tilt": 72,
        "Formant Centre": 96, "Formant Width": 60, "Formant Amount": 100,
    },
    "Vox - Oh": {
        "Volume": 60, "Amp Attack": 36, "Amp Release": 56,
        "Brightness": 80,
        "Formant Centre": 50, "Formant Width": 70, "Formant Amount": 100,
    },
    "Vox - Wah": {
        "Volume": 60, "Amp Attack": 24, "Amp Release": 48,
        "Brightness": 100,
        "Formant Centre": 60, "Formant Width": 64, "Formant Amount": 96,
        "LFO Rate": 80, "LFO Formant": 96,
    },
    "Vox - Choir Slow": {
        "Volume": 56, "Amp Attack": 80, "Amp Release": 90,
        "Brightness": 88,
        "Formant Centre": 64, "Formant Width": 88, "Formant Amount": 80,
        "Anim Depth": 30, "Anim Rate": 24,
    },
    "Vox - Talky SH": {
        "Volume": 56, "Amp Attack": 16, "Amp Release": 36,
        "Brightness": 96,
        "Formant Centre": 60, "Formant Width": 56, "Formant Amount": 100,
        "LFO Rate": 90, "LFO Shape": 110, "LFO Formant": 92,
    },

    # ─── Animated / Shimmer (10) ─────────────────────────────────────
    "Anim - Shimmer Pad": {
        "Volume": 50, "Amp Attack": 70, "Amp Release": 80,
        "Brightness": 96, "Tilt": 70,
        "Anim Depth": 64, "Anim Rate": 36,
    },
    "Anim - Sparkle": {
        "Volume": 48, "Amp Attack": 50, "Amp Release": 70,
        "Brightness": 110, "Tilt": 80,
        "Anim Depth": 96, "Anim Rate": 64,
    },
    "Anim - Glow": {
        "Volume": 52, "Amp Attack": 80, "Amp Release": 80,
        "Brightness": 80,
        "Anim Depth": 44, "Anim Rate": 24,
        "LFO Rate": 16, "LFO Bright": 76,
    },
    "Anim - Inhale Pad": {
        "Volume": 52, "Amp Attack": 90, "Amp Release": 80,
        "Brightness": 84,
        "Anim Depth": 52, "Anim Rate": 22,
        "LFO Rate": 12, "LFO Volume": 80, "LFO Anim": 84,
    },
    "Anim - Drift": {
        "Volume": 52, "Amp Attack": 80, "Amp Release": 90,
        "Brightness": 72,
        "Stretch": 76,
        "Anim Depth": 56, "Anim Rate": 18,
        "LFO Rate": 10, "LFO Stretch": 76,
    },
    "Anim - Sway": {
        "Volume": 52, "Amp Attack": 70, "Amp Release": 80,
        "Brightness": 92,
        "Anim Depth": 48, "Anim Rate": 28,
        "LFO Rate": 70, "LFO Pitch": 74,
    },
    "Anim - Aurora": {
        "Volume": 50, "Amp Attack": 90, "Amp Release": 90,
        "Brightness": 88, "Tilt": 72,
        "Stretch": 80,
        "Anim Depth": 70, "Anim Rate": 22,
        "LFO Rate": 14, "LFO Bright": 80, "LFO Stretch": 76,
    },
    "Anim - Pulse Field": {
        "Volume": 52, "Amp Attack": 60, "Amp Release": 76,
        "Brightness": 96,
        "Anim Depth": 60, "Anim Rate": 40,
        "LFO Rate": 76, "LFO Anim": 96,
    },
    "Anim - Ice Field": {
        "Volume": 50, "Amp Attack": 76, "Amp Release": 84,
        "Brightness": 110, "Tilt": 80,
        "Stretch": 88,
        "Anim Depth": 80, "Anim Rate": 50,
    },
    "Anim - Forest": {
        "Volume": 50, "Amp Attack": 80, "Amp Release": 84,
        "Brightness": 80,
        "Anim Depth": 48, "Anim Rate": 30,
        "LFO Rate": 96, "LFO Shape": 110, "LFO Pitch": 70,
    },

    # ─── Special FX (8) ──────────────────────────────────────────────
    "FX - Glitch": {
        "Volume": 50, "Amp Attack": 0, "Amp Decay": 30, "Amp Sustain": 60, "Amp Release": 24,
        "Brightness": 100,
        "Stretch": 96,
        "LFO Rate": 110, "LFO Shape": 110,
        "LFO Pitch": 96, "LFO Bright": 96, "LFO Stretch": 96, "LFO Volume": 92, "LFO Formant": 96,
    },
    "FX - Robot Voice": {
        "Volume": 56, "Amp Attack": 4, "Amp Release": 24,
        "Brightness": 90,
        "Formant Centre": 60, "Formant Width": 50, "Formant Amount": 100,
        "LFO Rate": 90, "LFO Shape": 110, "LFO Formant": 96,
    },
    "FX - Quantum": {
        "Volume": 50, "Amp Attack": 60, "Amp Release": 70,
        "Brightness": 96,
        "Stretch": 110,
        "Anim Depth": 96, "Anim Rate": 60,
        "LFO Rate": 50, "LFO Shape": 110, "LFO Stretch": 100,
    },
    "FX - Tape Stop": {
        "Volume": 60, "Amp Attack": 16, "Amp Release": 100,
        "Brightness": 80, "Glide": 110,
    },
    "FX - Mass Confusion": {
        "Volume": 48, "Amp Attack": 30, "Amp Release": 60,
        "Brightness": 100,
        "Stretch": 96,
        "Anim Depth": 80, "Anim Rate": 70,
        "LFO Rate": 80, "LFO Pitch": 90, "LFO Stretch": 88, "LFO Anim": 84,
    },
    "FX - Unstable": {
        "Volume": 52, "Amp Attack": 24, "Amp Release": 50,
        "Brightness": 96,
        "Anim Depth": 60, "Anim Rate": 80,
        "LFO Rate": 96, "LFO Shape": 110,
        "LFO Pitch": 88, "LFO Bright": 88, "LFO Stretch": 88,
    },
    "FX - Time Crystal": {
        "Volume": 48, "Amp Attack": 50, "Amp Release": 100,
        "Brightness": 110, "Tilt": 80,
        "Stretch": 104,
        "Anim Depth": 70, "Anim Rate": 24,
        "LFO Rate": 6, "LFO Stretch": 80, "LFO Anim": 76,
    },
    "FX - Maelstrom": {
        "Volume": 48, "Amp Attack": 70, "Amp Release": 90,
        "Brightness": 100,
        "Stretch": 84,
        "Anim Depth": 80, "Anim Rate": 50,
        "LFO Rate": 24,
        "LFO Pitch": 78, "LFO Bright": 88, "LFO Stretch": 88, "LFO Volume": 76,
        "LFO Formant": 76, "LFO Anim": 76,
    },
}


def emit_xml(presets, machine_name="Pedal invFFT"):
    out = ['<?xml version="1.0" encoding="utf-8"?>', '<PresetDictionary>']
    for name, overrides in presets.items():
        # Sanity: every override key must be a known param
        unknown = [k for k in overrides if k not in PARAM_INDEX]
        if unknown:
            raise ValueError(f"Preset '{name}' has unknown params: {unknown}")

        out.append(f'  <Item Key="{name}">')
        out.append(f'    <Preset Machine="{machine_name}">')
        out.append('      <Parameters>')
        for pname, pidx in PARAM_INDEX.items():
            value = overrides.get(pname, DEFAULTS[pname])
            out.append(
                f'        <Parameter Name="{pname}" Group="1" '
                f'Index="{pidx}" Track="0" Value="{value}" />'
            )
        out.append('      </Parameters>')
        out.append('      <Attributes />')
        out.append('      <Comment></Comment>')
        out.append('    </Preset>')
        out.append('  </Item>')
    out.append('</PresetDictionary>')
    return '\n'.join(out) + '\n'


if __name__ == "__main__":
    xml = emit_xml(PRESETS)
    out_path = Path(__file__).parent / "PedalInvFFT_Presets.prs.xml"
    # UTF-8 with BOM per Build §3.1
    with open(out_path, "w", encoding="utf-8-sig") as f:
        f.write(xml)
    print(f"Wrote {len(PRESETS)} presets → {out_path}")
