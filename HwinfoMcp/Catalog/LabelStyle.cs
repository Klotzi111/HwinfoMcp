namespace HwinfoMcp.Catalog;

/// <summary>Which of HWiNFO's two names for sensor groups and readings the tools report.</summary>
public enum LabelStyle
{
	/// <summary>
	/// The original English names. Stable across HWiNFO's language settings and unambiguous: HWiNFO's
	/// translations sometimes give different readings the same name.
	/// </summary>
	Original,

	/// <summary>The names as displayed in HWiNFO: translated, and renamed by the user where they did.</summary>
	User,
}
