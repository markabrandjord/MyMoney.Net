using System;
using System.Runtime.CompilerServices;

// Matches MyMoney.csproj's existing [assembly: CLSCompliant(true)] (App.xaml.cs).
// Without this, every public type here (Account, Category, etc.) is treated as
// not CLS-compliant by default when referenced from a CLS-compliant caller
// assembly, which is what produced the CS300x warning flood after this project
// was split out of MyMoney.csproj (see MyMoney.Net issue #9).
[assembly: CLSCompliant(true)]

// MyMoney.Data's GetCreateTableScript derives FK constraints from the internal
// ColumnObjectMapping.ForeignKeyTable/KeyProperty added in persistence-concurrency
// phase 1 task 4 -- keep those members internal (not part of the public mapping
// API surface) rather than making them public just to cross the project boundary.
// UnitTests needs the same visibility to exercise ColumnObjectMapping directly
// in Walkabout.Data.Tests.SqlMappingTests (mirrors MyMoney.Data's own
// InternalsVisibleTo("UnitTests") added in Task 3).
[assembly: InternalsVisibleTo("MyMoney.Data")]
[assembly: InternalsVisibleTo("UnitTests")]
