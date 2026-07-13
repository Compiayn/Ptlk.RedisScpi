using System.Text.RegularExpressions;
using Ptlk.RedisScpi.Contracts.Scpi;
using Ptlk.RedisScpi.Models;

namespace Ptlk.RedisScpi.Services.Scpi;

public sealed class ScpiTemplateRenderer
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.Compiled);

    public string RenderRead(ScpiPointConfig point)
    {
        ValidateTemplate(point.ReadTemplate, allowValue: false, requireValue: false);
        return Render(point.ReadTemplate, point.Name, value: null);
    }

    public string RenderWrite(ScpiPointConfig point, string value)
    {
        if (!ScpiAccessModes.Readwrite.Equals(point.Access, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScpiValidationException(ScpiErrorCodes.PointReadonly, $"Point '{point.SourcePath}' is readonly.");
        }

        ValidateTemplate(point.WriteTemplate, allowValue: true, requireValue: true);
        return Render(point.WriteTemplate!, point.Name, value);
    }

    public void ValidateReadTemplate(string? template) =>
        ValidateTemplate(template, allowValue: false, requireValue: false);

    public void ValidateWriteTemplate(string? template) =>
        ValidateTemplate(template, allowValue: true, requireValue: true);

    private static void ValidateTemplate(string? template, bool allowValue, bool requireValue)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ScpiTemplateException("SCPI template must not be empty.");
        }

        var placeholders = PlaceholderPattern.Matches(template)
            .Select(match => match.Groups["name"].Value)
            .ToList();
        foreach (var placeholder in placeholders)
        {
            if (!placeholder.Equals("name", StringComparison.Ordinal)
                && !(allowValue && placeholder.Equals("value", StringComparison.Ordinal)))
            {
                throw new ScpiTemplateException($"Unsupported SCPI template variable '{{{placeholder}}}'.");
            }
        }

        if (!allowValue && placeholders.Contains("value", StringComparer.Ordinal))
        {
            throw new ScpiTemplateException("ReadTemplate must not contain {value}.");
        }

        if (requireValue && !placeholders.Contains("value", StringComparer.Ordinal))
        {
            throw new ScpiTemplateException("WriteTemplate must contain {value}.");
        }
    }

    private static string Render(string template, string name, string? value)
    {
        var rendered = template.Replace("{name}", name, StringComparison.Ordinal);
        if (value is not null)
        {
            rendered = rendered.Replace("{value}", value, StringComparison.Ordinal);
        }

        if (PlaceholderPattern.IsMatch(rendered) || string.IsNullOrWhiteSpace(rendered))
        {
            throw new ScpiTemplateException("SCPI template could not be expanded to a non-empty command.");
        }

        return rendered.Trim();
    }
}
