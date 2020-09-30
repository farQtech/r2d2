using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;

namespace r2d2.Dialogs
{
    public class RedditDialog : CancelAndHelpDialog
    {
        private const string InterestStepMsgText = "What is your interest?";
        private const string SubscriberStepMsgText = "How many subscribers it must have?";

        public RedditDialog()
            : base(nameof(RedditDialog))
        {
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(nameof(NumberPrompt<long>), SubscriberPromptValidator));
            AddDialog(new ConfirmPrompt(nameof(ConfirmPrompt)));
            AddDialog(new DateResolverDialog());
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                InterestStepAsync,
                SubscriberStepAsync,
                IsPublicStepAsync,
                ConfirmStepAsync,
                FinalStepAsync,
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> InterestStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var redditSearch = (RedditSearch)stepContext.Options;

            if (redditSearch.Interest == null)
            {
                var promptMessage = MessageFactory.Text(InterestStepMsgText, InterestStepMsgText, InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }

            return await stepContext.NextAsync(redditSearch.Interest, cancellationToken);
        }

        private async Task<DialogTurnResult> SubscriberStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var redditSearch = (RedditSearch)stepContext.Options;

            redditSearch.Interest = (string)stepContext.Result;

            if (redditSearch.SubscribersCount == default(long))
            {
                var promptMessage = MessageFactory.Text(SubscriberStepMsgText, SubscriberStepMsgText, InputHints.ExpectingInput);
                var repromptMessage = MessageFactory.Text("Please enter value in numbers!, Try again!!", "Please enter value in numbers!, Try again!!", InputHints.ExpectingInput);
                return await stepContext.PromptAsync(nameof(NumberPrompt<long>), 
                    new PromptOptions { 
                        Prompt = promptMessage,
                        RetryPrompt = repromptMessage,
                    }, 
                    cancellationToken);
            }

            return await stepContext.NextAsync(redditSearch.SubscribersCount, cancellationToken);
        }

        private async Task<DialogTurnResult> IsPublicStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var redditSearch = (RedditSearch)stepContext.Options;

            redditSearch.SubscribersCount = Convert.ToInt64(stepContext.Result);

            if (redditSearch.IsPublic == false)
            {
                var messageText = $"Should this be a public subreddit?";
                var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);

                return await stepContext.PromptAsync(nameof(ConfirmPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);

                //  return await stepContext.BeginDialogAsync(nameof(DateResolverDialog), bookingDetails.IsPublic, cancellationToken);
            }

            return await stepContext.NextAsync(redditSearch.IsPublic, cancellationToken);
        }

        private async Task<DialogTurnResult> ConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var redditSearch = (RedditSearch)stepContext.Options;

            redditSearch.IsPublic = (bool)stepContext.Result;

            var messageText = $"Please confirm, You want a subreddit focusd on : {redditSearch.Interest} with atleast : {redditSearch.SubscribersCount} subscribers and it : {(redditSearch.IsPublic == true ? "Should" : "should not")} be public. Is this correct?";
            var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);

            return await stepContext.PromptAsync(nameof(ConfirmPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                StringBuilder res = new StringBuilder();

                var redditSearch = (RedditSearch)stepContext.Options;

                using (var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }))
                {
                    client.BaseAddress = new Uri("https://www.reddit.com/");
                    HttpResponseMessage response = client.GetAsync("reddits.json").Result;
                    response.EnsureSuccessStatusCode();
                    string result = response.Content.ReadAsStringAsync().Result;
                    var subreddits = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(result);
                    var data = subreddits.data.children;
                    foreach(var subreddit in data)
                    {
                        if (((string)subreddit.data.title).ToLower().Contains(redditSearch.Interest.ToLower()) && ((long)subreddit.data.subscribers) >= redditSearch.SubscribersCount)
                            if (redditSearch.IsPublic)
                            {
                                if (((string)subreddit.data.subreddit_type) == "public")
                                    res.AppendFormat("Subreddit: {0} - Subscribers: {1}{2}", subreddit.data.display_name_prefixed, subreddit.data.subscribers, Environment.NewLine);
                            }
                            else
                            {
                                if (((string)subreddit.data.subreddit_type) != "public")
                                    res.AppendFormat("Subreddit: {0} - Subscribers: {1}{2}", subreddit.data.display_name_prefixed, subreddit.data.subscribers, Environment.NewLine);
                            }
                    }
                    if (res.Equals(String.Empty))
                    {
                        await stepContext.Context.SendActivityAsync(
                            MessageFactory.Text("No subreddits found :(", inputHint: InputHints.IgnoringInput), cancellationToken);
                        return await stepContext.EndDialogAsync(redditSearch, cancellationToken);
                    }

                    await stepContext.Context.SendActivityAsync(
                   MessageFactory.Text(res.ToString(), inputHint: InputHints.IgnoringInput), cancellationToken);
                }

                return await stepContext.EndDialogAsync(redditSearch, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private static bool IsAmbiguous(string timex)
        {
            var timexProperty = new TimexProperty(timex);
            return !timexProperty.Types.Contains(Constants.TimexTypes.Definite);
        }

        private static Task<bool> SubscriberPromptValidator(PromptValidatorContext<long> promptContext, CancellationToken cancellationToken)
        {
            long i;
            bool isLong = Int64.TryParse(promptContext.Recognized.Value.ToString(), out i);
            return Task.FromResult(promptContext.Recognized.Succeeded && isLong && promptContext.Recognized.Value > 0);
        }
    }
}
