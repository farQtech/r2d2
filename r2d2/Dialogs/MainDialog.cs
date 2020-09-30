using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Adapters.Twilio;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;

using r2d2.CognitiveModels;
using Twilio;
using Twilio.Rest.Authy.V1;
using Twilio.Rest.Verify.V2.Service;

namespace r2d2.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        private readonly FlightBookingRecognizer _luisRecognizer;
        protected readonly ILogger Logger;
        private Dictionary<string, string> UserDb = new Dictionary<string, string>()
        {
            { "+917385500302", "ikram" },
            { "+918805844472", "sohel" },
            { "+918805844471", "sohel" }
        };

        // Dependency injection uses this constructor to instantiate MainDialog
        public MainDialog(FlightBookingRecognizer luisRecognizer, RedditDialog redditDialog, ILogger<MainDialog> logger)
            : base(nameof(MainDialog))
        {
            _luisRecognizer = luisRecognizer;
            Logger = logger;

            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(redditDialog);
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                IntroStepAsync,
                SendAuthTokenAsync,
                VerifyAuthTokenAsync,
                ActStepAsync,
                FinalStepAsync,
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!_luisRecognizer.IsConfigured)
            {
                //await stepContext.Context.SendActivityAsync(
                //    MessageFactory.Text("NOTE: LUIS is not configured. To enable all capabilities, add 'LuisAppId', 'LuisAPIKey' and 'LuisAPIHostName' to the appsettings.json file.", inputHint: InputHints.IgnoringInput), cancellationToken);

                return await stepContext.NextAsync(null, cancellationToken);
            }

            // Use the text provided in FinalStepAsync or the default if it is the first time.
            var messageText = stepContext.Options?.ToString() ?? "What can I help you with today?\nSay something like \"Book a flight from Paris to Berlin on March 22, 2020\"";
            var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);
            return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
        }

        private async Task<DialogTurnResult> SendAuthTokenAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Context.Activity.ChannelId == "twilio-sms")
            {
                
                string whatsAppNumber = ((TwilioMessage)stepContext.Context.Activity.ChannelData).From.Trim();
                var number = whatsAppNumber.Remove(0, whatsAppNumber.IndexOf("+"));
                const string accSid = "ACac6c0bd924618e933d565e00bd14e9b0";
                const string accToken = "094246d6291575fb2746e9f95b822375";
                const string serviceSid = "VA9cdb962bee2ed8a3241958634c0adf0f";

                SendVerificationCode(accSid, accToken, serviceSid, number);

                var promptMessage = MessageFactory.Text("Please enter verification code", "Please enter verification code", InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> VerifyAuthTokenAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Context.Activity.ChannelId == "twilio-sms")
            {
                string whatsAppNumber = ((TwilioMessage)stepContext.Context.Activity.ChannelData).From.Trim();
                var number = whatsAppNumber.Remove(0, whatsAppNumber.IndexOf("+"));
                var code = (string)stepContext.Result;
                const string accSid = "ACac6c0bd924618e933d565e00bd14e9b0";
                const string accToken = "094246d6291575fb2746e9f95b822375";
                const string serviceSid = "VA9cdb962bee2ed8a3241958634c0adf0f";

               if(VerifyUserToken(accSid, accToken, serviceSid, code, number))
                {
                    var user = UserDb.GetValueOrDefault(number);
                    await stepContext.Context.SendActivityAsync(
                            MessageFactory.Text("Verified as " + user, inputHint: InputHints.IgnoringInput), cancellationToken);
                    return await stepContext.NextAsync(user, cancellationToken);
                }

                await stepContext.Context.SendActivityAsync(
                            MessageFactory.Text("Verification Failed", inputHint: InputHints.IgnoringInput), cancellationToken);
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ActStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (!_luisRecognizer.IsConfigured)
            {
                // LUIS is not configured, we just run the BookingDialog path with an empty BookingDetailsInstance.
                return await stepContext.BeginDialogAsync(nameof(RedditDialog), new RedditSearch(), cancellationToken);
            }

            // Call LUIS and gather any potential booking details. (Note the TurnContext has the response to the prompt.)
            var luisResult = await _luisRecognizer.RecognizeAsync<FlightBooking>(stepContext.Context, cancellationToken);
            switch (luisResult.TopIntent().intent)
            {
                case FlightBooking.Intent.BookFlight:
                    await ShowWarningForUnsupportedCities(stepContext.Context, luisResult, cancellationToken);

                    // Initialize BookingDetails with any entities we may have found in the response.
                    //////IKRAM
                    //////var bookingDetails = new RedditSearch()
                    //////{
                    //////    // Get destination and origin from the composite entities arrays.
                    //////    Interest = luisResult.ToEntities.Airport,
                    //////    SubscribersCount = luisResult.FromEntities.Airport,
                    //////    IsPublic = luisResult.TravelDate,
                    //////};

                    // Run the BookingDialog giving it whatever details we have from the LUIS call, it will fill out the remainder.
                    //////return await stepContext.BeginDialogAsync(nameof(BookingDialog), bookingDetails, cancellationToken);
                    break;
                case FlightBooking.Intent.GetWeather:
                    // We haven't implemented the GetWeatherDialog so we just display a TODO message.
                    var getWeatherMessageText = "TODO: get weather flow here";
                    var getWeatherMessage = MessageFactory.Text(getWeatherMessageText, getWeatherMessageText, InputHints.IgnoringInput);
                    await stepContext.Context.SendActivityAsync(getWeatherMessage, cancellationToken);
                    break;

                default:
                    // Catch all for unhandled intents
                    var didntUnderstandMessageText = $"Sorry, I didn't get that. Please try asking in a different way (intent was {luisResult.TopIntent().intent})";
                    var didntUnderstandMessage = MessageFactory.Text(didntUnderstandMessageText, didntUnderstandMessageText, InputHints.IgnoringInput);
                    await stepContext.Context.SendActivityAsync(didntUnderstandMessage, cancellationToken);
                    break;
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        // Shows a warning if the requested From or To cities are recognized as entities but they are not in the Airport entity list.
        // In some cases LUIS will recognize the From and To composite entities as a valid cities but the From and To Airport values
        // will be empty if those entity values can't be mapped to a canonical item in the Airport.
        private static async Task ShowWarningForUnsupportedCities(ITurnContext context, FlightBooking luisResult, CancellationToken cancellationToken)
        {
            var unsupportedCities = new List<string>();

            var fromEntities = luisResult.FromEntities;
            if (!string.IsNullOrEmpty(fromEntities.From) && string.IsNullOrEmpty(fromEntities.Airport))
            {
                unsupportedCities.Add(fromEntities.From);
            }

            var toEntities = luisResult.ToEntities;
            if (!string.IsNullOrEmpty(toEntities.To) && string.IsNullOrEmpty(toEntities.Airport))
            {
                unsupportedCities.Add(toEntities.To);
            }

            if (unsupportedCities.Any())
            {
                var messageText = $"Sorry but the following airports are not supported: {string.Join(',', unsupportedCities)}";
                var message = MessageFactory.Text(messageText, messageText, InputHints.IgnoringInput);
                await context.SendActivityAsync(message, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // If the child dialog ("BookingDialog") was cancelled, the user failed to confirm or if the intent wasn't BookFlight
            // the Result here will be null.
            ////if (stepContext.Result is RedditSearch result)
            ////{
            ////    // Now we have all the booking details call the booking service.

            ////    // If the call to the booking service was successful tell the user.
            ////    //////result.IsPublic
            ////    var timeProperty = new TimexProperty();
            ////    var travelDateMsg = timeProperty.ToNaturalLanguage(DateTime.Now);
            ////    var messageText = $"I have you booked to {result.Interest} from {result.SubscribersCount} on {travelDateMsg}";
            ////    var message = MessageFactory.Text(messageText, messageText, InputHints.IgnoringInput);
            ////    await stepContext.Context.SendActivityAsync(message, cancellationToken);
            ////}

            // Restart the main dialog with a different message the second time around
            var promptMessage = "What else can I do for you?";
            return await stepContext.ReplaceDialogAsync(InitialDialogId, promptMessage, cancellationToken);
        }

         public void SendVerificationCode(string accSid, string accToken,  string serviceSid, string number)
        {
             string accountSid = accSid;
             string authToken = accToken;

            TwilioClient.Init(accountSid, authToken);

            // var service = ServiceResource.Create(friendlyName: "r2d2 Verify Service");

            var verification = VerificationResource.Create(
            to: number,
            channel: "sms",
            pathServiceSid: serviceSid
        );

            Console.WriteLine(verification.Status);
        }

        public bool VerifyUserToken(string accSid, string accToken, string serviceSid, string code, string number)
        {
             string accountSid = accSid;
             string authToken = accToken;

            TwilioClient.Init(accountSid, authToken);

            var verificationCheck = VerificationCheckResource.Create(
                to: number,
                code: code,
                pathServiceSid: serviceSid
            );

            return verificationCheck.Status == "approved";
        }
    }
}
