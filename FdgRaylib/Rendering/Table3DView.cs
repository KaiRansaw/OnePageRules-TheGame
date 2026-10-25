using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The 3D table view. Draws the table, terrain at its height, objective markers and every model as its base
/// with an upright card, from an orbiting camera. Stage 2: the game is played in it too - the table tools'
/// drawing is painted onto the felt (<see cref="SetOverlay"/>) and the mouse is turned back into a table
/// point (<see cref="Pick"/>).
///
/// <para>World space: inches, Y up, table centred on the origin. Table x maps to world X; table z (up the
/// screen in 2D) maps to world -Z, so the default camera - sitting off the bottom edge - sees the board the
/// same way round as the 2D view.</para>
///
/// <para>How a model LOOKS is decided in one place (<see cref="DrawFigure"/>): today a card with the unit's
/// name; later a custom image, then a real 3D model, can take its place without touching the rest.</para>
/// </summary>
internal sealed class Table3DView
{
    /// <summary>One model to draw this frame, already resolved by the renderer (gliding position,
    /// dying tint/alpha, owner color).</summary>
    internal readonly record struct ModelDraw(Position Position, float BaseRadius, Color Color, float Alpha,
        UnitID UnitId, string Label, bool IsHero, bool IsActive, ModelID ModelId, string Faction = "");

    internal readonly record struct ObjectiveDraw(Position Position, string Label, Color Color, bool IsPriority, bool IsToken = false);

    internal readonly record struct TerrainDraw(IZone Shape, ETerrainType Type, float HeightInches);

    internal readonly record struct AttackDraw(IReadOnlyList<Position> From, IReadOnlyList<Position> To, bool IsMelee,
        float Progress);

    internal enum EPreset { Default, Top, South, North, West, East }

    private const float BaseHeight = 0.12f;
    private const float CardAspect = 1.45f;   // card height / width

    private readonly float _tableW;
    private readonly float _tableH;

    // Orbit camera state.
    private Vector3 _target = Vector3.Zero;
    private float _yaw;        // degrees; 0 = looking from the bottom (south) edge toward the top
    private float _pitch;      // degrees above the table
    private float _distance;   // inches from the target

    private RenderTexture2D _target3D;
    private bool _targetReady;

    // Stage 2: what the table tools drew this frame (move ghosts, rings, ruler...), painted onto the felt.
    private Texture2D? _overlay;

    // The last frame's camera and models, for turning the mouse into a table point / card pick.
    private Camera3D _lastCamera;
    private IReadOnlyList<ModelDraw> _lastModels = Array.Empty<ModelDraw>();
    private int _targetW, _targetH;

    // One card texture per unit (and per joined hero), rebuilt when its name, color or hero mark changes.
    private readonly Dictionary<(UnitID Unit, bool Hero), (RenderTexture2D Texture, string Key)> _cards = new();
    private readonly Dictionary<string, RenderTexture2D> _objectiveLabels = new();

    // Unit pictures from the UnitImages folder, shown as upright standees instead of the name card.
    private readonly UnitImageLibrary _images = new();
    // Throws away the see-through pixels of a standee so they don't hide what stands behind them.
    private Shader? _cutoutShader;
    private bool _cutoutTried;
    private const string CutoutFragment = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
out vec4 finalColor;
void main()
{
    vec4 c = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
    if (c.a < 0.35) discard;
    finalColor = c;
}";

    public Table3DView(float tableWidthInches, float tableHeightInches)
    {
        _tableW = tableWidthInches;
        _tableH = tableHeightInches;
        SetPreset(EPreset.Default);
    }

    // ------------------------------------------------------------------ camera

    public void SetPreset(EPreset preset)
    {
        _target = Vector3.Zero;
        float fit = MathF.Max(_tableW, _tableH);
        switch (preset)
        {
            case EPreset.Top:   _yaw = 0f;   _pitch = 89f; _distance = fit * 1.15f; break;
            case EPreset.South: _yaw = 0f;   _pitch = 35f; _distance = fit * 1.05f; break;
            case EPreset.North: _yaw = 180f; _pitch = 35f; _distance = fit * 1.05f; break;
            case EPreset.West:  _yaw = 270f; _pitch = 35f; _distance = fit * 0.95f; break;
            case EPreset.East:  _yaw = 90f;  _pitch = 35f; _distance = fit * 0.95f; break;
            default:            _yaw = 0f;   _pitch = 52f; _distance = fit * 1.1f;  break;
        }
    }

    private Camera3D Camera()
    {
        float yaw = _yaw * MathF.PI / 180f, pitch = _pitch * MathF.PI / 180f;
        // yaw 0: camera sits toward +Z (the bottom edge) looking at the target.
        var offset = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        return new Camera3D
        {
            Position = _target + offset * _distance,
            Target = _target,
            Up = new Vector3(0f, 1f, 0f),
            FovY = 45f,
            Projection = CameraProjection.Perspective,
        };
    }

    /// <summary>
    /// Mouse: middle-drag orbits (right-drag too, unless the active tool uses right-click), Shift+middle-drag
    /// pans, Alt+wheel zooms (plain wheel too while no table tool wants it for rotating). Keys: arrows move
    /// over the table; WASD / Q-E also move and turn while no decision panel is open (its letter hotkeys
    /// would clash).
    /// </summary>
    public void HandleInput(Rectangle viewport, bool mouseFree, bool keysFree,
        bool rightDragOrbits = true, bool plainWheelZooms = true, bool letterKeysMove = true)
    {
        float dt = MathF.Min(Raylib.GetFrameTime(), 0.05f);
        Vector2 mouse = Raylib.GetMousePosition();
        bool over = Raylib.CheckCollisionPointRec(mouse, viewport);
        float yaw = _yaw * MathF.PI / 180f;
        // Ground directions relative to the camera: forward points away from the camera.
        var forward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);

        if (mouseFree && over)
        {
            Vector2 delta = Raylib.GetMouseDelta();
            bool middle = Raylib.IsMouseButtonDown(MouseButton.Middle);
            if ((middle && !shift) || (rightDragOrbits && Raylib.IsMouseButtonDown(MouseButton.Right)))
            {
                _yaw -= delta.X * 0.3f;
                _pitch = Math.Clamp(_pitch + delta.Y * 0.25f, 8f, 89f);
            }
            if (middle && shift)
            {
                float k = _distance * 0.0016f;
                _target += (-right * delta.X + forward * delta.Y) * k;
            }
        }

        // Zoom. Alt+wheel works whenever the pointer is over the board: holding Alt brings up the measuring
        // tool, which claims the mouse for ImGui, so it can't wait for the mouse to be "free" (the 2D view
        // gates its Alt+wheel zoom the same way). The plain wheel zooms only when nothing else wants it.
        float wheel = Raylib.GetMouseWheelMove();
        if (over && wheel != 0f && (alt || (mouseFree && plainWheelZooms)))
            _distance = Math.Clamp(_distance * MathF.Pow(0.9f, wheel), 6f, 220f);

        if (keysFree)
        {
            // Keyboard camera: WASD / arrows move, Q / E turn, Shift+W / S zoom, Shift+Q / E tilt.
            // Ctrl held = a shortcut (Ctrl+S, Ctrl+Z...), so the letters don't move the camera then.
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            float k = ViewSettings.CameraSpeed;
            float speed = MathF.Max(8f, _distance * 0.8f) * dt * k;
            bool letters = letterKeysMove && !ctrl;
            bool W = letters && Raylib.IsKeyDown(KeyboardKey.W), S = letters && Raylib.IsKeyDown(KeyboardKey.S);
            bool A = letters && Raylib.IsKeyDown(KeyboardKey.A), D = letters && Raylib.IsKeyDown(KeyboardKey.D);
            bool Q = letters && Raylib.IsKeyDown(KeyboardKey.Q), E = letters && Raylib.IsKeyDown(KeyboardKey.E);

            if (Raylib.IsKeyDown(KeyboardKey.Up) || (W && !shift)) _target += forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.Down) || (S && !shift)) _target -= forward * speed;
            if (Raylib.IsKeyDown(KeyboardKey.Right) || D) _target += right * speed;
            if (Raylib.IsKeyDown(KeyboardKey.Left) || A) _target -= right * speed;
            if (Q && !shift) _yaw += 70f * dt * k;
            if (E && !shift) _yaw -= 70f * dt * k;
            if (W && shift) _distance = Math.Clamp(_distance * MathF.Pow(0.35f, dt * k), 6f, 220f);
            if (S && shift) _distance = Math.Clamp(_distance / MathF.Pow(0.35f, dt * k), 6f, 220f);
            float tilt = 45f * dt * k * (ViewSettings.InvertTilt ? -1f : 1f);
            if (Q && shift) _pitch = Math.Clamp(_pitch - tilt, 8f, 89f);   // tilt up: toward the horizon
            if (E && shift) _pitch = Math.Clamp(_pitch + tilt, 8f, 89f);   // tilt down: toward top-down
        }

        // Keep the point the camera circles somewhere over (or just off) the table.
        _target.X = Math.Clamp(_target.X, -_tableW * 0.75f, _tableW * 0.75f);
        _target.Z = Math.Clamp(_target.Z, -_tableH * 0.75f, _tableH * 0.75f);
        _yaw %= 360f;
    }

    /// <summary>The preset-view buttons and controls hint, docked bottom-left of the board (ImGui pass).</summary>
    public void DrawControls(Rectangle viewport)
    {
        // Bottom-right of the board, clear of the Menu / Army Lists buttons in the bottom-left corner.
        ImGui.SetNextWindowPos(new Vector2(viewport.X + viewport.Width - 12f, viewport.Y + viewport.Height - 12f),
            ImGuiCond.Always, new Vector2(1f, 1f));
        ImGui.SetNextWindowBgAlpha(0.6f);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##view3dControls", flags))
        {
            ImGui.TextDisabled("3D VIEW");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset")) SetPreset(EPreset.Default);
            ImGui.SameLine();
            if (ImGui.SmallButton("Top")) SetPreset(EPreset.Top);
            ImGui.SameLine();
            if (ImGui.SmallButton("South")) SetPreset(EPreset.South);
            ImGui.SameLine();
            if (ImGui.SmallButton("North")) SetPreset(EPreset.North);
            ImGui.SameLine();
            if (ImGui.SmallButton("West")) SetPreset(EPreset.West);
            ImGui.SameLine();
            if (ImGui.SmallButton("East")) SetPreset(EPreset.East);
            ImGui.TextDisabled("Middle or right-drag: rotate   Shift+middle-drag: pan   Alt+wheel: zoom   Arrows/WASD: move   Q/E: turn");
        }
        ImGui.End();
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>
    /// Renders the scene into the board area. Call in the Raylib pass, outside any other texture mode.
    /// </summary>
    public void Draw(Rectangle viewport, Color surface, Color border, Color background,
        IReadOnlyList<TerrainDraw> terrain, IReadOnlyList<ObjectiveDraw> objectives,
        IReadOnlyList<ModelDraw> models, IReadOnlyList<AttackDraw> attacks)
    {
        int w = Math.Max(1, (int)viewport.Width), h = Math.Max(1, (int)viewport.Height);
        EnsureTarget(w, h);

        // Card / label textures are drawn with their own texture mode, so build them before ours begins.
        foreach (ModelDraw model in models) EnsureCard(model);
        foreach (ObjectiveDraw objective in objectives) EnsureObjectiveLabel(objective.Label);

        Camera3D camera = Camera();
        _lastCamera = camera;
        _lastModels = models;
        Raylib.BeginTextureMode(_target3D);
        Raylib.ClearBackground(background);
        Raylib.BeginMode3D(camera);

        DrawTableTop(surface, border);
        foreach (ObjectiveDraw objective in objectives) DrawObjective(objective, camera);
        foreach (ModelDraw model in models) DrawBase(model);
        DrawOverlay();
        DrawCursor();
        DrawFigures(models, camera);
        foreach (AttackDraw attack in attacks) DrawAttack(attack);

        // See-through terrain last, without writing depth, so models inside or behind it stay visible.
        Rlgl.DisableDepthMask();
        foreach (TerrainDraw piece in terrain) DrawTerrain(piece);
        Rlgl.EnableDepthMask();

        Raylib.EndMode3D();
        Raylib.EndTextureMode();

        // Render textures come out upside down: flip the source rect.
        Raylib.DrawTextureRec(_target3D.Texture, new Rectangle(0, 0, w, -h), new Vector2(viewport.X, viewport.Y), Color.White);
    }

    /// <summary>Where a table point (inches) appears on the screen, or null before the first 3D frame.
    /// Used to open decision pop-ups next to the acting unit.</summary>
    public Vector2? WorldToScreen(Rectangle viewport, float xIn, float zIn, float heightIn = 0f)
    {
        if (!_targetReady) return null;
        Vector2 p = Raylib.GetWorldToScreenEx(World(xIn, heightIn, zIn), _lastCamera, (int)viewport.Width, (int)viewport.Height);
        return new Vector2(p.X + viewport.X, p.Y + viewport.Y);
    }

    private Vector3 World(float x, float y, float z) => new(x - _tableW / 2f, y, _tableH / 2f - z);

    // ------------------------------------------------------------------ stage 2: tools on the 3D table

    /// <summary>The flat picture of this frame's table tools (see RaylibRenderer), or null for none. It
    /// covers the whole table: its left edge is table x=0, its bottom edge is table z=0, and it holds
    /// premultiplied color (it is drawn with premultiplied blending).</summary>
    public void SetOverlay(Texture2D? overlay) => _overlay = overlay;

    /// <summary>Where the pointer is on the table (inches), drawn as a small ring so it is always clear which
    /// spot of the table the tools are reading; null hides it.</summary>
    public Float2? Cursor { get; set; }

    private void DrawCursor()
    {
        if (Cursor is not Float2 c) return;
        if (c.X < 0f || c.X > _tableW || c.Y < 0f || c.Y > _tableH) return;
        Vector3 p = World(c.X, 0.04f, c.Y);
        Raylib.DrawCylinderWires(p, 0.35f, 0.35f, 0.02f, 20, new Color(255, 255, 255, 220));
        Raylib.DrawLine3D(p - new Vector3(0.5f, 0f, 0f), p + new Vector3(0.5f, 0f, 0f), new Color(255, 255, 255, 220));
        Raylib.DrawLine3D(p - new Vector3(0f, 0f, 0.5f), p + new Vector3(0f, 0f, 0.5f), new Color(255, 255, 255, 220));
    }

    private void DrawOverlay()
    {
        if (_overlay is not Texture2D tex) return;
        const float y = 0.02f;   // just above the felt and its grid lines
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        Rlgl.DisableDepthMask();
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        // Render-texture pixels are stored bottom-up, so texture v runs with table z.
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex3f(-_tableW / 2f, y, -_tableH / 2f);  // table (0, H)
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex3f(-_tableW / 2f, y,  _tableH / 2f);  // table (0, 0)
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex3f( _tableW / 2f, y,  _tableH / 2f);  // table (W, 0)
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex3f( _tableW / 2f, y, -_tableH / 2f);  // table (W, H)
        Rlgl.End();
        Rlgl.SetTexture(0);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Raylib.EndBlendMode();
    }

    /// <summary>
    /// Where the mouse points: the table point under it (inches; may lie beyond the table edge; null when the
    /// pointer is off the 3D view or above the horizon) and the
    /// model whose upright card it is over (null for none). Uses the camera of the last drawn frame.
    /// </summary>
    public (Float2? TablePoint, ModelID? Card) Pick(Rectangle viewport, Vector2 mouse)
    {
        if (!_targetReady) return (null, null);
        Vector2 local = new(mouse.X - viewport.X, mouse.Y - viewport.Y);
        if (local.X < 0 || local.Y < 0 || local.X > viewport.Width || local.Y > viewport.Height) return (null, null);
        Ray ray = Raylib.GetScreenToWorldRayEx(local, _lastCamera, (int)viewport.Width, (int)viewport.Height);

        // The table surface (y = 0).
        Float2? point = null;
        float floorDist = float.MaxValue;
        if (ray.Direction.Y < -1e-5f)
        {
            float t = -ray.Position.Y / ray.Direction.Y;
            Vector3 hit = ray.Position + ray.Direction * t;
            float x = hit.X + _tableW / 2f, z = _tableH / 2f - hit.Z;
            // Off the table edge too: the tools get the same off-board point the 2D view would give for a
            // pointer beside the table (they ignore it for hovering and aim ghosts toward it), rather than
            // nothing at all.
            point = new Float2(x, z);
            floorDist = t;
        }

        // The point is always the felt straight under the pointer. Cards and terrain blocks are NOT used to
        // stop the ray: switching between "felt behind" and "the thing in front" made the move markers jump
        // as the pointer crossed a card or a building. The felt point moves smoothly with the mouse.
        // The cards: upright, turned to the camera the way DrawBillboardRec turns them.
        Vector3 forward = Vector3.Normalize(_lastCamera.Target - _lastCamera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        ModelID? card = null;
        float best = floorDist;
        foreach (ModelDraw model in _lastModels)
        {
            if (model.Alpha < 0.5f) continue;
            var (width, height, _) = FigureSize(model);
            Vector3 c = World(model.Position.x, BaseHeight + height / 2f, model.Position.z);
            Vector3 r = right * (width / 2f), u = Vector3.UnitY * (height / 2f);
            RayCollision hit = Raylib.GetRayCollisionQuad(ray, c - r - u, c - r + u, c + r + u, c + r - u);
            if (hit.Hit && hit.Distance < best)
            {
                best = hit.Distance;
                card = model.ModelId;
            }
        }
        return (point, card);
    }




    private void DrawTableTop(Color surface, Color border)
    {
        // The felt as a slab, sitting on a slightly wider, lower rim so the edge reads at low angles. The rim's
        // top is kept well below the felt's: two surfaces at nearly the same height flicker in stripes.
        Raylib.DrawCube(new Vector3(0f, -0.65f, 0f), _tableW + 0.8f, 1f, _tableH + 0.8f, border);
        Raylib.DrawCube(new Vector3(0f, -0.25f, 0f), _tableW, 0.5f, _tableH, surface);
        // Faint 6" grid so distances can be judged in perspective.
        var grid = new Color(255, 255, 255, 22);
        for (float x = 6f; x < _tableW; x += 6f)
            Raylib.DrawLine3D(World(x, 0.01f, 0f), World(x, 0.01f, _tableH), grid);
        for (float z = 6f; z < _tableH; z += 6f)
            Raylib.DrawLine3D(World(0f, 0.01f, z), World(_tableW, 0.01f, z), grid);
    }

    private void DrawObjective(ObjectiveDraw objective, Camera3D camera)
    {
        Vector3 p = World(objective.Position.x, 0f, objective.Position.z);
        if (objective.IsToken)
        {
            // Star Quest search token / treasure marker: a small disc with its label above.
            Raylib.DrawCylinder(p, 0.45f, 0.45f, 0.15f, 16, objective.Color);
            Raylib.DrawCylinderWires(p, 0.45f, 0.45f, 0.15f, 16, Color.Black);
            if (_objectiveLabels.TryGetValue(objective.Label, out RenderTexture2D tokenLabel))
            {
                var tokenSrc = new Rectangle(0, 0, tokenLabel.Texture.Width, -tokenLabel.Texture.Height);
                Raylib.DrawBillboardRec(camera, tokenLabel.Texture, tokenSrc, p + new Vector3(0f, 0.8f, 0f), new Vector2(0.9f, 0.9f), Color.White);
            }
            return;
        }
        // The 3" seizure area as a faint ring on the felt, the marker as a short drum.
        Raylib.DrawCylinderWires(p + new Vector3(0f, 0.02f, 0f), 3f, 3f, 0.01f, 40,
            new Color(objective.Color.R, objective.Color.G, objective.Color.B, (byte)140));
        Raylib.DrawCylinder(p, 0.5f, 0.5f, 0.3f, 20, objective.Color);
        Raylib.DrawCylinderWires(p, 0.5f, 0.5f, 0.3f, 20, objective.IsPriority ? Color.Gold : Color.Black);
        if (_objectiveLabels.TryGetValue(objective.Label, out RenderTexture2D label))
        {
            var src = new Rectangle(0, 0, label.Texture.Width, -label.Texture.Height);
            Raylib.DrawBillboardRec(camera, label.Texture, src, p + new Vector3(0f, 1.1f, 0f), new Vector2(1.2f, 1.2f), Color.White);
        }
        if (objective.IsPriority)
            Raylib.DrawCylinderWires(p, 0.8f, 0.8f, 0.05f, 24, Color.Gold);
    }

    private void DrawBase(ModelDraw model)
    {
        Vector3 p = World(model.Position.x, 0f, model.Position.z);
        byte a = (byte)Math.Clamp(model.Alpha * 255f, 0f, 255f);
        var fill = new Color(model.Color.R, model.Color.G, model.Color.B, a);
        Raylib.DrawCylinder(p, model.BaseRadius, model.BaseRadius, BaseHeight, 24, fill);
        Raylib.DrawCylinderWires(p, model.BaseRadius, model.BaseRadius, BaseHeight, 24, new Color(0, 0, 0, (int)a));
        if (model.IsActive)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.2f);
            Raylib.DrawCylinderWires(p, model.BaseRadius + 0.25f, model.BaseRadius + 0.25f, 0.05f, 28,
                new Color(255, 225, 90, (int)(150 + 100 * pulse)));
        }
    }

    /// <summary>Draws every model's figure, farthest first, so the see-through edges of standees blend
    /// over what is behind them.</summary>
    private void DrawFigures(IReadOnlyList<ModelDraw> models, Camera3D camera)
    {
        EnsureCutoutShader();
        var order = models
            .OrderByDescending(m => Vector3.DistanceSquared(camera.Position, World(m.Position.x, 0f, m.Position.z)))
            .ToList();
        foreach (ModelDraw model in order) DrawFigure(model, camera);
    }

    /// <summary>
    /// How big a model's figure is, and the unit picture it shows (null = the name card). A picture keeps its
    /// own shape and stands about as tall as a real miniature on that base; the card is a fixed shape.
    /// </summary>
    private (float Width, float Height, Texture2D? Image) FigureSize(ModelDraw model)
    {
        if (_images.Get(model.Label, model.Faction) is Texture2D image)
        {
            float height = MathF.Max(1.6f, model.BaseRadius * 3.6f);
            float width = height * image.Width / MathF.Max(1f, image.Height);
            return (width, height, image);
        }
        float cardWidth = MathF.Max(1.2f, model.BaseRadius * 2.4f);
        return (cardWidth, cardWidth * CardAspect, null);
    }

    /// <summary>
    /// What stands on a model's base: the unit's picture from the UnitImages folder as an upright standee when
    /// there is one, otherwise a card with the unit's name in the owner's color. Both stay upright and turn to
    /// face the camera. This is the one place a real 3D model will plug in.
    /// </summary>
    private void DrawFigure(ModelDraw model, Camera3D camera)
    {
        var (width, height, image) = FigureSize(model);
        Vector3 p = World(model.Position.x, BaseHeight + height / 2f, model.Position.z);
        byte a = (byte)Math.Clamp(model.Alpha * 255f, 0f, 255f);
        if (image is Texture2D tex)
        {
            var src = new Rectangle(0, 0, tex.Width, tex.Height);
            if (_cutoutShader is Shader shader) Raylib.BeginShaderMode(shader);
            // Raylib widens the billboard by the picture's own shape (width = size.X x w/h), so pass the
            // height for both: the standee then keeps the picture's proportions.
            Raylib.DrawBillboardRec(camera, tex, src, p, new Vector2(height, height),
                new Color(255, 255, 255, (int)a));
            if (_cutoutShader is not null) Raylib.EndShaderMode();
            return;
        }
        if (!_cards.TryGetValue((model.UnitId, model.IsHero), out var card)) return;
        var cardSrc = new Rectangle(0, 0, card.Texture.Texture.Width, -card.Texture.Texture.Height);
        Raylib.DrawBillboardRec(camera, card.Texture.Texture, cardSrc, p, new Vector2(width, height), new Color(255, 255, 255, (int)a));
    }

    private void EnsureCutoutShader()
    {
        if (_cutoutTried) return;
        _cutoutTried = true;
        try
        {
            Shader shader = Raylib.LoadShaderFromMemory(null, CutoutFragment);
            if (shader.Id != 0 && shader.Id != Rlgl.GetShaderIdDefault()) _cutoutShader = shader;
        }
        catch
        {
            _cutoutShader = null;   // standees still draw, just without the cut-out
        }
    }

    private void DrawAttack(AttackDraw attack)
    {
        var color = attack.IsMelee ? new Color(255, 120, 80, 230) : new Color(255, 230, 120, 230);
        float lift = 1.2f;
        int count = Math.Min(attack.From.Count, attack.To.Count);
        for (int i = 0; i < count; i++)
        {
            Vector3 from = World(attack.From[i].x, lift, attack.From[i].z);
            Vector3 to = World(attack.To[i].x, lift, attack.To[i].z);
            Raylib.DrawLine3D(from, to, color);
            // A bright point travelling along the line shows which way the attack goes.
            float t = attack.Progress - MathF.Floor(attack.Progress);
            Raylib.DrawSphere(Vector3.Lerp(from, to, t), 0.18f, color);
        }
    }

    private void DrawTerrain(TerrainDraw piece)
    {
        (Color fill, Color outline) = TerrainColors.For(piece.Type);
        var see = new Color(fill.R, fill.G, fill.B, (byte)Math.Min((int)fill.A, 120));
        var edge = new Color(outline.R, outline.G, outline.B, (byte)200);
        float height = piece.HeightInches > 0.05f ? piece.HeightInches : DefaultHeight(piece.Type);
        DrawZone(piece.Shape, height, see, edge);
    }

    /// <summary>How tall terrain without a recorded height is drawn.</summary>
    private static float DefaultHeight(ETerrainType type)
    {
        if (type.HasFlag(ETerrainType.Blocking) || type.HasFlag(ETerrainType.Impassible)) return 5f;
        if (type.HasFlag(ETerrainType.Elevated)) return 3f;
        if (type.HasFlag(ETerrainType.Cover) && type.HasFlag(ETerrainType.Difficult)) return 4f;
        if (type.HasFlag(ETerrainType.Cover)) return 1.5f;
        return 0.4f;
    }

    private void DrawZone(IZone shape, float height, Color fill, Color edge)
    {
        switch (shape)
        {
            case RectangularZone r:
            {
                Vector3 c = World((r.Left + r.Right) / 2f, height / 2f, (r.Bottom + r.Top) / 2f);
                Raylib.DrawCube(c, r.Right - r.Left, height, r.Top - r.Bottom, fill);
                Raylib.DrawCubeWires(c, r.Right - r.Left, height, r.Top - r.Bottom, edge);
                break;
            }
            case CircularZone circle:
            {
                Vector3 c = World(circle.Center.X, 0f, circle.Center.Y);
                Raylib.DrawCylinder(c, circle.Radius, circle.Radius, height, 28, fill);
                Raylib.DrawCylinderWires(c, circle.Radius, circle.Radius, height, 28, edge);
                break;
            }
            case CompositeZone composite:
                foreach (IZone part in composite.Parts) DrawZone(part, height, fill, edge);
                break;
            case RotatedZoneWrapper rotated when rotated.Inner is RectangularZone rr:
            {
                // Rotate about the pivot in the table plane. Table z is flipped into world -Z, which turns a
                // counter-clockwise table angle into a positive turn about world Y.
                Vector3 pivot = World(rotated.Pivot.X, 0f, rotated.Pivot.Y);
                Vector3 local = World((rr.Left + rr.Right) / 2f, height / 2f, (rr.Bottom + rr.Top) / 2f) - pivot;
                Rlgl.PushMatrix();
                Rlgl.Translatef(pivot.X, 0f, pivot.Z);
                Rlgl.Rotatef(rotated.AngleDegrees, 0f, 1f, 0f);
                Raylib.DrawCube(local, rr.Right - rr.Left, height, rr.Top - rr.Bottom, fill);
                Raylib.DrawCubeWires(local, rr.Right - rr.Left, height, rr.Top - rr.Bottom, edge);
                Rlgl.PopMatrix();
                break;
            }
            case RotatedZoneWrapper other:
                foreach (IZone primitive in ((IZone)other).Primitives()) DrawZone(primitive, height, fill, edge);
                break;
        }
    }

    // ------------------------------------------------------------------ textures

    private void EnsureTarget(int w, int h)
    {
        if (_targetReady && _targetW == w && _targetH == h) return;
        if (_targetReady) Raylib.UnloadRenderTexture(_target3D);
        _target3D = Raylib.LoadRenderTexture(w, h);
        _targetW = w;
        _targetH = h;
        _targetReady = true;
    }

    private void EnsureCard(ModelDraw model)
    {
        string key = $"{model.Label}|{model.Color.R},{model.Color.G},{model.Color.B}|{model.IsHero}";
        if (_cards.TryGetValue((model.UnitId, model.IsHero), out var existing))
        {
            if (existing.Key == key) return;
            Raylib.UnloadRenderTexture(existing.Texture);
        }

        const int w = 256, h = 372;
        RenderTexture2D texture = Raylib.LoadRenderTexture(w, h);
        Raylib.SetTextureFilter(texture.Texture, TextureFilter.Bilinear);
        Raylib.BeginTextureMode(texture);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Color c = model.Color;
        Raylib.DrawRectangleRounded(new Rectangle(2, 2, w - 4, h - 4), 0.12f, 8, new Color(c.R, c.G, c.B, (byte)255));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(2, 2, w - 4, h - 4), 0.12f, 8, 8f, new Color(15, 15, 18, 255));
        // Darker panel for the name so it stays readable on any player color. The name gets most of the card:
        // it is what tells two units apart until custom images arrive.
        Raylib.DrawRectangle(16, 96, w - 32, h - 116, new Color(15, 15, 18, 210));
        if (model.IsHero) DrawCrown(w / 2, 52);
        DrawWrappedName(model.Label, 24, 104, w - 48, h - 132);
        Raylib.EndTextureMode();

        _cards[(model.UnitId, model.IsHero)] = (texture, key);
    }

    private static void DrawCrown(int cx, int cy)
    {
        var gold = new Color(255, 214, 80, 255);
        var dark = new Color(30, 22, 6, 255);
        // Raylib wants triangle corners counter-clockwise on screen (y down), or it culls them.
        Raylib.DrawRectangle(cx - 44, cy + 8, 88, 24, gold);
        Raylib.DrawTriangle(new Vector2(cx - 44, cy + 8), new Vector2(cx - 20, cy + 8), new Vector2(cx - 44, cy - 28), gold);
        Raylib.DrawTriangle(new Vector2(cx - 16, cy + 8), new Vector2(cx + 16, cy + 8), new Vector2(cx, cy - 36), gold);
        Raylib.DrawTriangle(new Vector2(cx + 20, cy + 8), new Vector2(cx + 44, cy + 8), new Vector2(cx + 44, cy - 28), gold);
        Raylib.DrawRectangleLinesEx(new Rectangle(cx - 44, cy + 8, 88, 24), 3f, dark);
    }

    /// <summary>The unit name, word-wrapped and shrunk until it fits the card's name panel.</summary>
    private static void DrawWrappedName(string text, int x, int y, int width, int height)
    {
        for (int size = 60; size >= 20; size -= 4)
        {
            List<string> lines = Wrap(text, size, width);
            bool tooWide = lines.Any(line => Raylib.MeasureText(line, size) > width);
            if ((lines.Count * (size + 2) > height || tooWide) && size > 20) continue;
            int ty = y + Math.Max(0, (height - lines.Count * (size + 2)) / 2);
            foreach (string line in lines.Take(Math.Max(1, height / (size + 2))))
            {
                int tw = Raylib.MeasureText(line, size);
                Raylib.DrawText(line, x + (width - tw) / 2, ty, size, Color.White);
                ty += size + 2;
            }
            return;
        }
    }

    private static List<string> Wrap(string text, int size, int width)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string trial = current.Length == 0 ? word : current + " " + word;
            if (Raylib.MeasureText(trial, size) <= width || current.Length == 0) current = trial;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private void EnsureObjectiveLabel(string label)
    {
        if (_objectiveLabels.ContainsKey(label)) return;
        const int size = 64;
        RenderTexture2D texture = Raylib.LoadRenderTexture(size, size);
        Raylib.SetTextureFilter(texture.Texture, TextureFilter.Bilinear);
        Raylib.BeginTextureMode(texture);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Raylib.DrawCircle(size / 2, size / 2, size / 2f - 2f, new Color(15, 15, 18, 220));
        int font = 40;
        int tw = Raylib.MeasureText(label, font);
        Raylib.DrawText(label, (size - tw) / 2, (size - font) / 2, font, Color.White);
        Raylib.EndTextureMode();
        _objectiveLabels[label] = texture;
    }

    /// <summary>Frees every GPU texture this view made (game exit).</summary>
    public void Unload()
    {
        foreach (var card in _cards.Values) Raylib.UnloadRenderTexture(card.Texture);
        _images.Unload();
        if (_cutoutShader is Shader cutout) Raylib.UnloadShader(cutout);
        _cutoutShader = null;
        _cutoutTried = false;
        _cards.Clear();
        foreach (RenderTexture2D label in _objectiveLabels.Values) Raylib.UnloadRenderTexture(label);
        _objectiveLabels.Clear();
        if (_targetReady) Raylib.UnloadRenderTexture(_target3D);
        _targetReady = false;
    }
}
