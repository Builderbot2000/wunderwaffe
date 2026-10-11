using Godot;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Combat on the viewed tile, drawn over buildings and units: wrecks (CB-006), dead unit blocks, health bars on
/// anything damaged, shots and beams in flight, shield bubbles, weapons that can't fire (CB-004), and the resource
/// radius (FB-018) of selected units and of the supplier under the cursor. Reads the sim every frame; few of these
/// exist next to buildings, so it is a plain redraw.
/// </summary>
public partial class CombatView : Node2D
{
    static readonly Color WreckFill = new(0.12f, 0.11f, 0.1f, 0.9f);
    static readonly Color WreckMark = new(0.45f, 0.4f, 0.35f);
    static readonly Color HpBack = new(0f, 0f, 0f, 0.7f);
    static readonly Color HpGood = new(0.35f, 0.9f, 0.4f);
    static readonly Color HpBad = new(0.95f, 0.3f, 0.25f);
    static readonly Color Shell = new(1f, 0.85f, 0.4f);
    static readonly Color ShieldRing = new(0.45f, 0.75f, 1f);
    static readonly Color Supply = new(0.5f, 1f, 0.6f);
    static readonly Color Warning = new(1f, 0.3f, 0.28f);

    SimHost _host = null!;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.ShowOnlyIn(this, ViewMode.Tile);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var sim = _host.Sim;
        var tile = _host.ViewedTile;
        float px = Palette.CellPx;
        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.GetTicksMsec() / 160f);

        foreach (var w in tile.Grid.Wrecks)
        {
            var r = new Rect2(w.X * px, w.Y * px, w.Width * px, w.Height * px);
            DrawRect(r.Grow(-1), WreckFill);
            DrawLine(r.Position + Vector2.One * 3, r.End - Vector2.One * 3, WreckMark, 2);
            DrawLine(new Vector2(r.Position.X + 3, r.End.Y - 3), new Vector2(r.End.X - 3, r.Position.Y + 3), WreckMark, 2);
        }

        foreach (var b in tile.Grid.Buildings)
        {
            var def = sim.Content.Buildings[b.DefId];
            if (b.Hp < def.Hp)
                HpBar(new Vector2(b.X * px, b.Y * px - 4), b.Width * px, b.Hp / (float)def.Hp);
            if (b.Kind == BuildingKind.Weapon && Combat.StatusOf(b) is WeaponStatus.NoAmmo or WeaponStatus.NoPower)
                WarnMark(new Vector2((b.X + b.Width) * px - 4, b.Y * px + 4), pulse);
            if (def.Shield is { } s && b.Combat!.Charge > 0)
                ShieldBubble(new Vector2((b.X + b.Width / 2f) * px, (b.Y + b.Height / 2f) * px), s, b.Combat.Charge);
        }

        foreach (var u in sim.Units)
        {
            if (UnitView.InView(tile, u))
                DrawUnitState(tile, u, pulse);
        }

        foreach (var p in sim.Projectiles)
        {
            var at = UnitView.PixelPos(tile, p.X, p.Y);
            if (p.Beam)
                DrawLine(UnitView.PixelPos(tile, p.FromX, p.FromY), at, Palette.Owner(p.Owner).Lightened(0.4f), 2);
            else if (!p.Done)
            {
                var back = UnitView.PixelPos(tile, p.X - p.VX * 2, p.Y - p.VY * 2);
                DrawLine(back, at, Shell with { A = 0.5f }, 2);
                DrawCircle(at, 2f, Shell);
            }
        }

        DrawSupplyRadii(tile);
    }

    void DrawUnitState(TileState tile, Unit u, float pulse)
    {
        var sim = _host.Sim;
        float px = Palette.CellPx;
        var pos = UnitView.PixelPos(tile, u.X, u.Y);
        int hp = 0, full = 0;
        var c0 = u.Cells[0];
        DrawSetTransformMatrix(new Transform2D(u.Angle * Mathf.Tau / Trig.Turn, pos));
        foreach (var b in u.Blocks)
        {
            var def = sim.Content.Buildings[b.DefId];
            hp += b.Hp;
            full += def.Hp;
            if (b.Hp > 0)
                continue;
            // A dead block: still attached, but dark and crossed out.
            var topLeft = (new Vector2(c0.OffX, c0.OffY) / Unit.CellScale + new Vector2(b.GX - c0.GX, b.GY - c0.GY) - Vector2.One / 2) * px;
            var r = new Rect2(topLeft, new Vector2(b.Width, b.Height) * px);
            DrawRect(r.Grow(-1), WreckFill);
            DrawLine(r.Position + Vector2.One * 3, r.End - Vector2.One * 3, WreckMark, 2);
        }
        DrawSetTransform(Vector2.Zero);

        float radius = u.Radius / (float)Unit.CellScale * px;
        if (hp < full)
            HpBar(pos + new Vector2(-radius, -radius - 8), radius * 2, hp / (float)full);
        if (Combat.UnitWeaponStatus(sim, u) == WeaponStatus.NoAmmo)
            WarnMark(pos + new Vector2(radius, -radius), pulse);
        for (int i = 0; i < u.Blocks.Count; i++)
        {
            if (u.Blocks[i].Hp > 0 && sim.Content.Buildings[u.Blocks[i].DefId].Shield is { } s && u.BlockCombat[i]!.Charge > 0)
                ShieldBubble(pos, s, u.BlockCombat[i]!.Charge);
        }
    }

    /// <summary>Resource radius (FB-018) of selected units, and of the core or storage under the cursor.</summary>
    void DrawSupplyRadii(TileState tile)
    {
        float px = Palette.CellPx;
        foreach (int id in _host.Selected)
        {
            if (_host.Sim.UnitById(id) is { SupplyRadius: > 0 } u)
                RadiusRing(UnitView.PixelPos(tile, u.X, u.Y), u.SupplyRadius * px);
        }
        var cell = TileView.CellAt(GetGlobalMousePosition());
        if (tile.Grid.BuildingAt(cell.X, cell.Y) is { SupplyRadius: > 0 } b)
            RadiusRing(new Vector2((b.X + b.Width / 2f) * px, (b.Y + b.Height / 2f) * px), b.SupplyRadius * px);
    }

    void RadiusRing(Vector2 centre, float radius)
    {
        DrawCircle(centre, radius, Supply with { A = 0.06f });
        DrawArc(centre, radius, 0, Mathf.Tau, 64, Supply with { A = 0.7f }, 2);
    }

    void ShieldBubble(Vector2 centre, ShieldDef s, int charge)
    {
        float radius = s.Radius * Palette.CellPx, a = 0.15f + 0.6f * charge / s.Capacity;
        DrawCircle(centre, radius, ShieldRing with { A = a * 0.15f });
        DrawArc(centre, radius, 0, Mathf.Tau, 64, ShieldRing with { A = a }, 2);
    }

    void HpBar(Vector2 at, float width, float share)
    {
        DrawRect(new Rect2(at, new Vector2(width, 3)), HpBack);
        DrawRect(new Rect2(at, new Vector2(width * Mathf.Clamp(share, 0, 1), 3)), HpBad.Lerp(HpGood, share));
    }

    void WarnMark(Vector2 at, float pulse)
    {
        DrawCircle(at, 5f, Colors.Black);
        DrawCircle(at, 4f, Warning with { A = pulse });
        DrawString(ThemeDB.FallbackFont, at + new Vector2(-2, 4), "!", HorizontalAlignment.Left, -1, 10, Colors.White);
    }
}
