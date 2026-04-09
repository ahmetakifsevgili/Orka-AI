using System;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;

namespace Test;

public class Program
{
    public static async Task Main()
    {
        try 
        {
            var client = new Client("test-key");
            Console.WriteLine("Client created successfully.");
            // check if GenerateContentAsync exists
            // client.Models.GenerateContentAsync(model: "gemini-1.5-flash", contents: "test");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
