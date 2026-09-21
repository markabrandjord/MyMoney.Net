namespace MyMoney.Shell.Services;

public readonly record struct RouteId(string Value)
{
    public static RouteId Accounts { get; } = new("accounts");

    public override string ToString() => this.Value;
}
