using SkiaSharp;
using Svg.Skia;

// Walk up to the repo root (the slnx marks it), tool-style.
var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RimManager.slnx")))
    dir = dir.Parent;
if (dir is null) { Console.Error.WriteLine("repo root not found"); return 1; }

var root = dir.FullName;
var badges = Path.Combine(root, "assets", "brand", "theme-badges");
var marks = Path.Combine(root, "src", "RimManager.App", "Assets", "marks");
Directory.CreateDirectory(marks);

// css-id -> AppTheme member name (the enum member IS the asset name).
var themes = new (string Css, string Pascal)[]
{
    ("droppods-dark", "DropPodsDark"),
    ("droppods-light", "DropPodsLight"),
    ("tribal", "Tribal"),
    ("arid", "Arid"),
    ("ice", "Ice"),
    ("toxic", "Toxic"),
    ("mech", "Mech"),
    ("royalty", "Royalty"),
    ("anomaly", "Anomaly"),
    ("glitter", "Glitter"),
};

const float Size = 256f;
foreach (var (css, pascal) in themes)
{
    var svgPath = Path.Combine(badges, $"badge-{css}.svg");
    using var svg = new SKSvg();
    if (svg.Load(svgPath) is not { } picture)
    {
        Console.Error.WriteLine($"could not load {svgPath}");
        return 1;
    }

    var scale = Size / picture.CullRect.Width;
    var outPath = Path.Combine(marks, $"mark-{pascal}.png");
    using var stream = File.Create(outPath);
    svg.Picture!.ToImage(stream, SKColor.Empty, SKEncodedImageFormat.Png, 100,
        scale, scale, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
    Console.WriteLine($"wrote {Path.GetRelativePath(root, outPath)}");
}

return 0;
