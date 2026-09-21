using System;
using System.Collections.Generic;

namespace MyMoney.Shell.Services;

public sealed class StatusService : IStatusService
{
    private readonly List<ActivityEntry> activity = new();

    public string CurrentStatus { get; private set; } = "Ready";

    public IReadOnlyList<ActivityEntry> Activity => this.activity;

    public event EventHandler? Changed;

    public void ShowStatus(string message)
    {
        this.CurrentStatus = message;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void LogActivity(string message)
    {
        this.activity.Insert(0, new ActivityEntry(DateTime.Now, message));
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
