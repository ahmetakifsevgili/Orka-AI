using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IWikiAgent
{
    IAsyncEnumerable<string> AskQuestionStreamAsync(string wikiContent, string question);
}
