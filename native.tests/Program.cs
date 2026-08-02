using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using YitDesktopFold.Native.Controls;
using YitDesktopFold.Native.Models;
using YitDesktopFold.Native.Services;

namespace YitDesktopFold.Native.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "YitDesktopFold.Tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("YITDESKTOPFOLD_DATA_DIR", testRoot);

        try
        {
            TestShortcutCopyAndSafeRemoval(testRoot);
            TestStateRoundTripAndRecovery();
            TestCombinedSettingsDraftIsolation();
            TestFixedLayoutGeometry();
            TestMagneticSnapGeometry();
            Console.WriteLine("PASS: shortcut import/removal");
            Console.WriteLine("PASS: multi-folder state round-trip/v1 migration/recovery");
            Console.WriteLine("PASS: global appearance draft isolation and normalization");
            Console.WriteLine("PASS: combined global/current-organizer settings draft isolation");
            Console.WriteLine("PASS: fixed large/medium/small/list layout geometry and reflow");
            Console.WriteLine("PASS: position and nearby-neighbor size snap geometry");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
            catch
            {
                // The unique temporary directory is safe to leave for diagnosis if locked.
            }
        }
    }

    private static void TestShortcutCopyAndSafeRemoval(string testRoot)
    {
        var sourceDirectory = Path.Combine(testRoot, "Source");
        Directory.CreateDirectory(sourceDirectory);
        var sourceShortcut = Path.Combine(sourceDirectory, "OpenAI.url");
        File.WriteAllText(sourceShortcut, "[InternetShortcut]\nURL=https://openai.com/\n");

        var service = new ShortcutService();
        var item = service.Import(sourceShortcut, moveDesktopShortcut: false, accentIndex: 2);

        Assert(File.Exists(sourceShortcut), "Copy import must retain the source shortcut.");
        Assert(File.Exists(item.LaunchPath), "Managed shortcut copy was not created.");
        Assert(item.IsManaged && !item.WasMovedFromDesktop, "Managed copy flags are invalid.");

        service.RemoveAndRecover(item);
        Assert(File.Exists(sourceShortcut), "Safe removal deleted the original shortcut.");
        Assert(!File.Exists(item.LaunchPath), "Managed copy was not removed.");
    }

    private static void TestStateRoundTripAndRecovery()
    {
        var store = new StateStore();
        var firstFolderId = Guid.NewGuid();
        var secondFolderId = Guid.NewGuid();
        var state = new OrganizerAppState
        {
            StartWithWindows = true,
            Appearance = new OrganizerAppearanceState
            {
                GlassEnabled = true,
                BackgroundOpacity = 0.71,
                BackgroundTone = OrganizerBackgroundTone.Light,
                MagneticSnapEnabled = true,
            },
            Folders =
            [
                new OrganizerFolderState
                {
                    Id = firstFolderId,
                    Name = "常用",
                    ShowName = true,
                    ShowIconNames = true,
                    IconsOnly = true,
                    IconLayoutMode = OrganizerIconLayoutMode.Small,
                    Left = 120,
                    Top = 90,
                    Width = 444,
                    Height = 340,
                    Shortcuts =
                    [
                        new ShortcutItem
                        {
                            Name = "测试",
                            LaunchPath = "C:\\Windows\\explorer.exe",
                            AccentIndex = 3,
                        },
                    ],
                },
                new OrganizerFolderState
                {
                    Id = secondFolderId,
                    Name = "创作",
                    ShowName = false,
                    ShowIconNames = false,
                    IconLayoutMode = OrganizerIconLayoutMode.List,
                    Left = 640,
                    Top = 130,
                    Width = 360,
                    Height = 260,
                },
            ],
        };

        store.Save(state);
        var loaded = store.Load();
        Assert(loaded.Folders.Count == 2, "Folder count did not round-trip.");
        Assert(loaded.Folders[0].Id == firstFolderId && loaded.Folders[1].Id == secondFolderId,
            "Stable folder IDs did not round-trip.");
        Assert(loaded.Folders[0].Shortcuts.Count == 1, "Folder shortcut count did not round-trip.");
        Assert(loaded.Folders[0].ShowName && !loaded.Folders[1].ShowName,
            "Independent folder name visibility did not round-trip.");
        Assert(loaded.Folders[0].ShowIconNames && !loaded.Folders[1].ShowIconNames,
            "Independent icon-name visibility did not round-trip.");
        Assert(loaded.Folders[0].IconsOnly && !loaded.Folders[1].IconsOnly,
            "Independent icons-only visibility did not round-trip.");
        Assert(loaded.Folders[0].IconLayoutMode is OrganizerIconLayoutMode.Small &&
               loaded.Folders[1].IconLayoutMode is OrganizerIconLayoutMode.List,
            "Independent fixed icon layout modes did not round-trip.");
        Assert(loaded.Folders[1].Left == 640 && loaded.Folders[1].Width == 360,
            "Independent folder placement did not round-trip.");
        Assert(loaded.Appearance.GlassEnabled &&
               Math.Abs(loaded.Appearance.BackgroundOpacity - 0.71) < 0.001 &&
               loaded.Appearance.BackgroundTone is OrganizerBackgroundTone.Light &&
               loaded.Appearance.MagneticSnapEnabled,
            "Appearance and magnetic-snap settings did not round-trip.");
        var isolatedDraft = loaded.Appearance.Copy();
        isolatedDraft.BackgroundOpacity = 0;
        isolatedDraft.BackgroundTone = OrganizerBackgroundTone.Dark;
        isolatedDraft.MagneticSnapEnabled = false;
        Assert(Math.Abs(loaded.Appearance.BackgroundOpacity - 0.71) < 0.001 &&
               loaded.Appearance.BackgroundTone is OrganizerBackgroundTone.Light &&
               loaded.Appearance.MagneticSnapEnabled,
            "Editing an appearance draft mutated the committed appearance.");
        Assert(!File.Exists(AppPaths.StateFile + ".tmp"), "Atomic save left a temporary file behind.");

        File.WriteAllText(
            AppPaths.StateFile,
            """
            {
              "SchemaVersion": 2,
              "Appearance": {
                "GlassEnabled": true,
                "BackgroundOpacity": 2,
                "GlassStrength": -4,
                "BackgroundTone": 999,
                "MagneticSnapEnabled": false
              },
              "Folders": [
                {
                  "Name": "边界测试",
                  "IconLayoutMode": 999,
                  "Width": 10,
                  "Height": 20
                }
              ]
            }
            """);
        var normalized = store.Load();
        Assert(Math.Abs(normalized.Appearance.BackgroundOpacity - 1) < 0.001 &&
               normalized.Appearance.BackgroundTone is OrganizerBackgroundTone.Dark,
            "Out-of-range appearance values were not normalized.");
        Assert(normalized.Folders[0].Width == 94 && normalized.Folders[0].Height == 120,
            "Organizer dimensions were not normalized to one Medium icon and the minimum height.");
        Assert(normalized.Folders[0].IconLayoutMode is OrganizerIconLayoutMode.Medium,
            "An invalid icon layout mode was not normalized.");

        normalized.Appearance.BackgroundOpacity = 0;
        normalized.Appearance.BackgroundTone = OrganizerBackgroundTone.Light;
        store.Save(normalized);
        var boundary = store.Load();
        Assert(Math.Abs(boundary.Appearance.BackgroundOpacity) < 0.001 &&
               boundary.Appearance.BackgroundTone is OrganizerBackgroundTone.Light,
            "Fully transparent light appearance did not round-trip.");

        File.WriteAllText(
            AppPaths.StateFile,
            """
            {
              "SchemaVersion": 1,
              "Left": 72,
              "Top": 48,
              "Width": 390,
              "Height": 280,
              "StartWithWindows": true,
              "Shortcuts": [
                {
                  "Name": "旧版项目",
                  "LaunchPath": "C:\\Windows\\explorer.exe"
                }
              ]
            }
            """);
        File.Delete(AppPaths.BackupStateFile);
        var migrated = store.Load();
        Assert(migrated.SchemaVersion == 2 && migrated.Folders.Count == 1,
            "Legacy state did not migrate to one schema-v2 folder.");
        Assert(migrated.Folders[0].Left == 72 && migrated.Folders[0].Shortcuts.Count == 1,
            "Legacy placement or shortcuts were not preserved.");
        Assert(!migrated.Folders[0].ShowName &&
               !migrated.Folders[0].ShowIconNames &&
               !migrated.Folders[0].IconsOnly &&
               migrated.Folders[0].IconLayoutMode is OrganizerIconLayoutMode.Medium &&
               migrated.StartWithWindows,
            "Legacy defaults were not migrated correctly.");
        Assert(migrated.Appearance.GlassEnabled && migrated.Appearance.MagneticSnapEnabled,
            "Legacy state did not receive safe appearance defaults.");

        store.Save(state);
        state.Folders[0].Name = "更新后的常用";
        store.Save(state);
        File.WriteAllText(AppPaths.StateFile, "{ invalid json");
        var restoredFromBackup = store.Load();
        Assert(restoredFromBackup.Folders.Count == 2, "Valid backup was not recovered.");
        store.Save(restoredFromBackup);
        Assert(store.Load().Folders.Count == 2, "Saving after backup recovery corrupted state.");

        var orphan = Path.Combine(AppPaths.ManagedShortcutsDirectory, "Recovered.url");
        File.WriteAllText(orphan, "[InternetShortcut]\nURL=https://example.com/\n");
        File.WriteAllText(AppPaths.StateFile, "{ invalid json");
        File.WriteAllText(AppPaths.BackupStateFile, "{ invalid json");
        var recovered = store.Load();
        Assert(recovered.Folders.Count == 1 &&
               !recovered.Folders[0].ShowIconNames &&
               !recovered.Folders[0].IconsOnly &&
               recovered.Folders[0].Shortcuts.Any(item => item.LaunchPath == orphan),
            "Corrupt state did not recover managed shortcuts into a default folder.");
    }

    private static void TestCombinedSettingsDraftIsolation()
    {
        var draft = new OrganizerSettingsDraft
        {
            Appearance = new OrganizerAppearanceState
            {
                GlassEnabled = true,
                BackgroundOpacity = 0.46,
                BackgroundTone = OrganizerBackgroundTone.Light,
                MagneticSnapEnabled = true,
            },
            ShowFolderName = true,
            ShowIconNames = false,
            IconsOnly = true,
            IconLayoutMode = OrganizerIconLayoutMode.List,
        };
        var copy = draft.Copy();
        copy.Appearance.BackgroundOpacity = 0.9;

        Assert(Math.Abs(draft.Appearance.BackgroundOpacity - 0.46) < 0.001,
            "Copying a combined settings draft shared its appearance instance.");
        Assert(copy.ShowFolderName &&
               !copy.ShowIconNames &&
               copy.IconsOnly &&
               copy.IconLayoutMode is OrganizerIconLayoutMode.List,
            "Copying a combined settings draft lost current-organizer display flags.");
    }

    private static void TestFixedLayoutGeometry()
    {
        AssertClose(OrganizerLayoutMetrics.GetMinimumWidth(OrganizerIconLayoutMode.Large), 114,
            "Large minimum width does not fit exactly one tile and its padding.");
        AssertClose(OrganizerLayoutMetrics.GetMinimumWidth(OrganizerIconLayoutMode.Medium), 94,
            "Medium minimum width does not fit exactly one tile and its padding.");
        AssertClose(OrganizerLayoutMetrics.GetMinimumWidth(OrganizerIconLayoutMode.Small), 76,
            "Small minimum width does not fit exactly one tile and its padding.");
        AssertClose(OrganizerLayoutMetrics.GetMinimumWidth(OrganizerIconLayoutMode.List), 76,
            "List minimum width does not preserve one usable icon row.");

        var panel = new AdaptiveIconPanel
        {
            ShowLabels = true,
            LayoutMode = OrganizerIconLayoutMode.Large,
        };
        for (var index = 0; index < 12; index++)
        {
            panel.Children.Add(new Button());
        }

        ArrangePanel(panel, 444, 340);
        AssertChildrenHaveSize(panel, 86, 98);
        Assert(GetDistinctColumnCount(panel) == 4, "Large layout did not form four fixed columns at 444 DIP.");
        AssertClose(VisualTreeHelper.GetOffset(panel.Children[0]).X, 14,
            "Large layout did not begin at its fixed left padding.");
        Assert(panel.AreLabelsVisible, "Explicit icon-name visibility was not honored.");

        ArrangePanel(panel, 360, 340);
        AssertChildrenHaveSize(panel, 86, 98);
        Assert(GetDistinctColumnCount(panel) == 3,
            "Resizing should reflow large icons without scaling them.");

        panel.LayoutMode = OrganizerIconLayoutMode.Medium;
        ArrangePanel(panel, 360, 340);
        AssertChildrenHaveSize(panel, 70, 84);
        AssertClose(VisualTreeHelper.GetOffset(panel.Children[0]).X, 12,
            "Medium layout was centered instead of left aligned.");
        panel.Measure(new Size(360, double.PositiveInfinity));
        AssertClose(panel.DesiredSize.Height, 296,
            "Medium layout did not expose its full vertical scroll extent.");

        panel.LayoutMode = OrganizerIconLayoutMode.Small;
        ArrangePanel(panel, 360, 340);
        AssertChildrenHaveSize(panel, 52, 64);

        panel.ShowLabels = false;
        panel.LayoutMode = OrganizerIconLayoutMode.List;
        panel.Measure(new Size(360, double.PositiveInfinity));
        AssertClose(panel.DesiredSize.Height, 570,
            "List layout did not expose its full vertical scroll extent.");
        ArrangePanel(panel, 360, 600);
        AssertChildrenHaveSize(panel, 336, 40);
        Assert(GetDistinctColumnCount(panel) == 1, "List layout must stay in one column.");
        Assert(panel.AreLabelsVisible, "List layout must always expose shortcut names.");

        panel.SuppressLabels = true;
        ArrangePanel(panel, 360, 600);
        Assert(!panel.AreLabelsVisible, "Icons-only mode did not suppress list labels.");
    }

    private static void ArrangePanel(AdaptiveIconPanel panel, double width, double height)
    {
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
    }

    private static int GetDistinctColumnCount(AdaptiveIconPanel panel) =>
        panel.Children.Cast<UIElement>()
            .Select(child => Math.Round(VisualTreeHelper.GetOffset(child).X, 3))
            .Distinct()
            .Count();

    private static void AssertChildrenHaveSize(AdaptiveIconPanel panel, double width, double height)
    {
        foreach (UIElement child in panel.Children)
        {
            AssertClose(child.RenderSize.Width, width, "A fixed layout changed tile width.");
            AssertClose(child.RenderSize.Height, height, "A fixed layout changed tile height.");
        }
    }

    private static void TestMagneticSnapGeometry()
    {
        var target = new Rect(400, 100, 120, 80);

        var snappedLeft = MagneticSnapService.Snap(
            new Rect(277, 103, 100, 80),
            [target]);
        AssertClose(snappedLeft.Bounds.Left, 290, "Left-side movement did not preserve the 10px gap.");
        AssertClose(snappedLeft.Bounds.Top, 100, "Movement did not align the nearby top edge.");
        AssertClose(snappedLeft.Bounds.Width, 100, "Position snapping changed width.");
        AssertClose(snappedLeft.Bounds.Height, 80, "Position snapping changed height.");
        Assert(snappedLeft.SnappedX && snappedLeft.SnappedY,
            "Position snapping did not report both aligned axes.");

        var snappedBelow = MagneticSnapService.Snap(
            new Rect(407, 203, 120, 80),
            [target]);
        AssertClose(snappedBelow.Bounds.Left, 400, "Below movement did not align left edges.");
        AssertClose(snappedBelow.Bounds.Top, 190, "Below movement did not preserve the 10px gap.");

        var rightEdges = MagneticSnapService.Snap(
            new Rect(382, 207, 145, 70),
            [target]);
        AssertClose(rightEdges.Bounds.Left, 375, "Movement did not align nearby right edges.");

        var outsideThreshold = MagneticSnapService.Snap(
            new Rect(271, 300, 100, 80),
            [target]);
        AssertClose(outsideThreshold.Bounds.Left, 271, "A 19px displacement must not snap.");
        Assert(!outsideThreshold.SnappedX, "Position snapping engaged beyond its threshold.");

        var distant = new Rect(1000, 900, 120, 80);
        var unchanged = MagneticSnapService.Snap(
            new Rect(100, 100, 100, 80),
            [distant]);
        AssertClose(unchanged.Bounds.Left, 100, "A distant block caused horizontal attraction.");
        AssertClose(unchanged.Bounds.Top, 100, "A distant block caused vertical attraction.");
        Assert(!unchanged.SnappedX && !unchanged.SnappedY, "A distant block must not engage snapping.");

        var unclamped = MagneticSnapService.Snap(
            new Rect(-123, 100, 100, 80),
            [new Rect(0, 100, 120, 80)]);
        AssertClose(unclamped.Bounds.Left, -110, "Position snapping unexpectedly clamped to a work area.");
        AssertClose(unclamped.Bounds.Width, 100, "Unclamped position snapping changed width.");

        var lowerBounds = new Rect(400, 190, 116, 147);
        var widthSnap = MagneticSnapService.SnapSizeToNearbyNeighbor(lowerBounds, [target]);
        Assert(widthSnap.SnappedWidth && !widthSnap.SnappedHeight,
            "Width-only release did not preserve independent axis matching.");
        AssertClose(widthSnap.Size.Width, 120, "Width did not match the upper neighbor.");
        AssertClose(widthSnap.Size.Height, 147, "Width-only release changed height.");

        var heightSnap = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 190, 145, 87),
            [target]);
        Assert(!heightSnap.SnappedWidth && heightSnap.SnappedHeight,
            "Height-only release did not preserve independent axis matching.");
        AssertClose(heightSnap.Size.Width, 145, "Height-only release changed width.");
        AssertClose(heightSnap.Size.Height, 80, "Height did not match the upper neighbor.");

        var bothSnap = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 190, 110, 90),
            [target]);
        Assert(bothSnap.SnappedWidth && bothSnap.SnappedHeight,
            "Both dimensions did not snap at the release threshold.");
        AssertClose(bothSnap.Size.Width, 120, "Combined release did not match width.");
        AssertClose(bothSnap.Size.Height, 80, "Combined release did not match height.");

        var outsideSizeThreshold = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 190, 109.5, 90.5),
            [target]);
        Assert(!outsideSizeThreshold.Snapped, "Size snapping engaged beyond the release threshold.");
        AssertClose(outsideSizeThreshold.Size.Width, 109.5, "An unsnapped release changed width.");
        AssertClose(outsideSizeThreshold.Size.Height, 90.5, "An unsnapped release changed height.");

        var lowerTarget = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 100, 116, 163),
            [new Rect(400, 190, 120, 80)]);
        Assert(!lowerTarget.Snapped, "A lower organizer incorrectly acted as the upper width target.");

        var horizontallyMisaligned = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(430, 190, 116, 163),
            [target]);
        Assert(!horizontallyMisaligned.Snapped,
            "A horizontally misaligned upper organizer incorrectly attracted width.");

        var leftNeighbor = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 100, 116, 87),
            [new Rect(270, 103, 120, 80)]);
        Assert(leftNeighbor.SnappedWidth && leftNeighbor.SnappedHeight,
            "A nearby left organizer did not provide width and height release snapping.");
        AssertClose(leftNeighbor.Size.Width, 120, "Left-neighbor release did not match width.");
        AssertClose(leftNeighbor.Size.Height, 80, "Left-neighbor release did not match height.");

        var rightNeighbor = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 100, 116, 87),
            [new Rect(526, 102, 120, 80)]);
        Assert(rightNeighbor.SnappedWidth && rightNeighbor.SnappedHeight,
            "A nearby right organizer did not provide width and height release snapping.");
        AssertClose(rightNeighbor.Size.Width, 120, "Right-neighbor release did not match width.");
        AssertClose(rightNeighbor.Size.Height, 80, "Right-neighbor release did not match height.");

        var rightNeighborWithSmallOverlap = MagneticSnapService.SnapSizeToNearbyNeighbor(
            new Rect(400, 100, 130, 87),
            [new Rect(526, 102, 120, 80)]);
        Assert(rightNeighborWithSmallOverlap.SnappedWidth && rightNeighborWithSmallOverlap.SnappedHeight,
            "A small right-edge overshoot incorrectly discarded the right neighbor.");
    }

    private static void AssertChildrenFit(AdaptiveIconPanel panel, double width, double height)
    {
        var rectangles = panel.Children.Cast<UIElement>()
            .Select(child =>
            {
                var offset = VisualTreeHelper.GetOffset(child);
                return new Rect(new Point(offset.X, offset.Y), child.RenderSize);
            })
            .ToArray();

        Assert(rectangles.All(rect => rect.Left >= 0 && rect.Top >= 0 && rect.Right <= width && rect.Bottom <= height),
            "A shortcut is outside the minimum organizer bounds.");

        for (var first = 0; first < rectangles.Length; first++)
        {
            for (var second = first + 1; second < rectangles.Length; second++)
            {
                Assert(!rectangles[first].IntersectsWith(rectangles[second]),
                    $"Shortcut rectangles {first} and {second} overlap.");
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertClose(double actual, double expected, string message)
    {
        if (Math.Abs(actual - expected) > 0.001)
        {
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}
