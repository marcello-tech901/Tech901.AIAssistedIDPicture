namespace Tech901.IdPhoto.Core.Helpers;

/// <summary>
/// Constructs well-formed SSML documents for Azure Speech Service synthesis.
/// </summary>
/// <remarks>
/// AI-102: SSML (Speech Synthesis Markup Language) is an XML-based markup language that
/// provides fine-grained control over speech synthesis. Key elements:
/// <list type="bullet">
///   <item><c>&lt;prosody&gt;</c> — controls rate, pitch, and volume</item>
///   <item><c>&lt;break&gt;</c> — inserts pauses (e.g., <c>time="500ms"</c>)</item>
///   <item><c>&lt;emphasis&gt;</c> — adds stress (levels: reduced, moderate, strong)</item>
///   <item><c>&lt;phoneme&gt;</c> — overrides pronunciation with IPA or SAPI</item>
///   <item><c>&lt;say-as&gt;</c> — controls how text is interpreted (date, number, etc.)</item>
/// </list>
/// </remarks>
// TODO DEMO-22: AI-102 — SSML document structure. <speak> root declares version and xmlns. <voice> selects the neural voice. Inner elements control prosody, breaks, emphasis.
public static class SsmlBuilder
{
    /// <summary>
    /// Wraps SSML body content in a complete, well-formed SSML document.
    /// </summary>
    /// <param name="voiceName">Neural voice short name (e.g., "en-US-JennyNeural").</param>
    /// <param name="body">SSML body (may contain prosody, break, emphasis elements).</param>
    public static string Build(string voiceName, string body) =>
        $"""
        <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
            <voice name="{voiceName}">
                {body}
            </voice>
        </speak>
        """;
}
