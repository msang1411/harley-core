using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Common.Authentication;

/// <summary>
/// Settings a downstream service needs in order to validate tokens issued by Identity.
/// Bound from the "Jwt" configuration section.
/// </summary>
public sealed class JwtValidationOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";

    [Required, Url] public string MetadataAddress { get; set; } = "";

    public bool RequireHttpsMetadata { get; set; } = true;

    [Range(0, 300)] public int ClockSkewSeconds { get; set; } = 30;
}