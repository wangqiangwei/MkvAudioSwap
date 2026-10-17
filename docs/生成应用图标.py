"""生成「替音工具」的应用图标（app.png / app.ico）。

设计：暖墨绿圆角方块 + 白色播放三角 + 两根高度不同的电平条。
- 16/24/32px 下先保证“有东西、不糊”
- 256px 下能看出这是音频/媒体工具
- 颜色与界面主色 #4A7560 / #4F7C66 / #3F6654 同源

用法（需要 Pillow）：
    python docs/生成应用图标.py

输出：
    src/MkvAudioSwap.App/Assets/app.png      512×512
    src/MkvAudioSwap.App/Assets/app.ico      16–256 多尺寸

预览请直接看 Assets/app.png；不要把截图提交到仓库。
"""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "MkvAudioSwap.App" / "Assets"

S = 4
N = 256 * S

TOP = (79, 124, 102, 255)   # #4F7C66
BOTTOM = (63, 102, 84, 255)  # #3F6654
WHITE = (255, 255, 255, 255)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def base_icon() -> Image.Image:
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))

    grad = Image.new("RGBA", (N, N))
    gd = ImageDraw.Draw(grad)
    for y in range(N):
        gd.line([(0, y), (N, y)], fill=lerp(TOP, BOTTOM, y / (N - 1)))

    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, N - 1, N - 1], radius=int(N * 0.225), fill=255
    )
    img.paste(grad, (0, 0), mask)

    # 顶部柔光，避免大面积纯色发闷
    hi = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)
    for y in range(int(N * 0.45)):
        alpha = int(26 * (1 - y / (N * 0.45)))
        hd.line([(0, y), (N, y)], fill=(255, 255, 255, alpha))
    hi.putalpha(Image.composite(hi.getchannel("A"), Image.new("L", (N, N), 0), mask))
    img = Image.alpha_composite(img, hi)

    # 细内边，缩到 16px 时仍有“圆角卡片”的轮廓
    edge = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle(
        [int(N * 0.012), int(N * 0.012), N - 1 - int(N * 0.012), N - 1 - int(N * 0.012)],
        radius=int(N * 0.215),
        outline=(255, 255, 255, 22),
        width=max(1, int(N * 0.006)),
    )
    return Image.alpha_composite(img, edge)


def draw_icon() -> Image.Image:
    img = base_icon()
    d = ImageDraw.Draw(img)

    # 播放三角
    d.polygon(
        [(int(N * 0.20), int(N * 0.30)), (int(N * 0.20), int(N * 0.70)),
         (int(N * 0.50), int(N * 0.50))],
        fill=WHITE,
    )

    # 两根电平条：高度不同，避免读成“暂停”
    bar_width = int(N * 0.075)
    for x, h in ((0.64, 0.26), (0.73, 0.42)):
        cx = int(N * x)
        half = int(N * h) // 2
        d.rounded_rectangle(
            [cx - bar_width // 2, int(N * 0.50) - half,
             cx + bar_width // 2, int(N * 0.50) + half],
            radius=bar_width // 2,
            fill=WHITE,
        )
    return img


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)

    icon = draw_icon()
    icon.resize((512, 512), Image.LANCZOS).save(ASSETS / "app.png")
    icon.resize((256, 256), Image.LANCZOS).save(
        ASSETS / "app.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    for name in ("app.png", "app.ico"):
        p = ASSETS / name
        print(f"{p}  {p.stat().st_size} bytes")


if __name__ == "__main__":
    main()
