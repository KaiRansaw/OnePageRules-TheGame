using FDG;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The doctrine picker and the in-battle stratagem drawer (Kai, 2026-11-25, mockup 2B) read every stratagem through
/// <see cref="StratagemBook"/>: each one needs its "when" tag, each doctrine its four stratagems and a line saying
/// what it's about - a stratagem added to the engine without one would show a blank tag.
/// </summary>
[TestFixture]
public class StratagemBookTests
{
    [Test]
    public void EveryStratagem_HasAWhenTag()
    {
        foreach (var u in CommandPoints.Universal) Assert.That(StratagemBook.When(u.Name), Is.Not.Empty, u.Name);
        foreach (var d in CommandPoints.DoctrineStratagems) Assert.That(StratagemBook.When(d.Name), Is.Not.Empty, d.Name);
    }

    [Test]
    public void EveryDoctrine_HasFourStratagems_AndATheme()
    {
        for (int d = 1; d <= 6; d++)
        {
            Assert.That(StratagemBook.OfDoctrine(d), Has.Count.EqualTo(4), CommandPoints.DoctrineName(d));
            Assert.That(StratagemBook.Theme(d), Is.Not.Empty);
            Assert.That(StratagemBook.Short(d), Does.Not.Contain("Doctrine"));
        }
        Assert.That(StratagemBook.Universal(), Has.Count.EqualTo(6));
    }

    [Test]
    public void TheText_IsTheEnginesOwn()
    {
        var e = StratagemBook.OfDoctrine(3).First(x => x.Name == CommandPoints.SweepingMove);
        Assert.That(e.Cost, Is.EqualTo(1));
        Assert.That(e.Text, Does.Contain("+4\""));
        Assert.That(e.When, Is.EqualTo("Charge"));
    }
}
