using System.Linq;
using Godot;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>Top right: the current objective, the next tech unlock, notifications; and the tech tree overlay.</summary>
public partial class Hud
{
    void RefreshGoal()
    {
        var goals = Content.Goals;
        var goal = Progression.CurrentGoal(Sim, Me);
        if (goal == null)
        {
            _goalStep.Text = "OBJECTIVES COMPLETE";
            _goalName.Text = "Demo complete";
            _goalText.Text = "Every objective is done. Keep expanding, or save from the menu.";
            _goalBar.Value = _goalBar.MaxValue;
            _goalCount.Text = "";
        }
        else
        {
            long have = System.Math.Min(Progression.Progress(Sim, Me, goal.Condition), goal.Condition.Amount);
            _goalStep.Text = $"OBJECTIVE {(MyState?.GoalsDone ?? 0) + 1} OF {goals.Count}";
            _goalName.Text = goal.Name;
            _goalText.Text = goal.Description;
            _goalBar.MaxValue = goal.Condition.Amount;
            _goalBar.Value = have;
            _goalCount.Text = $"{have} / {goal.Condition.Amount} {GameText.ConditionUnit(Content, goal.Condition)}";
        }

        var next = Content.TechList.FirstOrDefault(t => MyState?.HasTech(t.Id) != true);
        _techLine.Text = next == null
            ? "All tech unlocked."
            : $"Next unlock: {next.Name} ({ConditionText(next.Condition)}) → {GameText.UnlockNames(Content, next)}";
    }

    /// <summary>Toasts for tech and goals completed since the last frame; the demo-complete panel after the last goal.</summary>
    void CheckProgress()
    {
        if (MyState is not { } me)
            return;
        while (_seenUnlocks < me.UnlockLog.Count)
        {
            var tech = Content.Tech[me.UnlockLog[_seenUnlocks++].TechId];
            Toast($"Unlocked {tech.Name}: {GameText.UnlockNames(Content, tech)}", Ui.Good, 8,
                tech.Unlocks.FirstOrDefault(Content.Buildings.ContainsKey));
        }
        while (_seenGoals < me.GoalsDone)
        {
            var goal = Content.Goals[_seenGoals++];
            Toast($"Objective complete: {goal.Name}", Ui.Accent, 6);
            if (_seenGoals == Content.Goals.Count)
                _complete.Visible = true;
            RefreshGoal();
        }
    }

    /// <summary>A notification under the objective panel that fades out after <paramref name="seconds"/>.</summary>
    public Control Toast(string text, Color color, double seconds = 5, string? iconId = null)
    {
        var row = Ui.Row(10);
        if (iconId != null)
            row.AddChild(Ui.Icon(iconId, 32));
        row.AddChild(Ui.Label(text, Ui.FontSize, color, RightWidth - 70));
        var panel = Ui.Panel(row);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        _toasts.AddChild(panel);
        while (_toasts.GetChildCount() > 4)
            _toasts.GetChild(0).Free();
        var tween = panel.CreateTween();
        tween.TweenInterval(seconds);
        tween.TweenProperty(panel, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(panel.QueueFree));
        return panel;
    }

    /// <summary>"40/100 Iron Plate".</summary>
    string ConditionText(UnlockCondition c)
    {
        long have = System.Math.Min(Progression.Progress(Sim, Me, c), c.Amount);
        return $"{have}/{c.Amount} {GameText.ConditionUnit(Content, c)}";
    }

    void RefreshTech()
    {
        Ui.Clear(_techList);
        foreach (var tech in Content.TechList.OrderBy(t => MyState?.HasTech(t.Id) == true ? 0 : 1))
        {
            bool have = MyState?.HasTech(tech.Id) == true;
            var col = Ui.Column(4);
            var head = Ui.Row();
            head.AddChild(Ui.Label(have ? "✔" : "○", 20, have ? Ui.Good : Ui.Muted));
            head.AddChild(Ui.Label(tech.Name, 20, have ? Ui.Good : Ui.Text));
            col.AddChild(head);
            if (!have)
            {
                long n = System.Math.Min(Progression.Progress(Sim, Me, tech.Condition), tech.Condition.Amount);
                var bar = Ui.Row();
                bar.AddChild(new ProgressBar
                {
                    ShowPercentage = false, MaxValue = tech.Condition.Amount, Value = n,
                    CustomMinimumSize = new Vector2(300, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                });
                bar.AddChild(Ui.Label(ConditionText(tech.Condition), Ui.SmallFont, Ui.Muted));
                col.AddChild(bar);
            }
            var unlocks = Ui.Row(6);
            unlocks.AddChild(Ui.Label("Unlocks", Ui.SmallFont, Ui.Muted));
            foreach (var id in tech.Unlocks)
            {
                if (Content.Buildings.TryGetValue(id, out var def))
                {
                    unlocks.AddChild(Ui.Icon(id, 24));
                    unlocks.AddChild(Ui.Label(def.Name, Ui.SmallFont));
                }
                else if (Content.Recipes.TryGetValue(id, out var r))
                {
                    unlocks.AddChild(Ui.Label($"recipe: {GameText.ItemName(Content, r.Outputs.Keys.First())}", Ui.SmallFont));
                }
            }
            col.AddChild(unlocks);
            _techList.AddChild(col);
        }
        _techList.AddChild(Ui.Label("Tech unlocks by making things, not by spending points. Unlocks apply on every tile you own.",
            Ui.SmallFont, Ui.Muted, 600));
    }
}
