using Vogen;

[assembly: VogenDefaults(
    underlyingType: typeof(int),
    conversions: Conversions.Default,
    fromPrimitiveCasting: CastOperator.Explicit,
    toPrimitiveCasting: CastOperator.Explicit,
    parsableForStrings: ParsableForStrings.GenerateMethodsAndInterface,
    parsableForPrimitives: ParsableForPrimitives.HoistMethodsAndInterfaces,
    primitiveEqualityGeneration: PrimitiveEqualityGeneration.Omit,
    isInitializedMethodGeneration: IsInitializedMethodGeneration.Generate,
    tryFromGeneration: TryFromGeneration.GenerateBoolAndErrorOrMethods,
    deserializationStrictness: DeserializationStrictness.AllowValidAndKnownInstances,
    debuggerAttributes: DebuggerAttributeGeneration.Basic
)]

namespace MovieDB.Common;

[ValueObject<int>(comparison: ComparisonGeneration.Omit, stringComparers: StringComparersGeneration.Generate)]
public readonly partial struct MovieId;

[ValueObject<int>(comparison: ComparisonGeneration.Omit, stringComparers: StringComparersGeneration.Generate)]
public readonly partial struct GenreId;

[ValueObject<int>(comparison: ComparisonGeneration.Omit, stringComparers: StringComparersGeneration.Generate)]
public readonly partial struct MediumId;

/// <summary>A GUID string (users.id is VARCHAR(36)), matching the reference implementation's identity stores.</summary>
[ValueObject<string>(comparison: ComparisonGeneration.Omit, stringComparers: StringComparersGeneration.Generate)]
public readonly partial struct UserId;
