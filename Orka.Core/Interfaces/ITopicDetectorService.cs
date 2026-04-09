using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface ITopicDetectorService
{
    bool IsNewTopic(string message);
    Task<string> ExtractTopicNameAsync(string message);
}
