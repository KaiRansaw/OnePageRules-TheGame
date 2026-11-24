using Godot;
using OprGodot.Hud;

namespace OprGodot.HowTo;

/// <summary>
/// The Rules Guide's pictures (Kai, 2026-11-24: drawn diagrams, mockup screen 4): one simple top-down drawing per
/// lesson - blue is you, red the enemy, gold an objective, green what the lesson is about. Drawn here rather than
/// shipped as image files so the words on them use the game's own fonts and stay sharp at any size.
/// </summary>
public partial class GuideDiagram : Control
{
    public string Picture { get; set; } = "";

    private static readonly Color You = new(0.30f, 0.80f, 0.95f), Foe = new(0.93f, 0.36f, 0.30f), Obj = new(1f, 0.84f, 0.30f), Felt = new(0.11f, 0.17f, 0.13f),
        Grid = new(0.16f, 0.23f, 0.18f), Ink = new(0.87f, 0.91f, 0.91f);
    // Fixed colours, not the theme's: the pictures read the same in the Grimdark and the Fantasy look.
    private static readonly Color Go = new(0.58f, 0.86f, 0.30f), Dim = new(0.50f, 0.56f, 0.57f), Edge = new(0.30f, 0.42f, 0.45f);

    public GuideDiagram() { CustomMinimumSize = new Vector2(480, 340); }

    public override void _Draw()
    {
        Vector2 s = Size;
        DrawRect(new Rect2(Vector2.Zero, s), Felt);
        for (float x = 0; x < s.X; x += 32) DrawLine(new Vector2(x, 0), new Vector2(x, s.Y), Grid);
        for (float y = 0; y < s.Y; y += 32) DrawLine(new Vector2(0, y), new Vector2(s.X, y), Grid);
        // Everything is laid out on a 480 x 340 canvas and scaled to fit.
        float k = Mathf.Min(s.X / 480f, s.Y / 340f);
        DrawSetTransform(new Vector2((s.X - 480 * k) / 2, (s.Y - 340 * k) / 2), 0, new Vector2(k, k));
        switch (Picture)
        {
            case "dice": Dice(); break;
            case "objectives": Objectives(); break;
            case "deployment": Deployment(); break;
            case "actions": Actions(); break;
            case "cover": Cover(); break;
            case "shooting": Shooting(); break;
            case "melee": Melee(); break;
            case "fatigue": Fatigue(); break;
            case "morale": Morale(); break;
            case "hero": Hero(); break;
            case "seize": Seize(); break;
            case "rules": Rules(); break;
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawRect(new Rect2(Vector2.Zero, s), Edge, false, 1);
    }

    // ------------------------------------------------------------------ pieces

    private void Say(string text, float x, float y, int size = 17, Color? colour = null, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
        => DrawString(HudKit.Semi, new Vector2(x, y), text, align, width, size, colour ?? Ink);

    private void Unit(float cx, float cy, Color c, int n, float spacing = 22, int perRow = 5, IReadOnlyCollection<int>? gone = null)
    {
        for (int i = 0; i < n; i++)
        {
            float x = cx + (i % perRow - (Mathf.Min(n, perRow) - 1) / 2f) * spacing, y = cy + i / perRow * spacing;
            bool dead = gone?.Contains(i) == true;
            DrawCircle(new Vector2(x, y), 9, dead ? c with { A = 0.18f } : c);
            DrawArc(new Vector2(x, y), 9, 0, Mathf.Tau, 24, dead ? c with { A = 0.35f } : new Color(0, 0, 0, 0.6f), 2);
        }
    }

    private void Marker(float x, float y, Color owner, bool ring = true)
    {
        if (ring) DrawArc(new Vector2(x, y), 42, 0, Mathf.Tau, 64, Obj with { A = 0.45f }, 2);
        DrawCircle(new Vector2(x, y), 11, owner);
        DrawArc(new Vector2(x, y), 11, 0, Mathf.Tau, 24, Colors.Black, 2);
    }

    private void Arrow(Vector2 a, Vector2 b, Color c, float width = 3, bool dashed = false)
    {
        if (dashed) DrawDashedLine(a, b, c, width, 10); else DrawLine(a, b, c, width);
        Vector2 d = (b - a).Normalized(), n = new(-d.Y, d.X);
        DrawColoredPolygon(new[] { b, b - d * 14 + n * 7, b - d * 14 - n * 7 }, c);
    }

    private void Die(float x, float y, int face, Color c, float size = 44)
    {
        DrawRect(new Rect2(x, y, size, size), new Color(0.05f, 0.08f, 0.09f));
        DrawRect(new Rect2(x, y, size, size), c, false, 2);
        DrawString(HudKit.Bold, new Vector2(x, y + size * 0.72f), face.ToString(), HorizontalAlignment.Center, size, (int)(size * 0.6f), c);
    }

    private void Box(float x, float y, float w, float h, Color c, bool fill = false)
    {
        if (fill) DrawRect(new Rect2(x, y, w, h), c with { A = 0.25f });
        DrawRect(new Rect2(x, y, w, h), c, false, 2);
    }

    // ------------------------------------------------------------------ the twelve pictures

    private void Dice()
    {
        Say("A Quality test at Quality 4+", 24, 40, 20);
        for (int f = 1; f <= 6; f++) Die(24 + (f - 1) * 72, 70, f, f >= 4 ? Go : Dim, 52);
        Say("miss", 24, 150, 15, Dim, HorizontalAlignment.Center, 196);
        Say("success", 240, 150, 15, Go, HorizontalAlignment.Center, 196);
        Box(24, 190, 432, 120, Edge);
        Die(44, 210, 6, Go, 40); Say("a 6 always succeeds", 100, 238, 17, Go);
        Die(44, 258, 1, Foe, 40); Say("a 1 always fails, whatever the modifiers", 100, 286, 17, Foe);
    }

    private void Objectives()
    {
        Marker(90, 170, You); Marker(240, 170, Obj); Marker(390, 170, Dim);
        Unit(90, 140, You, 3);
        Unit(390, 136, You, 2); Unit(390, 204, Foe, 2);
        Say("seized - yours", 90, 250, 15, You, HorizontalAlignment.Center, -1);
        Say("held by no one", 240, 250, 15, Obj);
        Say("both within 3\" - neutral", 330, 250, 15, Dim);
        Say("3\"", 120, 128, 14, Obj);
        Say("At the end of each round", 24, 40, 20);
        Say("After round 4, most markers wins.", 24, 310, 17, Go);
    }

    private void Deployment()
    {
        Box(20, 20, 440, 300, Edge);
        DrawRect(new Rect2(20, 20, 440, 70), Foe with { A = 0.18f });
        DrawRect(new Rect2(20, 250, 440, 70), You with { A = 0.18f });
        Say("enemy edge - within 12\"", 30, 46, 15, Foe);
        Say("your edge - within 12\"", 30, 314, 15, You);
        Arrow(new Vector2(420, 236), new Vector2(420, 104), Ink, 2); Arrow(new Vector2(420, 104), new Vector2(420, 236), Ink, 2);
        Say("24\"+", 380, 176, 16);
        Unit(120, 282, You, 5); Say("1", 60, 288, 18, Go);
        Unit(130, 52, Foe, 5); Say("2", 70, 58, 18, Go);
        Unit(300, 282, You, 5); Say("3", 240, 288, 18, Go);
        Unit(310, 52, Foe, 5); Say("4", 250, 58, 18, Go);
        Say("Take turns placing one unit each", 110, 176, 17);
    }

    private void Actions()
    {
        (string Name, float Inches, string Note)[] a = { ("Hold", 0, "can shoot"), ("Advance", 6, "then can shoot"), ("Rush", 12, "can't shoot"), ("Charge", 12, "into melee") };
        for (int i = 0; i < a.Length; i++)
        {
            float y = 50 + i * 72;
            Say(a[i].Name, 24, y + 6, 20, Go);
            Unit(140, y, You, 1);
            if (a[i].Inches > 0) Arrow(new Vector2(154, y), new Vector2(154 + a[i].Inches * 20, y), i == 3 ? Foe : Go, 3);
            if (i == 3) Unit(154 + 12 * 20 + 14, y, Foe, 1);
            Say($"{a[i].Inches:0}\"  -  {a[i].Note}", 140, y + 32, 15, Dim);
        }
    }

    private void Cover()
    {
        DrawCircle(new Vector2(150, 220), 90, new Color(0.16f, 0.30f, 0.17f));
        DrawArc(new Vector2(150, 220), 90, 0, Mathf.Tau, 64, new Color(0.35f, 0.6f, 0.38f), 2);
        Say("Forest: Cover + Difficult", 70, 330, 15, new Color(0.6f, 0.85f, 0.6f));
        Unit(150, 210, You, 5);
        Unit(380, 60, Foe, 5);
        Arrow(new Vector2(366, 74), new Vector2(176, 196), Foe, 3, dashed: true);
        Box(280, 140, 180, 62, Go, true);
        Say("+1 to Defense rolls", 292, 166, 17, Go);
        Say("against shooting", 292, 188, 15, Ink);
        Say("Moving through it: 6\" max", 24, 40, 18);
    }

    private void Shooting()
    {
        Unit(110, 260, You, 5);
        Unit(370, 80, Foe, 5);
        Arrow(new Vector2(140, 246), new Vector2(340, 100), Obj, 3, dashed: true);
        Say("in range + line of sight", 190, 160, 16, Obj);
        Say("1  Your models roll to hit", 30, 316, 17, You);
        Say("2  They roll to block", 290, 40, 17, Foe);
        Die(250, 230, 6, Go, 30); Die(284, 230, 4, Go, 30); Die(318, 230, 2, Dim, 30);
        Say("Quality test per attack", 250, 280, 14, Dim);
    }

    private void Melee()
    {
        Unit(240, 210, You, 5);
        Unit(240, 186, Foe, 5);
        Arrow(new Vector2(240, 320), new Vector2(240, 232), You, 3);
        Say("charge - into base contact", 250, 300, 15, You);
        Say("1  The charger strikes", 24, 60, 18, You);
        Say("2  The defender may strike back", 24, 88, 18, Foe);
        Say("3  Most wounds taken = lost the melee", 24, 116, 18, Go);
        Say("    - the loser takes a Morale Test", 24, 140, 15, Dim);
    }

    private void Fatigue()
    {
        Unit(240, 120, You, 5);
        Say("After a melee where they attacked:", 24, 40, 18);
        for (int f = 1; f <= 6; f++) Die(60 + (f - 1) * 62, 180, f, f == 6 ? Go : Dim, 46);
        Say("hits only on an unmodified 6 in melee,", 24, 270, 17, Go);
        Say("until the end of the round", 24, 294, 17, Go);
    }

    private void Morale()
    {
        Unit(150, 110, Foe, 10, gone: new[] { 5, 6, 7, 8, 9 });
        Say("half or less left - Morale Test", 40, 200, 16, Ink);
        Die(300, 80, 2, Foe, 52);
        Say("Quality test failed", 270, 158, 15, Foe);
        Box(40, 230, 400, 84, Foe, true);
        Say("SHAKEN", 60, 262, 22, Foe);
        Say("stays idle, can't seize objectives,", 60, 286, 15, Ink);
        Say("strikes back as if Fatigued", 60, 306, 15, Ink);
    }

    private void Hero()
    {
        Unit(240, 150, You, 5, spacing: 26);
        DrawCircle(new Vector2(240, 196), 12, Obj);
        DrawArc(new Vector2(240, 196), 12, 0, Mathf.Tau, 24, Colors.Black, 2);
        Say("Hero, joined to the unit", 270, 202, 16, Obj);
        for (int p = 0; p < 3; p++) DrawRect(new Rect2(214 + p * 18, 220, 12, 12), Go, false, 2);
        Say("Tough(3): three wounds", 200, 252, 15, Go);
        Say("Wounds go on the Hero last.", 24, 40, 18);
        Say("Its unit may use the Hero's Quality for Morale Tests.", 24, 300, 15, Dim);
    }

    private void Seize()
    {
        Marker(240, 170, You);
        Unit(240, 150, You, 3);
        Unit(240, 290, Foe, 3);
        Arrow(new Vector2(300, 170), new Vector2(282, 170), Obj, 2);
        Say("3\"", 304, 176, 16, Obj);
        Say("No enemy within 3\" at round end: yours", 24, 40, 18, You);
        Say("Stays yours until the enemy contests it", 24, 66, 15, Dim);
    }

    private void Rules()
    {
        Box(60, 60, 360, 200, Edge, true);
        Say("Unit card", 80, 92, 20);
        string[] chips = { "AP(1)", "Fearless", "Hero", "Tough(3)" };
        float x = 80;
        foreach (string c in chips)
        {
            float w = 18 + c.Length * 9;
            Box(x, 112, w, 28, Obj);
            Say(c, x + 8, 132, 15, Obj);
            x += w + 10;
        }
        Box(170, 160, 230, 76, Go, true);
        Say("hover a rule:", 182, 184, 14, Dim);
        Say("the book's own words", 182, 210, 17, Go);
        Arrow(new Vector2(150, 150), new Vector2(176, 170), Go, 2);
        Say("Special rules sit on units and weapons.", 24, 310, 16);
    }
}
