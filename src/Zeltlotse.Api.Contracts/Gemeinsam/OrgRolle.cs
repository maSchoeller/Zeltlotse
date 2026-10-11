using System.Text.Json.Serialization;

namespace Zeltlotse.Api.Contracts.Gemeinsam;

/// <summary>Rolle einer Person in einer Organisation (FR-5); im JSON als Name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrgRolle>))]
public enum OrgRolle
{
    OrgAdmin,
    OrgUeberblick,
}
