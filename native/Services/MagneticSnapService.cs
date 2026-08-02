using System.Windows;

namespace YitDesktopFold.Native.Services;

/// <summary>
/// Provides independent position snapping for moved organizers and a restrained
/// release-time size snapping against a nearby organizer above, left, or right.
/// </summary>
public static class MagneticSnapService
{
    public const double DefaultGap = 10;
    public const double DefaultThreshold = 18;
    public const double DefaultResizeThreshold = 10;
    public const double DefaultNeighborGap = 36;
    public const double DefaultNeighborAlignmentThreshold = 24;

    private const double ComparisonTolerance = 0.001;

    public static MagneticSnapResult Snap(
        Rect movingBounds,
        IEnumerable<Rect> otherBounds,
        double gap = DefaultGap,
        double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(otherBounds);
        ValidateBounds(movingBounds, nameof(movingBounds));
        ValidateNonNegative(gap, nameof(gap), "The magnetic gap must be finite and non-negative.");
        ValidateNonNegative(threshold, nameof(threshold), "The snap threshold must be finite and non-negative.");

        AxisCandidate? bestX = null;
        AxisCandidate? bestY = null;
        foreach (var target in otherBounds.Where(IsUsable))
        {
            if (SpansAreNear(movingBounds.Top, movingBounds.Bottom, target.Top, target.Bottom, threshold + gap))
            {
                ConsiderAxis(ref bestX, target.Left, movingBounds.Left, threshold);
                ConsiderAxis(ref bestX, target.Right - movingBounds.Width, movingBounds.Left, threshold);
                ConsiderAxis(ref bestX, target.Left - gap - movingBounds.Width, movingBounds.Left, threshold);
                ConsiderAxis(ref bestX, target.Right + gap, movingBounds.Left, threshold);
            }

            if (SpansAreNear(movingBounds.Left, movingBounds.Right, target.Left, target.Right, threshold + gap))
            {
                ConsiderAxis(ref bestY, target.Top, movingBounds.Top, threshold);
                ConsiderAxis(ref bestY, target.Bottom - movingBounds.Height, movingBounds.Top, threshold);
                ConsiderAxis(ref bestY, target.Top - gap - movingBounds.Height, movingBounds.Top, threshold);
                ConsiderAxis(ref bestY, target.Bottom + gap, movingBounds.Top, threshold);
            }
        }

        var snappedBounds = new Rect(
            bestX?.Position ?? movingBounds.Left,
            bestY?.Position ?? movingBounds.Top,
            movingBounds.Width,
            movingBounds.Height);
        return new MagneticSnapResult(snappedBounds, bestX is not null, bestY is not null);
    }

    public static SizeSnapResult SnapSizeToNearbyNeighbor(
        Rect resizingBounds,
        IEnumerable<Rect> otherBounds,
        double threshold = DefaultResizeThreshold,
        double maximumNeighborGap = DefaultNeighborGap,
        double alignmentThreshold = DefaultNeighborAlignmentThreshold)
    {
        ArgumentNullException.ThrowIfNull(otherBounds);
        ValidateBounds(resizingBounds, nameof(resizingBounds));
        ValidateNonNegative(threshold, nameof(threshold), "The size threshold must be finite and non-negative.");
        ValidateNonNegative(maximumNeighborGap, nameof(maximumNeighborGap), "The neighbor gap must be finite and non-negative.");
        ValidateNonNegative(alignmentThreshold, nameof(alignmentThreshold), "The alignment threshold must be finite and non-negative.");

        var target = otherBounds
            .Where(IsUsable)
            .Select(target => CreateNeighborCandidate(
                resizingBounds,
                target,
                threshold,
                maximumNeighborGap,
                alignmentThreshold))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .OrderBy(candidate => candidate.Score)
            .Select(candidate => candidate.Bounds)
            .FirstOrDefault();

        if (!IsUsable(target))
        {
            return new SizeSnapResult(resizingBounds.Size, false, false);
        }

        var snappedWidth = Math.Abs(resizingBounds.Width - target.Width) <= threshold + ComparisonTolerance;
        var snappedHeight = Math.Abs(resizingBounds.Height - target.Height) <= threshold + ComparisonTolerance;
        return new SizeSnapResult(
            new Size(
                snappedWidth ? target.Width : resizingBounds.Width,
                snappedHeight ? target.Height : resizingBounds.Height),
            snappedWidth,
            snappedHeight);
    }

    private static NeighborCandidate? CreateNeighborCandidate(
        Rect resizingBounds,
        Rect target,
        double overlapTolerance,
        double maximumNeighborGap,
        double alignmentThreshold)
    {
        NeighborCandidate? best = null;

        Consider(
            resizingBounds.Top - target.Bottom,
            Math.Abs(resizingBounds.Left - target.Left));
        Consider(
            resizingBounds.Left - target.Right,
            Math.Abs(resizingBounds.Top - target.Top));
        Consider(
            target.Left - resizingBounds.Right,
            Math.Abs(resizingBounds.Top - target.Top));

        return best;

        void Consider(double gap, double alignmentDistance)
        {
            if (gap < -overlapTolerance - ComparisonTolerance ||
                gap > maximumNeighborGap + ComparisonTolerance ||
                alignmentDistance > alignmentThreshold + ComparisonTolerance)
            {
                return;
            }

            var candidate = new NeighborCandidate(target, Math.Abs(gap) + alignmentDistance * 0.25);
            if (best is null || candidate.Score < best.Value.Score - ComparisonTolerance)
            {
                best = candidate;
            }
        }
    }

    private static void ConsiderAxis(
        ref AxisCandidate? best,
        double candidatePosition,
        double currentPosition,
        double threshold)
    {
        var distance = Math.Abs(candidatePosition - currentPosition);
        if (distance > threshold + ComparisonTolerance ||
            best is AxisCandidate currentBest && distance >= currentBest.Distance - ComparisonTolerance)
        {
            return;
        }

        best = new AxisCandidate(candidatePosition, distance);
    }

    private static bool SpansAreNear(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd,
        double threshold) =>
        firstEnd >= secondStart - threshold && firstStart <= secondEnd + threshold;

    private static void ValidateBounds(Rect bounds, string parameterName)
    {
        if (!IsUsable(bounds))
        {
            throw new ArgumentException("Bounds must be finite and have a positive size.", parameterName);
        }
    }

    private static void ValidateNonNegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, message);
        }
    }

    private static bool IsUsable(Rect bounds) =>
        !bounds.IsEmpty &&
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height) &&
        bounds.Width > 0 &&
        bounds.Height > 0;

    private readonly record struct AxisCandidate(double Position, double Distance);
    private readonly record struct NeighborCandidate(Rect Bounds, double Score);
}

public readonly record struct MagneticSnapResult(Rect Bounds, bool SnappedX, bool SnappedY)
{
    public Point Position => Bounds.Location;
}

public readonly record struct SizeSnapResult(Size Size, bool SnappedWidth, bool SnappedHeight)
{
    public bool Snapped => SnappedWidth || SnappedHeight;
}
