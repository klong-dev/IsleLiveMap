using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;
public sealed class InboundCurrentJumpTests
{
    [Fact]
    public void Raw493SurvivalTail_DoesNotLabelUnknownTwoFieldsAsHealthAndStamina()
    {
        // 20260929 current-jump capture, owner 263880. These two optional
        // fields were 0.293/0.043 while full-frame HP/stamina were thousands/hundreds.
        var decoder = new ExperimentalVitalsDecoder();
        var at = DateTimeOffset.Parse("2026-09-29T07:33:35Z");
        decoder.Observe(Convert.FromBase64String("AAA0NrDl/////9OBAMkpiAUAAABCNiB4DwgBIADqMsnLJC+TPKmKnA0IUJhCg/43FUX9byqK2mgnELXRTiDqf1NR1P+moqjnhKVPzwlLn/GcMD3jOWF6kOgFAShAqCLoILaAUopTelW5HpQMljBw4Q5ZLBGAAoQqgg5iC/h6OKW7ldfXymDABGnmkLz7AChAqCLoILaAeIxbEnc5SaEMxmNgig0xixF0AoQqgg5Cy/d71aG467xDJ8x3wAMLEnQhAAUIVQQdxBbww3BKLCvvBYrBnAxa9SH9NwIZgFCpgesRPgg2fAAUIFQRdBBbwO67KemrfDxOBvvgL0WEwHIIQAFCFUEHsQXUJpxSO8pTomMwToLeCchAjKATIFQRdBBavsarDqVz1+s6YYD543CQAP8AKECoIuggtoD3e1MCUHkMogwGuO+OxA=="), at, "scope");
        var fields = decoder.Observe(Convert.FromBase64String("AACYNsXl/////9KBALImCAUAAABCNiB4DwgBIADqYpKLSS4mOamKnA0IUJhCg/8xFUX/YyqKQk8nEIWeTiD6H1NR9D+moiiDhKVPBglLn1yQMD25IGF6gEAHQCjIVSBLNQIZgFCpwC+WPMhRjUAGIFQq8IslD5JUI5ABCJUK/GLJg8jbCDoBQhVBB6Hl/gGFrhjALtIaFtOvWwFBqhHIAIRKBX6x5EESaASgAKGKoIPYAqIrbulo5tGJMliHAXM2RKlGIAMQKhX4xZIHMbURdAKEKoIOQsu3Q9UhaO4/pibMUSB9nwE="), at.AddSeconds(1), "scope");
        Assert.Contains(fields, e => e.Field == "HungerCandidate");
        Assert.Contains(fields, e => e.Field == "ThirstCandidate");
        Assert.DoesNotContain(fields, e => e.Field is "HealthCandidate" or "StaminaCandidate");
    }
}

