using System;
using System.Collections.Generic;

namespace MyMoney.Shell.Services;

public interface IStatusService
{
    string CurrentStatus { get; }
    IReadOnlyList<ActivityEntry> Activity { get; }
    void ShowStatus(string message);
    void LogActivity(string message);
    event EventHandler? Changed;
}
