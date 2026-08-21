using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotMapCalibrationTests
{
    [Fact]
    public void Project_HandlesIndependentlyInvertedAxes()
    {
        var calibration = Calibration(
            new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 1, V = 0 },
            new IslePilotMapCalibrationPointDto { WorldX = 100_000, WorldY = -100_000, U = 0, V = 1 });

        var point = calibration.Project(25_000, -50_000);

        Assert.Equal(0.75, point.Left, precision: 8);
        Assert.Equal(0.5, point.Top, precision: 8);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 180)]
    [InlineData(180, 270)]
    [InlineData(270, 0)]
    public void ProjectHeading_MapsWorldYawToWpfNeedleAngle(double yaw, double expected)
    {
        var calibration = Calibration(
            new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 0, V = 0 },
            new IslePilotMapCalibrationPointDto { WorldX = 100_000, WorldY = 100_000, U = 1, V = 1 });

        var heading = calibration.ProjectHeading(50_000, 50_000, yaw);

        Assert.Equal(expected, heading, precision: 8);
    }

    [Fact]
    public void Constructor_RejectsDegenerateCalibration()
    {
        var dto = new IslePilotMapCalibrationDto
        {
            A = new IslePilotMapCalibrationPointDto { WorldX = 1, WorldY = 0, U = 0, V = 0 },
            B = new IslePilotMapCalibrationPointDto { WorldX = 1, WorldY = 100, U = 1, V = 1 }
        };

        Assert.Throws<InvalidDataException>(() => new IslePilotMapCalibration(dto));
    }

    private static IslePilotMapCalibration Calibration(
        IslePilotMapCalibrationPointDto a,
        IslePilotMapCalibrationPointDto b) => new(new IslePilotMapCalibrationDto { A = a, B = b });
}
