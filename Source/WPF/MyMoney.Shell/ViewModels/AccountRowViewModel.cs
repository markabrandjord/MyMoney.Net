namespace MyMoney.Shell.ViewModels;

public sealed record AccountRowViewModel(long Id, string Name, string Type, string Currency, decimal OpeningBalance);
