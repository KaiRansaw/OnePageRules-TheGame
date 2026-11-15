using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-14: each unit picture can be moved and zoomed in its frame (Asset Lab -> Frame pictures).</summary>
[TestFixture]
public class PictureFramingTests
{
    [Test]
    public void AsItComes_TheLargestCentredCropOfTheFramesShape()
    {
        // A square picture in a 2:1 frame: full width, the middle half of the height.
        Assert.That(PictureFraming.Region(800, 800, 2f, PictureFraming.Frame.Default), Is.EqualTo((0f, 200f, 800f, 400f)));
    }

    [Test]
    public void Zoom_Shrinks_TheCrop_AroundTheChosenPoint_AndNeverLeavesThePicture()
    {
        var (x, y, w, h) = PictureFraming.Region(800, 800, 1f, new PictureFraming.Frame(0.25f, 0.5f, 2f));
        Assert.That((x, y, w, h), Is.EqualTo((0f, 200f, 400f, 400f)), "centred on a quarter across, kept inside the left edge");
        (x, _, w, _) = PictureFraming.Region(800, 800, 1f, new PictureFraming.Frame(1f, 0.5f, 4f));
        Assert.That(x + w, Is.EqualTo(800f), "the right edge, not past it");
    }

    [Test]
    public void Frames_AreSavedPerUnit_IgnoringCaseAndPunctuation_AndResetRemovesThem()
    {
        string file = Path.Combine(Path.GetTempPath(), $"framing-{Guid.NewGuid():N}.json");
        try
        {
            PictureFraming.Forget();
            PictureFraming.Set("Alien Hives", "Assault Grunts", new PictureFraming.Frame(0.4f, 0.3f, 9f), file);
            PictureFraming.Forget();
            var f = PictureFraming.For("alien-hives", "ASSAULT GRUNTS", file);
            Assert.That(f, Is.EqualTo(new PictureFraming.Frame(0.4f, 0.3f, PictureFraming.MaxZoom)), "read back, zoom capped");
            Assert.That(File.ReadAllText(file), Does.Not.Contain("IsDefault"));
            PictureFraming.Set("Alien Hives", "Assault Grunts", PictureFraming.Frame.Default, file);
            PictureFraming.Forget();
            Assert.That(PictureFraming.For("Alien Hives", "Assault Grunts", file).IsDefault, Is.True);
        }
        finally { File.Delete(file); PictureFraming.Forget(); }
    }
}
