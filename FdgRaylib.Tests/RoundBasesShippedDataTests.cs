using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Kai, 2026-11-18 (house rule): every model stands on a ROUND base, sized from Army Forge - an oval or a
// vehicle's rectangle plays as a circle as wide as its long side. The engine reads any leftover rectangle that
// way (BaseFileEntry.ToBaseShape); this keeps the shipped books themselves honest, so the Army Forge screen
// shows what is played.
[TestFixture]
public class RoundBasesShippedDataTests
{
    [Test]
    public void EveryShippedBookBase_IsACircle()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        var bad = Directory.GetFiles(root, "*.fdgbook", SearchOption.AllDirectories)
            .Where(p => File.ReadAllText(p).Contains("\"shape\": \"Rectangle\""))
            .Select(Path.GetFileName).ToList();
        Assert.That(Directory.GetFiles(root, "*.fdgbook", SearchOption.AllDirectories), Has.Length.GreaterThan(200));
        Assert.That(bad, Is.Empty);
    }
}
