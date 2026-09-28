using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
using YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services;

internal static class OfflineReviewTests
{
    internal static void Run(Action<bool, string> check, string[] args)
    {
        string output = Value(args, "--output") ?? Path.GetFullPath("artifacts/pipeiso-offline");
        Directory.CreateDirectory(output);
        var scenes = new List<(string Name, ReviewScene Scene)>();
        foreach (string shape in new[] { "tee", "loop", "disconnected", "dense", "dense64", "vertical", "collapsed" })
        {
            var scene = new ReviewScene { Title = shape + " 合成案例", Evidence = "合成幾何測試，非實際模型。" };
            if (shape == "tee")
            {
                Add(scene, "P1", 0, 0, 4000, 0); Add(scene, "P2", 4000, 0, 8000, 0);
                Add(scene, "P3", 4000, 0, 4000, 4000);
            }
            if (shape == "loop")
            {
                Add(scene, "P1", 0, 0, 5000, 0); Add(scene, "P2", 5000, 0, 5000, 5000);
                Add(scene, "P3", 5000, 5000, 0, 5000); Add(scene, "P4", 0, 5000, 0, 0);
            }
            if (shape == "disconnected") { Add(scene, "P1", -5000, -6000, -3000, -4000); Add(scene, "P2", 2000, 4000, 4000, 2000); }
            if (shape == "dense")
                for (int i = 0; i < 24; i++) Add(scene, "P" + i.ToString("D2"), i * 280, 0, i * 280, 4000);
            if (shape == "dense64")
                for (int i = 0; i < 64; i++) Add(scene, "P" + (15300000 + i), i * 280, 0, i * 280, 4000);
            if (shape == "vertical") Add(scene, "P1", 0, 0, 0, 6000);
            if (shape == "collapsed") Add(scene, "P1", 0, 0, 0, 0);
            scenes.Add((shape, scene));
        }
        string pipes = Value(args, "--pipes");
        if (pipes != null) scenes.Add(("actual-pipes", FromPipes(pipes, check)));
        string replay = Value(args, "--review-json");
        if (replay != null)
        {
            var captured = JsonSerializer.Deserialize<ReviewScene>(File.ReadAllText(replay));
            check(captured.SchemaVersion == 1, "supported replay schema");
            scenes.Add(("replay", captured));
        }
        var report = new List<object>();
        foreach (var item in scenes)
        {
            var scene = item.Scene;
            List<ReviewPlacement> placement = null;
            try { placement = ReviewLayout.Arrange(scene); } catch (LayoutCrowdedException) { }
            if (placement == null || ReviewLayout.NeedsCompact(placement))
            {
                ReviewLayout.MakeCompact(scene);
                placement = ReviewLayout.Arrange(scene);
                check(scene.Labels.All(l => !string.IsNullOrEmpty(l.Detail)), item.Name + " compact mode preserves dimensions");
            }
            check(placement.Count == scene.Labels.Count, item.Name + " no dropped labels");
            bool overlaps = placement.Any(p => placement.Any(q => p != q && p.Box.Overlaps(q.Box, 0)));
            bool hitsPipe = placement.Any(p => scene.Lines.Any(l => ReviewLayout.Hits(p.Box, l, 0)));
            bool hitsLeader = placement.Any(p => placement.Any(q => p != q && ReviewLayout.Hits(p.Box, q.Leader, 0)));
            check(!overlaps && !hitsPipe && !hitsLeader, item.Name + " text clear of labels, model and leaders");
            string Signature() => string.Join(";", ReviewLayout.Arrange(scene).Select(p =>
                FormattableString.Invariant($"{p.Label.Code}:{p.Box.Left:R},{p.Box.Bottom:R}")));
            string initial = Signature();
            scene.Labels.Reverse(); scene.Lines.Reverse();
            check(Signature() == initial, item.Name + " deterministic independent of input order");
            scene.Title += " <&>測試";
            string svg = ReviewSvg.Render(scene);
            var xml = XDocument.Parse(svg);
            check(xml.Root.Name.LocalName == "svg" && xml.Descendants().Any(e => e.Value.Contains("<&>")), item.Name + " valid escaped SVG");
            AtomicOutput.Write(Path.Combine(output, item.Name + ".svg"), w => w.Write(svg));
            AtomicOutput.Write(Path.Combine(output, item.Name + ".json"), w => w.Write(JsonSerializer.Serialize(scene, new JsonSerializerOptions { WriteIndented = true })));
            report.Add(new { scenario = item.Name, labels = placement.Count, textCollisions = 0,
                leaderCrossingCandidates = placement.Sum(p => p.Crossings), evidence = scene.Evidence });
        }
        var bad = new ReviewScene();
        Add(bad, "same", 0, 0, 1, 1); Add(bad, "same", 0, 0, 1, 1);
        bool rejected = false;
        try { ReviewLayout.Arrange(bad); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "duplicate scene IDs rejected");
        bad.Labels.RemoveAt(1); bad.Labels[0].Width = double.NaN;
        rejected = false;
        try { ReviewLayout.Arrange(bad); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "invalid text geometry rejected");
        bad.Labels[0].Width = 100;
        bad.SchemaVersion = 2;
        rejected = false;
        try { ReviewLayout.Arrange(bad); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "unsupported replay version rejected");
        bad.SchemaVersion = 1; bad.Units = "feet";
        rejected = false;
        try { ReviewLayout.Arrange(bad); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "wrong replay units cannot silently change dimensions");
        var horizontal = new ReviewLine { A = new ReviewPoint(-10, 0), B = new ReviewPoint(10, 0) };
        check(ReviewLayout.Hits(new ViewRect(-1, -1, 1, 1), horizontal, 0), "line through text interior detected");
        check(!ReviewLayout.Hits(new ViewRect(-1, 2, 1, 3), horizontal, 0), "nearby separated line is clear");
        check(ReviewLayout.Intersects(horizontal, new ReviewLine { A = new ReviewPoint(5,0), B = new ReviewPoint(15,0) }), "collinear overlapping leaders detected");
        check(!ReviewLayout.Intersects(horizontal, new ReviewLine { A = new ReviewPoint(11,0), B = new ReviewPoint(15,0) }), "collinear separated leaders are clear");
        string atomic = Path.Combine(output, "atomic-check.txt");
        File.WriteAllText(atomic, "original");
        try { AtomicOutput.Write(atomic, writer => { writer.Write("partial"); throw new IOException("interrupted"); }); } catch (IOException) { }
        check(File.ReadAllText(atomic) == "original", "interrupted export preserves original");
        check(!Directory.EnumerateFiles(output, ".hb-iso-*.tmp").Any(), "interrupted export cleans temporary file");
        AtomicOutput.Write(atomic, writer => writer.Write("complete"));
        check(File.ReadAllText(atomic) == "complete", "atomic replacement writes complete output");
        File.Delete(atomic);
        AtomicOutput.Write(Path.Combine(output, "report.json"), w => w.Write(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })));
        Console.WriteLine("Offline review scenarios: " + scenes.Count + "; output: " + output);
    }
    private static string Value(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    private static void Add(ReviewScene scene, string code, double x0, double y0, double x1, double y1)
    {
        scene.Lines.Add(new ReviewLine { Owner = code, A = new ReviewPoint(x0, y0), B = new ReviewPoint(x1, y1) });
        scene.Labels.Add(new ReviewLabel { Code = code, Anchor = new ReviewPoint((x0+x1)/2, (y0+y1)/2),
            Text = code + " 管徑 100 mm\n模型長度核對", Width = 2200, Height = 525 });
    }
    private static ReviewScene FromPipes(string path, Action<bool, string> check)
    {
        var scene = new ReviewScene { Title = "逐管資料離線排版", Evidence = "實際 Pipes.csv 的直管；估計字框。未含管件幾何，非完整系統預覽。" };
        using var parser = new TextFieldParser(path);
        parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
        var header = parser.ReadFields();
        double[] origin = null;
        double total = 0;
        int vertical = 0;
        while (!parser.EndOfData)
        {
            var cells = parser.ReadFields();
            string Cell(string name) => cells[Array.IndexOf(header, name)];
            double Number(string name) => double.Parse(Cell(name), CultureInfo.InvariantCulture);
            if (Cell("直管坡度絕對值%").Contains("曲管"))
                throw new InvalidOperationException("Pipes.csv 只有端點，曲管請使用完整 _Review.json 重播，不以端點直線冒充曲線。");
            var m = new PipeMeasurement { X0 = Number("端0_Xmm"), Y0 = Number("端0_Ymm"), Z0 = Number("端0_Zmm"),
                X1 = Number("端1_Xmm"), Y1 = Number("端1_Ymm"), Z1 = Number("端1_Zmm"),
                LengthMm = Number("模型長度mm"), DiameterMm = Number("管徑mm"), IsStraight = true };
            double distance = Math.Sqrt(Math.Pow(m.X1-m.X0,2)+Math.Pow(m.Y1-m.Y0,2)+Math.Pow(m.Z1-m.Z0,2));
            check(Math.Abs(distance-m.LengthMm) < 0.003, "actual rounded endpoints agree with pipe length " + Cell("ElementId"));
            check(Cell("加工切長mm") == "", "unknown cut length stays blank " + Cell("ElementId"));
            if (m.HorizontalMm <= 0.001) vertical++;
            origin ??= new[] { m.X0, m.Y0, m.Z0 };
            ReviewPoint Project(double x, double y, double z) => new ReviewPoint(
                ((x-origin[0])-(y-origin[1])) / Math.Sqrt(2), (-(x-origin[0])-(y-origin[1])+2*(z-origin[2])) / Math.Sqrt(6));
            var a = Project(m.X0,m.Y0,m.Z0); var b = Project(m.X1,m.Y1,m.Z1);
            string code = Cell("圖面編號"), text = m.Label(code);
            Add(scene, code, a.X,a.Y,b.X,b.Y);
            var label = scene.Labels.Last(); label.Text = text;
            label.Width = text.Split('\n').Max(s => s.Sum(c => c > 255 ? 1 : 0.65)) * 3.5 * scene.Scale;
            total += m.LengthMm;
        }
        check(scene.Labels.Count > 0 && total > 0, "pipe replay contains positive model quantities");
        if (scene.Labels.Count == 8 && scene.Labels.Any(l => l.Code == "P15399347") && scene.Labels.Any(l => l.Code == "P15399597"))
            check(Math.Abs(total-29542) < 0.001 && vertical == 1, "known fire sample total length and vertical pipe");
        return scene;
    }
}
