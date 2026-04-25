using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orka.Infrastructure.SemanticKernel.Audio;

public class ParsedPodcastChunk
{
    public string Text { get; set; } = string.Empty; // TTS metni
    public string RawChunk { get; set; } = string.Empty; // UI metni (bozulmamış)
    public Orka.Core.Interfaces.TtsVoice Voice { get; set; }
}

public static class VoicePodcastParser
{
    private static readonly Regex TagRegex = new Regex(
        @"(\[HOCA\]:?|\*\*Hoca\*\*:?|\[ASİSTAN\]:?|\[ASISTAN\]:?|\*\*Asistan\*\*:?|\[IDE_OPEN\]|\[REMEDIAL_OFFER\]|\[PLAN_READY\]|\[TOPIC_COMPLETE:[^\]]+\])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Filter for Mermaid blocks and Images
    private static readonly Regex MarkdownImageRegex = new Regex(@"\!\[[^\]]*\]\([^\)]+\)", RegexOptions.Compiled);
    private static readonly Regex MermaidRegex = new Regex(@"```mermaid[\s\S]*?(?:```|$)", RegexOptions.Compiled);


    public static async IAsyncEnumerable<ParsedPodcastChunk> ParseStreamAsync(
        IAsyncEnumerable<string> rawStream, 
        [EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        var currentVoice = Orka.Core.Interfaces.TtsVoice.Hoca;
        string buffer = "";
        
        // State Machine for filtering TTS
        bool inMermaidBlock = false;
        bool inImageTag = false;

        await foreach (var chunk in rawStream.WithCancellation(ct))
        {
            string emittedRaw = chunk; 
            buffer += chunk;
            
            int unclosedBracket = buffer.LastIndexOf('[');
            int unclosedAsterisk = buffer.LastIndexOf("**");
            
            bool mightBeInTag = false;
            // Handle speaker tags buffering
            if (unclosedBracket != -1)
            {
                int closedBracket = buffer.IndexOf(']', unclosedBracket);
                if (closedBracket == -1) mightBeInTag = true; // Wait for ]
            }
            if (unclosedAsterisk != -1 && buffer.Length - unclosedAsterisk < 12 && unclosedAsterisk == buffer.IndexOf("**", unclosedAsterisk + 1)) 
            {
                mightBeInTag = true;
            }

            var parsedChunkToYieldUI = new ParsedPodcastChunk
            {
                Text = "",
                RawChunk = emittedRaw,
                Voice = currentVoice 
            };

            if (!mightBeInTag)
            {
                var matches = TagRegex.Matches(buffer);
                int lastIndex = 0;

                foreach (Match match in matches)
                {
                    string textBeforeTag = buffer.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrWhiteSpace(textBeforeTag))
                    {
                        yield return new ParsedPodcastChunk
                        {
                            Text = textBeforeTag,
                            RawChunk = "",
                            Voice = currentVoice
                        };
                    }

                    string tag = match.Value.ToUpper();
                    if (tag.Contains("HOCA")) currentVoice = Orka.Core.Interfaces.TtsVoice.Hoca;
                    else if (tag.Contains("ASISTAN") || tag.Contains("ASİSTAN")) currentVoice = Orka.Core.Interfaces.TtsVoice.Asistan;

                    lastIndex = match.Index + match.Length;
                }

                string remainingText = buffer.Substring(lastIndex);
                if (!string.IsNullOrEmpty(remainingText))
                {
                    parsedChunkToYieldUI.Text = remainingText;
                }
                
                buffer = "";
            }

            // FILTERING TTS OUTPUT (parsedChunkToYieldUI.Text)
            if (!string.IsNullOrEmpty(parsedChunkToYieldUI.Text))
            {
                string ttsText = parsedChunkToYieldUI.Text;
                
                // Extremely simple stream state filter
                // Note: We'll filter the emitted Text based on character iteration to handle stream chunks
                var filteredTts = new System.Text.StringBuilder();
                for (int i = 0; i < ttsText.Length; i++)
                {
                    char c = ttsText[i];

                    // Mermaid Check
                    if (!inMermaidBlock && i + 10 <= ttsText.Length && ttsText.Substring(i, 10).ToLower() == "```mermaid")
                    {
                        inMermaidBlock = true;
                    }
                    else if (inMermaidBlock && i + 3 <= ttsText.Length && ttsText.Substring(i, 3) == "```")
                    {
                        inMermaidBlock = false;
                        i += 2; // skip
                        continue;
                    }

                    // Image Check
                    if (!inMermaidBlock && !inImageTag && i + 2 <= ttsText.Length && ttsText.Substring(i, 2) == "![")
                    {
                        inImageTag = true;
                    }
                    else if (inImageTag && c == ')') // naive closing
                    {
                        inImageTag = false;
                        continue;
                    }

                    if (!inMermaidBlock && !inImageTag)
                    {
                        filteredTts.Append(c);
                    }
                }
                parsedChunkToYieldUI.Text = filteredTts.ToString();
            }

            yield return parsedChunkToYieldUI;
        }
        
        if (buffer.Length > 0)
        {
            var cleanText = TagRegex.Replace(buffer, "");
            if(!string.IsNullOrEmpty(cleanText))
            {
                bool omit = inMermaidBlock || inImageTag; // naive discard if ended unclosed
                yield return new ParsedPodcastChunk
                {
                    Text = omit ? "" : cleanText,
                    RawChunk = "", 
                    Voice = currentVoice
                };
            }
        }
    }
}
