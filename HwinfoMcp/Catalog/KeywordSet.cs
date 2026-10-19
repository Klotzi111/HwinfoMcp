using System.Text.RegularExpressions;

namespace HwinfoMcp.Catalog;

/// <summary>
/// A list of keywords that match case-insensitively at the start of a word anywhere in a text, so
/// "throttl" matches "Thermal Throttling (HTC)" and "dram" matches "DRAM Read Bandwidth", but "imc"
/// doesn't match inside another word.
/// </summary>
public sealed class KeywordSet
{
	private readonly Regex _regex;

	/// <summary>Creates a keyword set.</summary>
	/// <param name="keywords">The keywords, matched literally.</param>
	public KeywordSet(params string[] keywords)
	{
		Keywords = keywords;
		var alternatives = string.Join('|', keywords.Select(Regex.Escape));
		_regex = new Regex($@"\b(?:{alternatives})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	/// <summary>The keywords of this set.</summary>
	public IReadOnlyList<string> Keywords { get; }

	/// <summary>Returns whether any keyword occurs in <paramref name="text"/>.</summary>
	public bool IsMatch(string? text)
	{
		return !string.IsNullOrEmpty(text) && _regex.IsMatch(text);
	}
}
