using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

/// <summary>
/// Bu controller sadece yazdığımız EdgeTTS + WebM OPUS mimarisinin
/// Swagger üzerinden test edilebilmesi için açılmış geçici bir test aracıdır.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestTtsController : ControllerBase
{
    private readonly ITtsStreamService _tts;

    public TestTtsController(ITtsStreamService tts)
    {
        _tts = tts;
    }

    [HttpGet("generate")]
    [Produces("audio/mpeg")]
    public async Task GenerateAudio([FromQuery] string text = "Merhaba, ben Albert. Yazdığımız sesli sınıf mimarisinin ilk ve mükemmel testi budur. Lütfen sesi dinleyiniz.")
    {
         if (string.IsNullOrWhiteSpace(text))
         {
             Response.StatusCode = 400;
             Response.ContentType = "text/plain";
             await Response.WriteAsync("Error: 'text' parametresi bos olamaz.");
             return;
         }
         
         Response.ContentType = "audio/mpeg";
         
         try
         {
             await foreach(var chunk in _tts.GetAudioStreamAsync(text, HttpContext.RequestAborted))
             {
                  await Response.Body.WriteAsync(chunk, HttpContext.RequestAborted);
                  await Response.Body.FlushAsync(HttpContext.RequestAborted);
             }
         }
         catch (System.Exception ex)
         {
             // If headers haven't been sent yet, we can change the status code
             if (!Response.HasStarted)
             {
                 Response.StatusCode = 500;
                 Response.ContentType = "text/plain";
                 await Response.WriteAsync($"Error: {ex.Message}\nStack: {ex.StackTrace}");
             }
         }
    }
}
