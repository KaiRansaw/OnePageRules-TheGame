using FdgRaylib.Paper;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// A paper miniature on the table (lane 5, Kai 2026-11-27: "a flat cut-out standing on the unit's round base, turned to
/// face the camera, like a real paper mini"). One quad, turned round its upright axis to face the camera (the shader
/// does it, so nothing runs per frame), standing on top of the game's base. When the camera is behind the model - the
/// model's facing is its +Z, as for 3D models - the quad shows the figure's back picture, or the front mirrored and
/// darkened when the sheet had no back.
/// <para>Materials are shared per figure (the same picture on every model that uses it).</para>
/// </summary>
public static class Standee
{
    /// <summary>The top of <see cref="TableLook.AddBase"/>'s base: the standee's feet.</summary>
    public const float BaseTop = 0.25f;

    private const string ShaderCode = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;
uniform sampler2D front_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D back_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform bool has_back = false;
uniform float alpha_cut = 0.5;
uniform vec4 tint : source_color = vec4(1.0);
varying flat float away;
varying vec3 to_cam_world;

void vertex() {
    // Turn round the upright axis to face the camera (a paper mini always shows its picture).
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 cam = INV_VIEW_MATRIX[3].xyz;
    vec3 tc = cam - origin;
    tc.y = 0.0;
    tc = length(tc) < 0.0001 ? vec3(0.0, 0.0, 1.0) : normalize(tc);
    vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), tc));
    float sx = length(MODEL_MATRIX[0].xyz);
    float sy = length(MODEL_MATRIX[1].xyz);
    vec3 world = origin + right * VERTEX.x * sx + vec3(0.0, 1.0, 0.0) * VERTEX.y * sy;
    // Behind the model (its facing is +Z): show the back.
    vec3 fwd = MODEL_MATRIX[2].xyz;
    fwd.y = 0.0;
    away = dot(fwd, tc) < 0.0 ? 1.0 : 0.0;
    to_cam_world = tc;
    VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
}

void fragment() {
    vec4 c;
    if (away > 0.5) {
        if (has_back) { c = texture(back_tex, UV); }
        else { c = texture(front_tex, vec2(1.0 - UV.x, UV.y)); c.rgb *= 0.45; }
    } else {
        c = texture(front_tex, UV);
    }
    if (c.a < alpha_cut) { discard; }
    ALBEDO = c.rgb * tint.rgb;
    ROUGHNESS = 0.92;
    SPECULAR = 0.15;
    // Lit as a card leaning a little back: facing the camera, catching the light from above.
    NORMAL = normalize((VIEW_MATRIX * vec4(normalize(to_cam_world + vec3(0.0, 0.55, 0.0)), 0.0)).xyz);
}
";

    private static Shader? _shader;
    private static readonly Dictionary<string, ShaderMaterial> Materials = new();
    private static readonly Dictionary<string, Texture2D?> Textures = new();
    private static QuadMesh? _quad;

    /// <summary>Forget loaded pictures (after the paper screens re-cut a figure).</summary>
    public static void ClearCache()
    {
        Materials.Clear();
        Textures.Clear();
    }

    public static Texture2D? Picture(string path)
    {
        if (Textures.TryGetValue(path, out var t)) return t;
        t = null;
        if (System.IO.File.Exists(path) && Image.LoadFromFile(path) is { } img)
        {
            img.GenerateMipmaps();
            t = ImageTexture.CreateFromImage(img);
        }
        return Textures[path] = t;
    }

    private static ShaderMaterial? Material(PaperLibrary.Figure f)
    {
        string key = f.FrontPath + "|" + f.BackPath;
        if (Materials.TryGetValue(key, out var m)) return m;
        if (Picture(f.FrontPath) is not { } front) return null;
        _shader ??= new Shader { Code = ShaderCode };
        m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("front_tex", front);
        Texture2D? back = f.BackPath == null ? null : Picture(f.BackPath);
        m.SetShaderParameter("back_tex", back ?? front);
        m.SetShaderParameter("has_back", back != null);
        return Materials[key] = m;
    }

    /// <summary>
    /// Stands the figure on a base already built in <paramref name="root"/> (a round base of
    /// <paramref name="baseRadius"/>"). Returns its height over the table, or 0 when the picture can't be read.
    /// </summary>
    public static float Add(Node3D root, PaperLibrary.Figure f, float baseRadius)
    {
        if (Material(f) is not { } mat) return 0;
        var (w, h) = UnitLooks.StandeeSize(baseRadius, f.Aspect);
        _quad ??= new QuadMesh { Size = new Vector2(1, 1), CenterOffset = new Vector3(0, 0.5f, 0) };
        var mi = new MeshInstance3D
        {
            Name = "Standee", Mesh = _quad, MaterialOverride = mat,
            Position = new Vector3(0, BaseTop, 0), Scale = new Vector3(w, h, 1),
            // The quad's own bounds are 1 x 1 before the shader turns it: widen them so it is never culled at the edge.
            ExtraCullMargin = Mathf.Max(w, h),
        };
        root.AddChild(mi);
        return BaseTop + h;
    }
}
