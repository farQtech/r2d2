using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Adapters.Twilio;

namespace r2d2.Controllers
{
    [Route("api/twilio")]
    [ApiController]
    public class TwilioController : ControllerBase
    {
        private readonly TwilioAdapter _adapter;
        private readonly IBot _bot;

        public TwilioController(TwilioAdapter adapter, IBot bot)
        {
            _adapter = adapter;
            _bot = bot;
        }

        [HttpPost]
        [HttpGet]
        public async Task PostAsync()
        {
            // Delegate the processing of the HTTP POST to the adapter.
            // The adapter will invoke the bot.
            try
            {
                await _adapter.ProcessAsync(Request, Response, _bot, default);
            } catch (Exception ex){
                Console.Write(ex);
            }
        }
    }
}