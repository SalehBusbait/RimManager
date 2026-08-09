#!/usr/bin/env python3
"""Generates the ten Avalonia theme dictionaries from the accepted design's tokens.css.

Source of truth: assets/themes/tokens.css — a build input, which is why it lives
beside this script rather than in docs/ (see assets/themes/SOURCES.md for where
the design tokens came from).
Output: src/RimManager.App/Themes/Tokens.<Id>.axaml, one per theme, 62 Rm* keys
plus the 11 Fluent Slider/RadioButton keys the retired AccentPalette.Derive used
to write at runtime (implementation-notes.md: they live in each dictionary now).

Hand-edits go to the CSS (upstream first), never to the generated axaml — rerun:
    python assets/themes/generate-tokens.py

One deliberate deviation from the CSS: --rm-shadow there is the mockup's
downward card shadow; the app's one composed BoxShadows (RmDrawerShadow) is the
info-pane drawer's LEFTWARD shadow (-8 0 24 0). The geometry is kept and only
the per-theme colour weight is taken from the CSS's first shadow layer.
"""
import io
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(os.path.dirname(__file__), "tokens.css")
OUT_DIR = os.path.join(ROOT, "src", "RimManager.App", "Themes")

# (css id, pascal id, display name, variant)
THEMES = [
    ("droppods-dark", "DropPodsDark", "Drop Pods Dark", "dark"),
    ("droppods-light", "DropPodsLight", "Drop Pods Light", "light"),
    ("tribal", "Tribal", "Tribal Dawn", "dark"),
    ("arid", "Arid", "Arid Rim", "light"),
    ("ice", "Ice", "Ice Sheet", "dark"),
    ("toxic", "Toxic", "Toxic Fallout", "dark"),
    ("mech", "Mech", "Mechanoid Threat", "dark"),
    ("royalty", "Royalty", "Imperial Court", "dark"),
    ("anomaly", "Anomaly", "Void Provocation", "dark"),
    ("glitter", "Glitter", "Glitterworld", "dark"),
]

# css handle -> Avalonia key. Order here is emission order (grouped as the old
# hand-written dictionaries were, so diffs stay readable).
BRUSHES = [
    ("canvas", "RmBackgroundCanvasBrush"),
    ("surface", "RmBackgroundSurfaceBrush"),
    ("alt", "RmBackgroundAltBrush"),
    ("elevated", "RmBackgroundElevatedBrush"),
    ("zebra", "RmBackgroundZebraBrush"),
    ("hover", "RmBackgroundHoverBrush"),
    ("selected", "RmBackgroundSelectedBrush"),
    ("selected-idle", "RmBackgroundSelectedIdleBrush"),
    ("divider", "RmBorderDividerBrush"),
    ("subtle", "RmBorderSubtleBrush"),
    ("strong", "RmBorderStrongBrush"),
    ("thumb", "RmScrollThumbBrush"),
    ("text-1", "RmTextPrimaryBrush"),
    ("text-2", "RmTextSecondaryBrush"),
    ("text-3", "RmTextTertiaryBrush"),
    ("text-4", "RmTextDisabledBrush"),
    ("accent", "RmAccentBrush"),
    ("accent-hover", "RmAccentHoverBrush"),
    ("accent-tint", "RmAccentTintBrush"),
    ("accent-text", "RmAccentTextBrush"),
    ("on-accent", "RmOnAccentBrush"),
    ("brand", "RmBrandBrush"),
    ("focus", "RmFocusRingBrush"),
    ("ok", "RmSuccessBrush"),
    ("ok-tint", "RmSuccessTintBrush"),
    ("warn", "RmWarningBrush"),
    ("warn-tint", "RmWarningTintBrush"),
    ("bad", "RmDangerBrush"),
    ("bad-tint", "RmDangerTintBrush"),
    ("bad-hover", "RmDangerHoverBrush"),
    ("info", "RmInfoBrush"),
    ("info-tint", "RmInfoTintBrush"),
    ("harmony", "RmHarmonyBrush"),
    ("harmony-tint", "RmHarmonyTintBrush"),
    ("p0", "RmPalette0Brush"),
    ("p1", "RmPalette1Brush"),
    ("p2", "RmPalette2Brush"),
    ("p3", "RmPalette3Brush"),
    ("p4", "RmPalette4Brush"),
    ("p5", "RmPalette5Brush"),
    # O14 · the favourite pill. Its own hue, fixed at 310 in the CSS because that
    # is the widest gap on the palette wheel (p3 ~0, p4 ~261) — a favourite that
    # matched a tag colour would be indistinguishable from that tag. Saturation
    # and lightness come from each theme's own p3, so it inherits that theme's
    # contrast conventions instead of importing one constant everywhere.
    ("fav", "RmFavouriteBrush"),
    ("fav-bg", "RmFavouriteBgBrush"),
    ("p0-bg", "RmPalette0BgBrush"),
    ("p1-bg", "RmPalette1BgBrush"),
    ("p2-bg", "RmPalette2BgBrush"),
    ("p3-bg", "RmPalette3BgBrush"),
    ("p4-bg", "RmPalette4BgBrush"),
    ("p5-bg", "RmPalette5BgBrush"),
    ("src-core", "RmSrcCoreFgBrush"),
    ("src-core-bg", "RmSrcCoreBgBrush"),
    ("src-dlc", "RmSrcDlcFgBrush"),
    ("src-dlc-bg", "RmSrcDlcBgBrush"),
    ("src-workshop", "RmSrcWorkshopFgBrush"),
    ("src-workshop-bg", "RmSrcWorkshopBgBrush"),
    ("src-local", "RmSrcLocalFgBrush"),
    ("src-local-bg", "RmSrcLocalBgBrush"),
    ("src-git", "RmSrcGitFgBrush"),
    ("src-git-bg", "RmSrcGitBgBrush"),
    ("src-pin", "RmSrcPinnedFgBrush"),
    ("src-pin-bg", "RmSrcPinnedBgBrush"),
    ("diff-add", "RmDiffAddedBrush"),
    ("diff-del", "RmDiffRemovedBrush"),
    ("scrim", "RmScrimBrush"),
]

# Fluent keys for the controls we never re-templated (Slider, plain RadioButton).
# AccentPalette.Derive wrote these at runtime; the dictionaries carry them now.
FLUENT = [
    ("SliderTrackValueFill", "accent"),
    ("SliderTrackValueFillPointerOver", "accent-hover"),
    ("SliderTrackValueFillPressed", "accent"),
    ("SliderThumbBackground", "accent"),
    ("SliderThumbBackgroundPointerOver", "accent-hover"),
    ("SliderThumbBackgroundPressed", "accent"),
    ("RadioButtonOuterEllipseCheckedFill", "accent"),
    ("RadioButtonOuterEllipseCheckedFillPointerOver", "accent-hover"),
    ("RadioButtonOuterEllipseCheckedFillPressed", "accent"),
    ("RadioButtonOuterEllipseCheckedStroke", "accent"),
    ("RadioButtonCheckGlyphFill", "on-accent"),
]


def parse_css(text):
    themes = {}
    for m in re.finditer(r'\[data-theme="([a-z-]+)"\]\{([^}]*)\}', text):
        block = {}
        for decl in m.group(2).split(";"):
            decl = decl.strip()
            if not decl:
                continue
            name, _, value = decl.partition(":")
            block[name.strip().removeprefix("--rm-")] = value.strip()
        themes[m.group(1)] = block
    return themes


def rgba_to_hex(value):
    m = re.fullmatch(r"rgba\((\d+),(\d+),(\d+),([0-9.]+)\)", value.replace(" ", ""))
    if not m:
        raise SystemExit(f"unparseable rgba: {value}")
    r, g, b = (int(m.group(i)) for i in (1, 2, 3))
    a = round(float(m.group(4)) * 255)
    return f"#{a:02X}{r:02X}{g:02X}{b:02X}"


def shadow_alpha(value):
    # First layer's rgba alpha, e.g. "0 14px 36px rgba(0,0,0,.5), ..." -> 0.5
    m = re.search(r"rgba\(\d+,\s*\d+,\s*\d+,\s*(\.?[0-9.]+)\)", value)
    if not m:
        raise SystemExit(f"unparseable shadow: {value}")
    return round(float(m.group(1)) * 255)


def emit(css_id, pascal, display, variant, tokens):
    lines = []
    lines.append('<ResourceDictionary xmlns="https://github.com/avaloniaui"')
    lines.append('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
    lines.append("  <!--")
    lines.append(f"    {display} ({variant}) — GENERATED by assets/themes/generate-tokens.py from")
    lines.append("    assets/themes/tokens.css. Do not hand-edit: change the CSS and rerun")
    lines.append("    (provenance of the tokens is in assets/themes/SOURCES.md).")
    lines.append("")
    lines.append("    THE RULE (design non-negotiable #1): every colour in the app is a")
    lines.append("    {DynamicResource Rm*} pointing at a dictionary like this one. Key parity")
    lines.append("    across all ten dictionaries is test-enforced (ThemeTokenTests).")
    lines.append("  -->")
    for handle, key in BRUSHES:
        value = tokens[handle]
        if value.startswith("rgba"):
            value = rgba_to_hex(value)
        lines.append(f'  <SolidColorBrush x:Key="{key}" Color="{value}" />')
    alpha = shadow_alpha(tokens["shadow"])
    lines.append("  <!-- The info-pane drawer's leftward shadow — geometry is the app's, only the")
    lines.append("       colour weight comes from the design's shadow token (see generator header). -->")
    lines.append(f'  <BoxShadows x:Key="RmDrawerShadow">-8 0 24 0 #{alpha:02X}000000</BoxShadows>')
    lines.append("  <!-- The command-palette shell's downward shadow (S-LEGEND/S-SELECTOR cite the")
    lines.append("       palette as the reference shell, and it shipped shadowless). Same colour")
    lines.append("       weight, centred-overlay geometry. -->")
    lines.append(f'  <BoxShadows x:Key="RmFlyoutShadow">0 8 24 0 #{alpha:02X}000000</BoxShadows>')
    lines.append("  <!-- Fluent keys for the un-retemplated controls (Slider, plain RadioButton);")
    lines.append("       AccentPalette.Derive wrote these at runtime before it was retired. -->")
    for key, handle in FLUENT:
        lines.append(f'  <SolidColorBrush x:Key="{key}" Color="{tokens[handle]}" />')
    lines.append("</ResourceDictionary>")
    lines.append("")

    path = os.path.join(OUT_DIR, f"Tokens.{pascal}.axaml")
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    return path


def main():
    with io.open(SRC, "r", encoding="utf-8") as f:
        themes = parse_css(f.read())

    if len(themes) != len(THEMES):
        raise SystemExit(f"expected {len(THEMES)} theme blocks, found {len(themes)}")

    for css_id, pascal, display, variant in THEMES:
        tokens = themes[css_id]
        missing = [h for h, _ in BRUSHES if h not in tokens]
        if missing or "shadow" not in tokens:
            raise SystemExit(f"{css_id}: missing handles {missing}")
        path = emit(css_id, pascal, display, variant, tokens)
        print(f"wrote {os.path.relpath(path, ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
