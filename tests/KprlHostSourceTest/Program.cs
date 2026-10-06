using TheIsleOverlay.LocalTelemetry;

var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
await using var src = new KprlLocalMovementSource();
var n = 0;
await foreach (var obs in src.WatchAsync(cts.Token))
{
    n++;
    var v = obs.DinosaurVitals;
    string vitalsText;
    if (v is { } has)
    {
        var vv = has.Vitals;
        vitalsText = $"HP={vv.Health:F0}/{vv.MaxHealth:F0} ST={vv.Stamina:F0}/{vv.MaxStamina:F0} G={vv.Growth:P1}";
    }
    else
    {
        vitalsText = "NO VITALS";
    }
    if (n % 25 == 1) Console.WriteLine($"[f{n}] {vitalsText}");
    if (n >= 400) break;
}
Console.WriteLine(n >= 20 ? "PASS" : "FAIL");
