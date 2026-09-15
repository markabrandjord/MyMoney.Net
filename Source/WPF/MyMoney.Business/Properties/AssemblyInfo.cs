using System;

// Matches MyMoney.csproj's existing [assembly: CLSCompliant(true)] (App.xaml.cs).
// Without this, every public type here (Account, Category, etc.) is treated as
// not CLS-compliant by default when referenced from a CLS-compliant caller
// assembly, which is what produced the CS300x warning flood after this project
// was split out of MyMoney.csproj (see MyMoney.Net issue #9).
[assembly: CLSCompliant(true)]
