using System;
using System.IO;
using System.Linq;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using NUnit.Framework;

namespace FdgRaylib.Tests
{
    /// <summary>
    /// Kai, 2026-11-02: "add an option in the map maker as to what game mode you're making the map for.
    /// The map size changes depending on the game (since some gamemodes are on 4x4 or 6x4)."
    ///
    /// <para>
    /// A map now carries the table it was drawn on. Stored as a SIZE rather than a game system: a 72x48
    /// map is playable in any system that uses a 72x48 table, and binding a map to one system would
    /// hide it from the others for no reason.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MapTableSizeTests
    {
        /// <summary>
        /// The load-bearing one. Every map drawn before this field existed was drawn on 6' x 4', and
        /// the default has to agree - a 0 default would make every existing map "0 x 0 inches", which
        /// the picker would draw as nothing and the editor would clamp every piece to the origin.
        /// </summary>
        [Test]
        public void AMapWrittenBeforeTableSizesIsSixByFour()
        {
            // No table fields at all, which is exactly what an old file on disk looks like.
            const string json = """{"Name":"Old Map","Pieces":[],"Objectives":[],"DeploymentZones":[]}""";

            TerrainLayoutFile? layout = LoadFromText(json, out string? error);

            Assert.That(layout, Is.Not.Null, error);
            Assert.That(layout!.TableWidthInches, Is.EqualTo(72f));
            Assert.That(layout.TableHeightInches, Is.EqualTo(48f));
            Assert.That(layout.BaseMarkers, Is.Empty, "and no base references, rather than null.");
        }

        [Test]
        public void ATableSizeRoundTrips()
        {
            var original = new TerrainLayoutFile
            {
                Name = "Four by four",
                TableWidthInches = 48f,
                TableHeightInches = 48f,
            };

            TerrainLayoutFile? back = RoundTrip(original, out string? error);

            Assert.That(back, Is.Not.Null, error);
            Assert.That(back!.TableWidthInches, Is.EqualTo(48f));
            Assert.That(back.TableHeightInches, Is.EqualTo(48f));
        }

        /// <summary>
        /// Every size the editor offers has to be a size some system actually plays on, because that is
        /// where the editor's list comes from. If a new system arrives with a 44x60 table this test
        /// says nothing - but if someone hard-codes a size into the editor that no game uses, the
        /// editor's own list is the thing that would have to change, and this records why.
        /// </summary>
        [Test]
        public void TheGamesPlayOnExactlyTwoTableSizes()
        {
            var sizes = Enum.GetValues<EGameSystem>()
                .Select(GameWideConstants.TableSizeFor)
                .Distinct()
                .ToList();

            Assert.That(sizes, Is.EquivalentTo(new[] { (72f, 48f), (48f, 48f) }),
                "the map maker offers one button per size here. A new size means a new button, which "
                + "it builds from this same list - but a size nothing plays on would be a button that "
                + "draws maps no game can use.");
        }

        /// <summary>
        /// Base references are saved with the map and must come back as what they were - a 28mm circle
        /// that reloads as a 1-inch square is a reference that lies about the thing being referenced.
        /// </summary>
        [Test]
        public void BaseReferencesRoundTrip()
        {
            var original = new TerrainLayoutFile
            {
                Name = "With refs",
                BaseMarkers =
                {
                    new MapBaseMarkerEntry
                    {
                        X = 12f, Z = 20f, Label = "28mm",
                        Base = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = 1.1023622f },
                    },
                    new MapBaseMarkerEntry
                    {
                        X = 30f, Z = 10f, Label = "25x50",
                        Base = new BaseFileEntry
                        {
                            Shape = EBaseShapeKind.Rectangle,
                            WidthInches = 0.984252f, HeightInches = 1.968504f,
                        },
                    },
                },
            };

            TerrainLayoutFile? back = RoundTrip(original, out string? error);

            Assert.That(back, Is.Not.Null, error);
            Assert.That(back!.BaseMarkers, Has.Count.EqualTo(2));

            MapBaseMarkerEntry circle = back.BaseMarkers[0];
            Assert.That(circle.Base.Shape, Is.EqualTo(EBaseShapeKind.Circle));
            Assert.That(circle.Base.DiameterInches, Is.EqualTo(1.1023622f).Within(0.0001f));
            Assert.That(circle.X, Is.EqualTo(12f));
            Assert.That(circle.Label, Is.EqualTo("28mm"));

            MapBaseMarkerEntry rect = back.BaseMarkers[1];
            Assert.That(rect.Base.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
            Assert.That(rect.Base.WidthInches, Is.EqualTo(0.984252f).Within(0.0001f));
            Assert.That(rect.Base.HeightInches, Is.EqualTo(1.968504f).Within(0.0001f));
        }

        /// <summary>
        /// Base references must be invisible to the game. They are not terrain, they block nothing, and
        /// nothing reads them outside the map maker - so the one thing to prove is that no engine code
        /// has quietly started treating them as content.
        /// </summary>
        [Test]
        public void NothingOutsideTheMapMakerReadsBaseReferences()
        {
            string? repo = RepoRoot();
            if (repo == null) Assert.Ignore("Not running from a checkout.");

            var readers = Directory
                .EnumerateFiles(Path.Combine(repo!, "FutureOfDarkGrimness"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
                .Where(f => Path.GetFileName(f) != "TerrainLayoutFile.cs")
                .Where(f => File.ReadAllText(f).Contains("BaseMarkers", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToList();

            Assert.That(readers, Is.Empty,
                "base references are a drawing aid for the person making a map. If engine code has "
                + "started reading them, either that is a bug or they have become real content - and "
                + "either way the comment on TerrainLayoutFile.BaseMarkers saying the game ignores "
                + "them is now a lie.");
        }

        /// <summary>
        /// An L-shaped deployment zone (Kai, 2026-11-02) survives a save and reload. Written as one
        /// rectangle plus extras so a map from before odd shapes existed is still a valid zone, and so
        /// anything reading only the four edges gets a sensible rectangle rather than nothing.
        /// </summary>
        [Test]
        public void AnLShapedDeploymentZoneRoundTrips()
        {
            var original = new TerrainLayoutFile
            {
                Name = "L zone",
                DeploymentZones =
                {
                    new MapDeploymentZoneEntry
                    {
                        Left = 0f, Right = 20f, Bottom = 0f, Top = 6f,
                        ExtraParts = { new MapZoneRect { Left = 0f, Right = 6f, Bottom = 6f, Top = 20f } },
                    },
                    new MapDeploymentZoneEntry { Left = 0f, Right = 72f, Bottom = 36f, Top = 48f },
                },
            };

            TerrainLayoutFile? back = RoundTrip(original, out string? error);

            Assert.That(back, Is.Not.Null, error);
            Assert.That(back!.DeploymentZones, Has.Count.EqualTo(2));

            MapDeploymentZoneEntry lshape = back.DeploymentZones[0];
            Assert.That(lshape.ExtraParts, Has.Count.EqualTo(1));
            Assert.That(lshape.ExtraParts[0].Top, Is.EqualTo(20f));
            Assert.That(lshape.AllParts().Count(), Is.EqualTo(2), "the main rectangle plus the arm.");

            Assert.That(back.DeploymentZones[1].ExtraParts, Is.Empty,
                "an ordinary rectangular zone stays ordinary.");
            Assert.That(back.DeploymentZones[1].AllParts().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// A zone saved before odd shapes existed has no ExtraParts field at all, and must come back as
        /// a plain one-rectangle zone rather than one with a null list that AllParts would throw on.
        /// </summary>
        [Test]
        public void AZoneWrittenBeforeOddShapesIsStillOneRectangle()
        {
            const string json = """
                {"Name":"Old","Pieces":[],"Objectives":[],
                 "DeploymentZones":[{"Left":0.0,"Right":72.0,"Bottom":0.0,"Top":12.0}]}
                """;

            TerrainLayoutFile? layout = LoadFromText(json, out string? error);

            Assert.That(layout, Is.Not.Null, error);
            Assert.That(layout!.DeploymentZones, Has.Count.EqualTo(1));
            Assert.That(layout.DeploymentZones[0].AllParts().Count(), Is.EqualTo(1));
            Assert.That(layout.DeploymentZones[0].ExtraParts, Is.Not.Null,
                "AllParts must not throw on a file that never had the field.");
        }

        /// <summary>
        /// Writes the layout and reads it back the way the game does - through a real file. There is no
        /// deserialize-from-string on the app's loader, and going through disk is the path that
        /// actually runs when a map is saved and reopened, so this is the honest round trip rather
        /// than a shortcut past it.
        /// </summary>
        private static TerrainLayoutFile? RoundTrip(TerrainLayoutFile layout, out string? error) =>
            LoadFromText(TerrainLoader.SerializeToJson(layout), out error);

        private static TerrainLayoutFile? LoadFromText(string json, out string? error)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "fdg-map-test-" + Guid.NewGuid().ToString("N")[..8] + TerrainLayoutFile.EXTENSION_WITH_PERIOD);
            try
            {
                File.WriteAllText(path, json);
                return TerrainLoader.TryLoadFromFile(path, out error);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        private static string? RepoRoot()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                 dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "FutureOfDarkGrimness"))) return dir.FullName;
            }
            return null;
        }
    }
}
