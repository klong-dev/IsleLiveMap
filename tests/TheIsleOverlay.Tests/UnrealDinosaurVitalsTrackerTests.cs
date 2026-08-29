using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class UnrealDinosaurVitalsTrackerTests
{
    [Fact]
    public void Track_LearnsOwningHandleAndReadsLiveGasAttributes()
    {
        var tracker = new UnrealDinosaurVitalsTracker();
        var startedAt = DateTimeOffset.Parse("2026-08-28T02:35:44.877Z");

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(FirstHeartbeat),
            startedAt,
            out _));

        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SecondHeartbeat),
            startedAt.AddSeconds(1.056),
            out var heartbeat));
        Assert.Equal(103_940UL, heartbeat.NetRefHandle);
        Assert.Equal(0.2961587607860565d, heartbeat.Vitals.Hunger);
        Assert.Equal(837.140869140625d, heartbeat.Vitals.Thirst);
        Assert.Null(heartbeat.Vitals.Health);

        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(PeriodicAttributes),
            startedAt.AddSeconds(2),
            out var complete));
        Assert.Equal(103_940UL, complete.NetRefHandle);
        Assert.Equal(0.10464542359113693d, complete.Vitals.Growth);
        Assert.Equal(4.601197242736816d, complete.Vitals.Health);
        Assert.Equal(4.601197242736816d, complete.Vitals.MaxHealth);
        Assert.Equal(175.84938049316406d, complete.Vitals.Stamina);
        Assert.Equal(175.84938049316406d, complete.Vitals.MaxStamina);
        Assert.Equal(1.518395185470581d, complete.Vitals.MaxHunger);
        Assert.Equal(1_000d, complete.Vitals.MaxThirst);
    }

    [Fact]
    public void Track_ReacquiresOwnerAfterHeartbeatTimeout()
    {
        var tracker = new UnrealDinosaurVitalsTracker();
        var startedAt = DateTimeOffset.Parse("2026-08-28T02:35:44.877Z");
        tracker.TryTrack(Convert.FromBase64String(FirstHeartbeat), startedAt, out _);
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SecondHeartbeat),
            startedAt.AddSeconds(1),
            out _));

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(FirstHeartbeat),
            startedAt.AddSeconds(20),
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SecondHeartbeat),
            startedAt.AddSeconds(21),
            out var reacquired));

        Assert.Equal(103_940UL, reacquired.NetRefHandle);
    }

    [Fact]
    public void Track_BootstrapsPositiveTrailingHealthAndStaminaBeforeMaximumFrame()
    {
        var tracker = new UnrealDinosaurVitalsTracker();
        var startedAt = DateTimeOffset.Parse("2026-08-28T07:47:24.209Z");

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(TrailingAttributesHeartbeat1),
            startedAt,
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(TrailingAttributesHeartbeat2),
            startedAt.AddSeconds(1.03),
            out var observation));

        Assert.Equal(208_310UL, observation.NetRefHandle);
        Assert.Equal(0.4000110626220703d, observation.Vitals.Hunger);
        Assert.Equal(990.9522705078125d, observation.Vitals.Thirst);
        Assert.Equal(1.0151753425598145d, observation.Vitals.Health);
        Assert.Equal(91.7905044555664d, observation.Vitals.Stamina);
        Assert.Null(observation.Vitals.MaxHealth);
        Assert.Null(observation.Vitals.MaxStamina);
    }

    [Fact]
    public void Track_RejectsPteranodonFalseHealthButKeepsTrailingStamina()
    {
        var tracker = new UnrealDinosaurVitalsTracker();
        var startedAt = DateTimeOffset.Parse("2026-08-28T16:04:00.000Z");

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(PteranodonHeartbeat1),
            startedAt,
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(PteranodonHeartbeat2),
            startedAt.AddSeconds(1),
            out var observation));

        Assert.Equal(54_816UL, observation.NetRefHandle);
        Assert.Null(observation.Vitals.Health);
        Assert.Equal(277.3397521972656d, observation.Vitals.Stamina);
    }

    [Fact]
    public void Track_ReadsSparseMaximumFrameAndRestoresItAfterHeartbeatGap()
    {
        var tracker = new UnrealDinosaurVitalsTracker();
        var startedAt = DateTimeOffset.Parse("2026-08-28T15:47:05.722Z");

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(SparseMaximumHeartbeat1),
            startedAt,
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SparseMaximumHeartbeat2),
            startedAt.AddSeconds(1),
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SparseMaximumAttributes),
            startedAt.AddSeconds(2),
            out var complete));

        Assert.Equal(54_816UL, complete.NetRefHandle);
        Assert.Equal(0.711446225643158d, complete.Vitals.Growth);
        Assert.Equal(62.79351043701172d, complete.Vitals.Health);
        Assert.Equal(62.79351043701172d, complete.Vitals.MaxHealth);
        Assert.Equal(777.4014282226562d, complete.Vitals.Stamina);
        Assert.Equal(777.4014282226562d, complete.Vitals.MaxStamina);
        Assert.Equal(20.721858978271484d, complete.Vitals.MaxHunger);

        Assert.False(tracker.TryTrack(
            Convert.FromBase64String(SparseMaximumHeartbeat1),
            startedAt.AddSeconds(20),
            out _));
        Assert.True(tracker.TryTrack(
            Convert.FromBase64String(SparseMaximumHeartbeat2),
            startedAt.AddSeconds(21),
            out var restored));

        Assert.Equal(62.79351043701172d, restored.Vitals.MaxHealth);
        Assert.Equal(777.4014282226562d, restored.Vitals.MaxStamina);
        Assert.Equal(20.721858978271484d, restored.Vitals.MaxHunger);
    }

    [Fact]
    public void DetachedFrame_SkipsDenormalOverlapAndReadsDamagedDinosaurVitals()
    {
        Assert.True(UnrealDinosaurVitalsTracker.TryDecodeDetachedPeriodicAttributeFrame(
            Convert.FromBase64String(DetachedDamagedAttributes),
            33.55d,
            out var vitals));

        Assert.Equal(0.026307906955480576d, vitals.Growth);
        Assert.Equal(83.77029418945312d, vitals.Health);
        Assert.Equal(98.46177673339844d, vitals.MaxHealth);
        Assert.Equal(169.23199462890625d, vitals.Stamina);
        Assert.Equal(169.23199462890625d, vitals.MaxStamina);
        Assert.Equal(49.23088836669922d, vitals.MaxHunger);
    }

    private const string FirstHeartbeat =
        "GABASKHd/////9GBACI1iAwAAAAisAyYDQgFIADqWzHiL1PVPpgpAADgQEI+8iMLkYJlAMpAlGbilz7NxC99+p1FEfU7iyJqJn7p00z80gcIWAGEgloA9EEUMwxoIITFE3AaEEIABhzCCIdCfAWSUfeFZylbUBZAyK/aiYC99TUPQ/LiCUCxygycDaIZOBtE3Q3jh7obxg8h+c8ACYBQEQj+M0ACIFQEApgL6AQIVQQdhFYyJCWOb0EZoV3YSUHPMMT+GSABECoCSX8GdAKEKoIOQosSjJQOHWR8TifsqyApFXK3DMABCGU7t9mDDyOez4BOgFBF0EFoaW5nigpX/s/qhNkNBH9BgpgBnQChiqCD0FKqIQr0/hthjwvjM/guhOyjAuABQgqAAL8z+VE4fgCcElj2aSP/qACUarkZX6FyM75CiP4zQAIgVATBcm8NCGkABBzCyASktHCm4LXy6sAsJ0nIVmGD0CCvX1P8XIQmYO4oVkkRsT6UImJ9aDXWAdFqrAMihKkZEAMQKm6V1jKEbDYDOgFCFUEHoeUGgIauXsWn2xp2RMCsg5j5uQWEEgABhzAywdG2wr91NOV6HpwWsr824Hx/H0UBH4LmhyqKmx9Kd9htDtGw2xwi5P4ZIAEQKoIo8xkGhDQAAg5hZAJKPiTxWntBn8l4L4FS6DUoD0iBAQI7RofCzIcqijQfSrdjTwNRx54GIgQOFwAKEKoIOogt0PydYPkfnTeag5nknxxB4pQBxQChDCmgJQAAgMHtpIsjmNxBjLDoVMjB9faDuPYCOgFCFUEHoa3fhkaOsEQF/hqWpx9eBRl+BtgDhJJrpT82NCzIpHpDTR6o0InsUQThQ1CAayci0N5pIMRRFNAJEKoIOgit+Yk0zDop8tHWsKX+u1qkbUAGIFStRskrkQE=";

    private const string SecondHeartbeat =
        "GABsSKnd/////+CBANYxiAwAAAAisAyYDQgVIADqgwvhL1PVVpgpAADgQEJy8iwLkYJlAMpAlB+ilz4/RC99EiRFESVIiiL6IXrp80P00gcIjAGEAlsIgF8AeIoBDXSAsIACOgFCFUEHoW3mAIdkeUeI9ho2j//hC8myBXQChCqCDkKrC0ATh7igkGIuDGRgzwuC2QwABQhVBB3ElputFHRm+1WAtMGYCVTpIQbHgGKAkAYAgIEEJAy0Eiq+dKlXGPlgg2rlKQY0QvI7AZ0AoYqgg9CaD5LDzF7jkXUNYylItIPkkwJAAUIVQQexBVq5E6xdpxO6cjC8fTxayAAyABQgVBF0EFvuC0vQ4eE3fMwGwyAYGoagPwM6AUIVQQehdR6szgn8jK2hE2alv7wJgXQGyACESl3rUfRBmJsBMQCh4iAHm0TIKTCgEyBUEXQQWskfkjj6+8xnd2HdAV7BEDxngAxAqNSWmUkfRKkZ0AkQqgg6CC03NCN0RQ1CxdcwJwKpTAg9LKATIFQRdBBaXZWWOLz+bCV0YQoH5oUQN1cAKECoIuggtkC1eYLVivSGaQ5mj9/HB6F/BnQChCqCDkKr3KIUDgcFMjkn7Hrf7AOS0AroBAhVBB2EVnIRJY5WQdF+XVjAfgkwhLwZIAMQKvUI19sHuXIGxACEisMbnREhUbKAToBQRdBBaCXfUeL4FvQJ3IWd5KueQ1KdAaAAoYqgg9gCLqBNaYze4aMy2K5Bpjik2xkQAxAqXq3+QUTaBmQAQtWizrwSGQ==";

    private const string PeriodicAttributes =
        "GAB4Sgre/////yqBAFs5CAwAAAAisAyoLggFAIDKZ6YDWCMzBQAAHEjIgzxyIVKwDEDhxfDT3xprCf801hL+6WH8Q9TD+IdoXPbL0Ljsl6HQlOs+oSnXfUJTrvuEplz3yfkK85PzFeYn5yvMT85XmJ+cr7A+OV9hfXK+wvrkfIX1eQVl3fMKyrqnQM8kUIGeSaACPZNABXomgcZlvwyNy34ZGpf9MjQu+2WoQM8kUIGeSaACPRNABXomgAr0TAAV6JkAKtAzCVSgZxKoQM8kUIGeSSAgMAYQCmwhAE6pcPQGNBACwuIJOAcIIQACDmGEwi34kdJXx7f3IMrCWgORoG/9hwcJShDLZ0AnQKgi6CC09bXOyNGPuBlfw7D8q0sgwMuAYoBQhhTQEgAAwOCW8l4NHZrp/SGoLASPwvpBYJUBMgChUi/h2n6QrGUAKECoIuggtkATe4IF5zTAYQ7GXGAVAyFqBoAChCqCDmIL9KonWERN977lYDz8oIuQVGcAKECoIuggtoDVcFO6Z/eapgz2248bDWnDBXQChCqCDkJb/zKMHMSFITg27B4/8Amy6QwABQhVBB3EdvsSDnS18iXlqcH+BGL5EDf0GIBQccKfhSGEhhlQDBDKkAJaAgAAGNyetjiCuR3Ee8u5hesoTD+IFSzAQCCkARBwCCMT0PLMVMy4NmvomuLZGT2Fb9ArmellIhiKWMkVLABVG6054kuyYAEo3QUbD0QLNh6ICBYsAAEQAI8QIl7NAFCAUEXQQWwBJ8Wm5I/wvSkZjJZ/VBuSAQ3gAgi1movC9chzGwhpMCLrDOgECFUEHYSWe+kWOpwF7b5sWAh/nzpEzxkQAxAqvpDzRYSEOgNiAELFIxSPiBCHZYAMQKhUqEexH0Q+GWAWENIABDCQXkBDAVohN1/I/CwssbVB5h5MRw/Fu7v7AUDskwEo3S6jyEFdRpGDEMBdQCdAqCLoILTmBeAw69aYkFPD8vhH9SDAyoDygJAGYABjg+BTmYCSmoz816q2LdNDl2Kviw2SQD4eAwAAQBAIA2w7RJCx5O8HwJwEIVYGoHQrLIELVVgCF0IYkwFIACEEQEAiwzc8MgM=";

    private const string DetachedDamagedAttributes =
        "FABsI37b////f6KAAGs7iBKAAAD5QAUkwZyAeYNRAACovJZDgBUgcQEAAXDApDNMoflKdfJcYQQRmpn7x12AuwAVAjAEatQl/JQ63+/J+A1RVOpcAQgxJwCFF8NPf+vGTiTUjZ1IqA68D1EdeB+iZDspQ8l2UobKJ3YElU/sCCqf2BFUPrEj6LoTW9B1J7ag605sQded2IKuO7EFXXdiC7ruxBZ03YktqOvgNU/XwWueZIqnQskUT4W6sRMLdWMnFkq2kzKUbCdlKNlOylCynZShbuzEQt3YiYW6sRMJdWMnEurGTiTUjZ1IqBs7sVA3dmKhbuzEQt3YiYVw0hmAgBVAKKgFAI3+EXRgBkIsZAFYA2Nglts7yKQy/Dp6fCMWXkuNDAoAAACcdcr8EwAAADjrlPlnAAAAcNYp889IAAAolBiBQxYKlR7fsvBajQxgQ6lUBWxACCSyAbFVCwbEVi0YkAAAAMBZp8w/AQAAgLNOmX8GAAAAZ50y/8gUnFQGZIMUIAMQKhWkcM0HQZAFyACESn0Urt9BKk4BMgChUqGJZ3oQMVaADECo1Eu4th+ENRYgAxAqFXIcxwdJKAV0AoQqgg5C26ytDsnmkAzwNcx4IJ0C8twI6AQIVQQdhJbhVrkCW3zcj06Y4H4vKUTOFCADECqV7DDNB5k4BcgAhEqFJp7pQVRdATEAoeKd5rZEiAcpQAYgVCpI4ZoPohwLkAEIlbpH4dKDLMMCZABCpRYzM/kgaKMAGYBQqbgeBT5IayxABiBUKuQ4jg8yIwqQAQiVSlyP4oO8xgJkAEKlQo7j+CA0ogAZgFCpxPUoPgjFKUAGIFQqNPFMDzJnCpABCJVKdpjmQ7j2OwFCFUEHoWXYJq7Akx8uqBNWt1/5AlmNBcgAhEqFHMfxQfRcAaAAoYqgg9hy25+CLrS9vnQ22GI/gBxkfBTQCRCqCDoIbf3FMXL+GTpi17BK/nsySAcpQAYgVCpI4ZoP4gEKAAUIVQQdxBbQW2xK8r8ut5PBTPfTbyAfpAAZgFCpIIVrPoiTK0AGIFTqoXDJDzL5CtAECFUEHUS2k9ofJwiX4qQTG9XPRILkvgJiAELFIeYGiRDzUwAoQKgi6CC2gAyFUwrsNbJsDPa7/8INiTMFyACESiU7TPNBhjoBoAChiqCD2ALS2E2pkNaSoTJYKAFnHQM=";

    private const string TrailingAttributesHeartbeat1 =
        "HAC06Zpz+///+xiAANcpCAYAAACybRl4DggBIADqclOXqYp4WwZQqELQaOLMPtHEmX2ybd8RZdu+I4omzuwTTZzZ5wd34M8P7sCfcxW3QucqboWAgBVAKKgFwDDBGL4bBkLkJQOdAKGKoIPQbrP/QdfaYTYwDfMg4CSGfD8E6gBCCAAAAwnxy49d+nLQTALZUwx0AoQqgg5C69uvlRRs5Is1Jyyev9YJ4t8Y6AQIVQQdhJZ7UxY6Swbd12uYeUFjISSNMtAJEKoIOggt958pdJYM6mzasPiCskXInGIAFCBUEXQQW8BFuikpHBynVQaj2pc0h/QcBkABQhVBB7EFhJWbknjF3WplMC8ApCCII1wAFCBUEXQQW8AXsSkNQlxyTwYjJHCvhgRbBjoBQhVBB6GVTGiJ4/dPzGUXBj8Ayw3MygCEqk3RgiYy";

    private const string TrailingAttributesHeartbeat2 =
        "HADo6aPz/2///x2AAI4tiAsAAACybRl4DggBIADqXiWXqYp4WwZQqELQQM7MPoGcmX3K894Rlee9IwrkzOwTyJnZJ1F84E+i+MCfvZS3Qnspb4WAwBhAKLCFAKj0Qkt4w0AJiLxkoBMgVBF0ENpt9j/o9jqUUqZhygNsw5Dvh0AdQAgBAGAgIX65LiL9DZ5aIA6XARmAUKnsUTg9iJ5lABQgVBF0EFtAuMIpqYwcv0wGO9XvkYZsSwZAAUIVQQexBWQum9LPxKXkZLAr/WQDCJ1lABQgVBF0EFvAGr0pZXZc4TYGy9r/8YM0GgZkAEKlguQX8yCQhgEZgFCpIPnFPMg1QkAGIFTqSMDXD1KNEJABCJU6EvD1g1gjBGQAQqWOBHz9II6GARmAUKkg+cU8yJtloBMgVBF0EFruo1PohBtEArRh2wV8bRBqhIAMQKjUkYCvH6SzIFAKEAIAArQ12CKgBQGUeADg8aEAwONDyKNhQAYgVCpIfjEPEmkYkAEIlQqSX8yDTCMEZABCpY4EfP0gDYkBGYBQqTMKRz9IYWAAFCBUEXQQW8DmtCmVoBy0VQY7LmBdA2ZlAELVPqVBExk=";

    private const string SparseMaximumHeartbeat1 =
        "EAAUWUjv/////7mBAOMhiAAAAAABsa4MCBUgAOoCwAsAnqhKTKxRmEKBRps4QY02cYICrUEQBVqDIGq0iRPUaBMn6Pj9Z8/x+88eAw==";

    private const string SparseMaximumHeartbeat2 =
        "EACcWWbv////f7aBANskiAIAAAABsc4PCAEgAOoC4AsAn6hql8gUAABwICEP4TuGxMQahSkUCE6IExScECeoMRMEUWMmCKLghDhBwQlxgp63f/Y8b//sAQIdAKEg14EMJAI6AUIVQQeh5Wb5hK7gwO9BGzZLwM8DkUMEdAKEKoIOQsudkArdfIIPoTZsdsB6E2KJCOgECFUEHYSWe88SurQDtQVtmBtBQw0D";

    private const string PteranodonHeartbeat1 =
        "EACYVgbm/////9OBAHUjiAEAAAABsf4OCAEgAOqK14t4nahKTKxRuEIBwHUOmkHrHDSDdqyxDu1YYx1a56AZtM5BM+ilh03PSw+bnvmOiUPzHROHgMAYQCiwhQCITqYpUNVAEgjNI6ATIFQRdBBabqNR6BIVTHq0YfsFPkYM";

    private const string PteranodonHeartbeat2 =
        "EAAMVyPm////f7qBAO8kCAIAAAABsR4SCAEgAOoSrwvxmqhq58wUAABwICGT4qmGxMQahSsUALwEoBl0CUAzaIoL69AUF9ahSwCaQZcANIN6Ktj09FSw6dm3qji0b1VxCFFSBIAChCqCDmLL3bsIugr+ct7RYKYCOqmQLEhAJ0CoIuggtNxWmNCJKhjOaMNmC9ZMIaORgE6AUEXQQWi5O0OhM1hQS9OGbReEVDE=";

    private const string SparseMaximumAttributes =
        "EADUZV3y/////2eAAN0nCAIAAAABsV4sCAEAgMoHCANYaDIFAAAcSEjgopshMbFG4cXw0956GZcG9TIuDRqSp0o0JE+VaGyWENHYLCGicvDIQOXgkYHKwSMDlYNHBmpFaxHUitYiqBWtRVArWougVrSWQK1oLYFa0VoCtaK1BHoVYvPzKsTmpyPLnlBHlj2hjix7Qh1Z9oTGZgkRjc0SIurIsifUkWVPqCPLflBHlv2gjiz7QR1Z9oM6suwJdWTZE+rIsifUkWVPCAhYAYSCWgAeTCN+LGogpBUR0AkQqgg6CC139yl0aQfiItowCoJ8GggSIqATIFQRdBBabu1N6CIK5MmxYZn80EEM";
}
