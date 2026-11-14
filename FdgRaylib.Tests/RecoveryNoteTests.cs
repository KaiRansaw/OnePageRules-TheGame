using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #187 in Godot: when a dropped connection ends a game the host writes a recovery save, and the end card says where
/// it went and how to pick the game up (or that it couldn't be written).
/// </summary>
[TestFixture]
public class RecoveryNoteTests
{
    [Test]
    public void TheNote_NamesTheFile_AndSaysHowToResume()
    {
        string note = GameSession.RecoveryNoteFor("/some/where/Saves/recovery-20261113-020000.fdgsave");
        Assert.That(note, Does.Contain("recovery-20261113-020000.fdgsave"));
        Assert.That(note, Does.Contain("Load Game"));
        Assert.That(note.All(c => c < 128), "game text is ASCII only");
    }

    [Test]
    public void AFailedWrite_SaysSo() =>
        Assert.That(GameSession.RecoveryNoteFor(null), Does.Contain("could not be written"));
}
