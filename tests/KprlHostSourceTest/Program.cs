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
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] f{n} pos=({obs.Movement.X:F0},{obs.Movement.Y:F0}) {vitalsText}");
    if (n >= 30) break;
}
Console.WriteLine(n >= 20 ? "PASS" : "FAIL");
