namespace Ptlk.RedisScpi.Contracts.Scpi;

public static class ScpiAccessModes
{
    public const string Readonly = "readonly";
    public const string Readwrite = "readwrite";
    public static readonly IReadOnlyList<string> All = [Readonly, Readwrite];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}

public static class ScpiDataTypes
{
    public const string Number = "number";
    public const string String = "string";
    public const string Enum = "enum";
    public static readonly IReadOnlyList<string> All = [Number, String, Enum];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}

public static class ScpiNumberTypes
{
    public const string Int = "int";
    public const string Double = "double";
    public static readonly IReadOnlyList<string> All = [Int, Double];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}

public static class ScpiStringFormats
{
    public const string Raw = "raw";
    public const string Quoted = "quoted";
    public static readonly IReadOnlyList<string> All = [Raw, Quoted];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}

public static class ScpiEnumFormats
{
    public const string Value = "value";
    public const string Code = "code";
    public static readonly IReadOnlyList<string> All = [Value, Code];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}

public static class ScpiErrorCheckModes
{
    public const string None = "none";
    public const string AfterWrite = "after-write";
    public const string AfterCommand = "after-command";
    public static readonly IReadOnlyList<string> All = [None, AfterWrite, AfterCommand];
    public static bool IsValid(string? value) => All.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}
