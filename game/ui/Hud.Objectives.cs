using System.Linq;
using Godot;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>Top right: the current objective and notifications.</summary>
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
    }

    /// <summary>Toasts for goals completed since the last frame; the demo-complete panel after the last goal.</summary>
    void CheckProgress()
    {
        if (MyState is not { } me)
            return;
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
}
