using FdgRaylib.Models3D;
using Godot;

namespace OprGodot.Lab;

/// <summary>
/// The colour painter (Kai, 2026-11-15: level 1 - colour swaps, per model, seen by the opponents). Repaints a model
/// on screen: every part whose colour is close to a swap's "from" colour is redrawn in its "to" colour, keeping the
/// shading painted into the texture (a darker red fold becomes a darker blue fold).
///
/// <para>Done with a small shader that wraps each surface's own texture, so nothing on disk changes and the same
/// downloaded model can stand painted differently a hundred times. The swaps themselves are text on the model
/// (<see cref="ModelPaint"/>), which is what travels to the other players.</para>
/// </summary>
public static class ModelPainter
{
    private const string ShaderCode = @"
shader_type spatial;
render_mode cull_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool use_normal = false;
uniform float roughness = 0.8;
uniform float metallic = 0.0;
uniform float alpha_cut = 0.0;
uniform vec3 from_c[4];
uniform vec3 to_c[4];
uniform int count = 0;

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}
vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}
void fragment() {
    vec4 t = texture(albedo_tex, UV) * albedo_color;
    vec3 c = t.rgb;
    vec3 hc = rgb2hsv(c);
    vec3 outc = c;
    float best = 0.0;
    for (int i = 0; i < 4; i++) {
        if (i >= count) break;
        vec3 hf = rgb2hsv(from_c[i]);
        float dh = abs(hc.x - hf.x);
        dh = min(dh, 1.0 - dh);
        // A coloured source is matched mostly on hue; a grey/black/white one on saturation and brightness.
        float coloured = step(0.15, hf.y) * step(0.12, hc.y);
        float d = mix(abs(hc.y - hf.y) * 1.5 + abs(hc.z - hf.z) * 1.2,
                      dh * 4.0 + abs(hc.y - hf.y) * 0.8 + abs(hc.z - hf.z) * 0.5, coloured);
        float w = 1.0 - smoothstep(0.16, 0.34, d);
        if (w > best) {
            best = w;
            vec3 ht = rgb2hsv(to_c[i]);
            float v = clamp(hc.z * (ht.z / max(hf.z, 0.06)), 0.0, 1.0);
            float s = clamp(hf.y > 0.08 ? ht.y * (hc.y / hf.y) : ht.y, 0.0, 1.0);
            outc = mix(c, hsv2rgb(vec3(ht.x, s, v)), w);
        }
    }
    ALBEDO = outc;
    ROUGHNESS = roughness;
    METALLIC = metallic;
    if (use_normal) NORMAL_MAP = texture(normal_tex, UV).rgb;
    if (alpha_cut > 0.0 && t.a < alpha_cut) discard;
}";

    private static Shader? _shader;
    private static Shader PaintShader => _shader ??= new Shader { Code = ShaderCode };
    private static ImageTexture? _white;
    private static Texture2D White
    {
        get
        {
            if (_white != null) return _white;
            Image img = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
            img.Fill(Colors.White);
            return _white = ImageTexture.CreateFromImage(img);
        }
    }

    /// <summary>Repaints every surface under <paramref name="root"/> with the model's swaps. No swaps: left alone.</summary>
    private static readonly Dictionary<(ulong Source, string Paint), ShaderMaterial> Painted = new();

    public static void Apply(Node3D root, string? paint)
    {
        List<ColourSwap> swaps = ModelPaint.Parse(paint);
        if (swaps.Count == 0) return;
        var from = new Vector3[4];
        var to = new Vector3[4];
        for (int i = 0; i < swaps.Count; i++) { from[i] = Vec(swaps[i].From); to[i] = Vec(swaps[i].To); }

        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(root))
        {
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                Material? m = mi.GetActiveMaterial(s);
                // Perf sweep (2026-11-15): every painted model of a squad shares one material per original material
                // and paint, instead of a new one per model (10 models = 10 identical materials before).
                var key = (m?.GetInstanceId() ?? 0UL, paint!);
                if (Painted.TryGetValue(key, out ShaderMaterial? shared) && GodotObject.IsInstanceValid(shared)
                    && (m == null || GodotObject.IsInstanceValid(m)))
                {
                    mi.SetSurfaceOverrideMaterial(s, shared);
                    continue;
                }
                var sm = new ShaderMaterial { Shader = PaintShader };
                Painted[key] = sm;
                if (m is BaseMaterial3D bm)
                {
                    sm.SetShaderParameter("albedo_tex", bm.AlbedoTexture ?? White);
                    sm.SetShaderParameter("albedo_color", bm.AlbedoColor);
                    sm.SetShaderParameter("roughness", bm.Roughness);
                    sm.SetShaderParameter("metallic", bm.Metallic);
                    if (bm.NormalEnabled && bm.NormalTexture != null)
                    {
                        sm.SetShaderParameter("normal_tex", bm.NormalTexture);
                        sm.SetShaderParameter("use_normal", true);
                    }
                    if (bm.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
                        sm.SetShaderParameter("alpha_cut", Mathf.Max(0.3f, bm.AlphaScissorThreshold));
                }
                else
                {
                    sm.SetShaderParameter("albedo_tex", White);
                }
                sm.SetShaderParameter("from_c", from);
                sm.SetShaderParameter("to_c", to);
                sm.SetShaderParameter("count", swaps.Count);
                mi.SetSurfaceOverrideMaterial(s, sm);
            }
        }
    }

    private static Vector3 Vec(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    public static int Rgb(Color c) => ((int)Mathf.Round(c.R * 255) << 16) | ((int)Mathf.Round(c.G * 255) << 8) | (int)Mathf.Round(c.B * 255);

    public static Color ColorOf(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    /// <summary>
    /// The model's main colours, biggest first - what the painter offers to repaint. Read from its textures: pixels
    /// are grouped by hue (or, for greys, by brightness), tiny groups dropped, each group shown as its average colour.
    /// </summary>
    public static List<(Color Colour, float Share)> MainColours(Node3D root, int max = 8)
    {
        var bins = new Dictionary<int, (Vector3 Sum, int N)>();
        int total = 0;
        foreach (MeshInstance3D mi in AssetLoader.All<MeshInstance3D>(root))
        {
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.GetActiveMaterial(s) is not BaseMaterial3D bm) continue;
                Color tint = bm.AlbedoColor;
                Image? img = null;
                try { img = bm.AlbedoTexture?.GetImage(); } catch (Exception) { }
                if (img == null) { Add(tint, 400); continue; }
                if (img.IsCompressed()) img.Decompress();
                int w = img.GetWidth(), h = img.GetHeight();
                int step = Math.Max(1, (int)Math.Sqrt(w * h / 6000.0));
                for (int y = 0; y < h; y += step)
                    for (int x = 0; x < w; x += step)
                    {
                        Color p = img.GetPixel(x, y);
                        if (p.A < 0.5f) continue;
                        Add(p * tint, 1);
                    }
            }
        }

        void Add(Color c, int n)
        {
            c.ToHsv(out float hue, out float sat, out float val);
            int key = sat < 0.15f || val < 0.12f
                ? 1000 + (int)(val * 5)                       // greys: by brightness
                : (int)(hue * 18) % 18 * 10 + (val < 0.45f ? 0 : 1) * 3 + (sat < 0.5f ? 0 : 1);
            bins[key] = bins.TryGetValue(key, out var b) ? (b.Sum + new Vector3(c.R, c.G, c.B) * n, b.N + n) : (new Vector3(c.R, c.G, c.B) * n, n);
            total += n;
        }

        if (total == 0) return new();
        return bins.Values.OrderByDescending(b => b.N)
            .Where(b => b.N / (float)total >= 0.02f)
            .Take(max)
            .Select(b => { Vector3 a = b.Sum / b.N; return (new Color(a.X, a.Y, a.Z), b.N / (float)total); })
            .ToList();
    }
}
