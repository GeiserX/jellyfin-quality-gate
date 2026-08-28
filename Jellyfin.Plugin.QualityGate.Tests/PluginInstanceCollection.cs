namespace Jellyfin.Plugin.QualityGate.Tests;

/// <summary>
/// The collection every test class that constructs a <see cref="Plugin"/> belongs to.
///
/// Each constructor replaces the static <c>Plugin.Instance</c>, which
/// <c>QualityGateService.GetUserPolicy</c> reads. xUnit runs separate collections in
/// parallel, so without this two classes can be mid-test at the same time and one swaps the
/// configuration the other is asserting against. Sharing one collection serialises them.
///
/// The assembly also disables parallelisation outright (see AssemblyInfo.cs). This attribute
/// is the explicit guard: it survives that setting being relaxed, and it says at each class
/// why the class cannot run beside its siblings.
/// </summary>
[CollectionDefinition(Name)]
public class PluginInstanceCollection
{
    /// <summary>The collection name, referenced from every member class.</summary>
    public const string Name = "PluginInstance";
}
