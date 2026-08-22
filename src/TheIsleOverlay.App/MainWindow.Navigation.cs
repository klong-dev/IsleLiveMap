using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private WorldLocation? _routeTarget;
    private MapPoint? _routeTargetMapLocation;

    private void RouteCoordinateInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RouteCoordinateHint.Visibility = string.IsNullOrWhiteSpace(RouteCoordinateInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_routeTarget is null)
        {
            RouteInputStatus.Text = "Hỗ trợ chuỗi tọa độ copy trực tiếp từ game.";
            RouteInputStatus.Foreground = BrushFrom("#6F8C84");
        }
    }

    private void RouteCoordinateInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        StartRoute();
        e.Handled = true;
    }

    private void StartRouteButton_Click(object sender, RoutedEventArgs e) => StartRoute();

    private void StopRouteButton_Click(object sender, RoutedEventArgs e) => StopRoute();

    private void StartRoute()
    {
        if (!NavigationTargetParser.TryParse(RouteCoordinateInput.Text, out var target))
        {
            RouteInputStatus.Text = "Không đọc được tọa độ. Dán đủ X, Y, Z như mẫu.";
            RouteInputStatus.Foreground = ErrorBrush;
            RouteCoordinateInput.Focus();
            RouteCoordinateInput.SelectAll();
            return;
        }

        _routeTarget = target;
        _routeTargetMapLocation = GatewayMapProjection.Project(target);
        StopRouteButton.IsEnabled = true;
        StartRouteButton.Content = "CẬP NHẬT";
        RouteInputStatus.Text = $"Đang chỉ đường · X {target.X:0.###} / Y {target.Y:0.###}";
        RouteInputStatus.Foreground = BrushFrom("#E7B74E");
        PositionMap();
    }

    private void StopRoute()
    {
        _routeTarget = null;
        _routeTargetMapLocation = null;
        StopRouteButton.IsEnabled = false;
        StartRouteButton.Content = "CHỈ ĐƯỜNG";
        RouteInputStatus.Text = "Đã tắt chỉ đường. Tọa độ vẫn được giữ để bật lại.";
        RouteInputStatus.Foreground = BrushFrom("#6F8C84");
        HideRouteVisuals();
    }

    private void PositionRoute(
        MapPoint? playerPoint,
        double imageLeft,
        double imageTop,
        double imageWidth,
        double imageHeight)
    {
        if (_routeTarget is null || _routeTargetMapLocation is null || playerPoint is null)
        {
            HideRouteVisuals();
            return;
        }

        var startX = imageLeft + playerPoint.Value.Left * imageWidth;
        var startY = imageTop + playerPoint.Value.Top * imageHeight;
        var targetX = imageLeft + _routeTargetMapLocation.Value.Left * imageWidth;
        var targetY = imageTop + _routeTargetMapLocation.Value.Top * imageHeight;

        RouteLine.X1 = startX;
        RouteLine.Y1 = startY;
        RouteLine.X2 = targetX;
        RouteLine.Y2 = targetY;
        RouteLine.Visibility = Visibility.Visible;

        Canvas.SetLeft(RouteTargetMarker, targetX - RouteTargetMarker.Width / 2d);
        Canvas.SetTop(RouteTargetMarker, targetY - RouteTargetMarker.Height / 2d);
        RouteTargetMarker.Visibility = Visibility.Visible;
        RouteDistanceBadge.Visibility = Visibility.Visible;
        RouteDistanceLabel.Text = FormatRouteDistance(_location, _routeTarget);
    }

    private void HideRouteVisuals()
    {
        RouteLine.Visibility = Visibility.Collapsed;
        RouteTargetMarker.Visibility = Visibility.Collapsed;
        RouteDistanceBadge.Visibility = Visibility.Collapsed;
    }

    private static string FormatRouteDistance(WorldLocation? current, WorldLocation target)
    {
        if (current is null)
        {
            return "ĐÍCH · ĐANG CHỜ VỊ TRÍ";
        }

        var deltaX = target.X - current.X;
        var deltaY = target.Y - current.Y;
        var unrealUnits = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return unrealUnits < 100_000d
            ? $"ĐÍCH · {unrealUnits / 100d:0} M"
            : $"ĐÍCH · {unrealUnits / 100_000d:0.00} KM";
    }
}
