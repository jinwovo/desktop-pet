"""Make a GitHub GIF from the public WPF smoke test's recorded HTML.

Optional documentation tooling only: Python + Pillow. No app dependency.
The HTML must contain only Companion, Nimbus and Ember. All positions come
from PetBrain/WalkAppearanceController; this script does not simulate gait.
"""
import argparse
import base64
import io
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


def build(source: Path, destination: Path) -> None:
    html = source.read_text(encoding="utf-8")
    decoder = json.JSONDecoder()
    marker = "const sprites="
    sprite_data, length = decoder.raw_decode(html.split(marker, 1)[1])
    track_text = html.split(marker, 1)[1][length:]
    tracks, _ = decoder.raw_decode(track_text.split("tracks=", 1)[1])
    expected = {"Companion", "Nimbus", "Ember"}
    if len(tracks) != 3 or {track["name"] for track in tracks} != expected:
        raise ValueError("Use an HTML capture built with public assets only (all three characters).")
    images = [Image.open(io.BytesIO(base64.b64decode(src.split(",", 1)[1]))).convert("RGBA")
              for src in sprite_data]
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
    small = ImageFont.truetype(str(font_path), 13) if font_path.exists() else ImageFont.load_default()
    rendered = []
    length = min(len(track["frames"]) for track in tracks)
    for frame_index in range(0, length, 3):  # 60Hz recording -> 20fps documentation preview
        canvas = Image.new("RGBA", (640, 490), "#f4f7f1")
        draw = ImageDraw.Draw(canvas)
        draw.text((22, 14), "DesktopPet  |  a little company while you work", font=font, fill="#354e3b")
        for lane, track in enumerate(tracks):
            top = 42 + lane * 142
            step = track["frames"][frame_index]
            draw.text((22, top + 2), track["name"], font=small, fill="#6d7c70")
            draw.line((20, top + 130, 620, top + 130), fill="#d9e3d4", width=1)
            sprite = Image.new("RGBA", (128, 128))
            if step["live"]:
                for part, x, y in (("far", step["farX"], step["farY"]),
                                   ("near", step["nearX"], step["nearY"]),
                                   ("body", 0, step["bodyY"])):
                    # Subpixel sampling matches retained sprite transforms at preview resolution.
                    translated = images[step[part]].transform(
                        (128, 128), Image.Transform.AFFINE, (1, 0, -x, 0, 1, -y),
                        resample=Image.Resampling.BICUBIC)
                    sprite.alpha_composite(translated)
            else:
                sprite.alpha_composite(images[step["sprite"]])
            if step["mirror"]:
                sprite = ImageOps.mirror(sprite)
            canvas.alpha_composite(sprite, (round(step["x"]), top + 8))
        rendered.append(canvas.convert("RGB"))
    palette = rendered[0].quantize(colors=192)
    frames = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in rendered]
    destination.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(destination, save_all=True, append_images=frames[1:], duration=50,
                   loop=0, optimize=True, disposal=1)
    print(f"Public preview: {destination} ({destination.stat().st_size:,} bytes, {len(frames)} frames)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("html", type=Path, help="public-assets UI capture character-walk-v8.html")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "docs/preview/public-walk.gif")
    args = parser.parse_args()
    build(args.html, args.output)
