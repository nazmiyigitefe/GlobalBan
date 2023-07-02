// ReSharper disable AnnotateNotNullParameter
// ReSharper disable AnnotateNotNullTypeMember

using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace Pustalorc.GlobalBan.API.External
{
    public static class Discord
    {
        /// <summary>
        /// Sends a Rest.POST message to the specified url (Designed for discord URLs).
        /// </summary>
        /// <param name="url">The target URL to send the message to.</param>
        /// <param name="message">The message to be included in the Rest.POST query.</param>
        public static async Task SendWebhookPostAsync(string url, object message)
        {
            var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);

            var jsonToSend = JsonConvert.SerializeObject(message);

            request.AddJsonBody(jsonToSend);

            await client.ExecutePostAsync(request);
        }

        /// <summary>
        /// Sends a Rest.POST message to the specified url (Designed for discord URLs).
        /// </summary>
        /// <param name="url">The target URL to send the message to.</param>
        /// <param name="message">The message to be included in the Rest.POST query.</param>
        public static void SendWebhookPost(string url, object message)
        {
            var client = new RestClient(url);
            var request = new RestRequest("", Method.Post);
            var jsonToSend = JsonConvert.SerializeObject(message);

            request.AddJsonBody(jsonToSend);

            client.Execute(request);
        }

        /// <summary>
        /// Builds a basic discord message with an embed.
        /// </summary>
        /// <param name="title">The message for the embed.</param>
        /// <param name="description">The description to be shown in the embed.</param>
        /// <param name="username">The username to be used in the embed.</param>
        /// <param name="avatar">The avatar to be used in the embed.</param>
        /// <param name="embedColour">The colour to be used in the embed.</param>
        /// <param name="fields">The extra fields in the message of the embed.</param>
        /// <returns>JSON Styled object to then be sent to discord by a webhook.</returns>
        public static object BuildDiscordMessageWithEmbed(string title, string description, string username, string avatar,
            int embedColour, object[] fields)
        {
            var embMsg = new
            {
                title,
                description,
                color = embedColour,
                fields
            };

            return new
            {
                embeds = new[] { embMsg },
                username,
                avatar_url = avatar
            };
        }

        /// <summary>
        /// Builds a basic EmbedField object
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <param name="text">The main text of the field.</param>
        /// <param name="inline">If the field should inline.</param>
        /// <returns>A field object for discord Embeds</returns>
        public static object BuildDiscordField(string name, string text, bool? inline)
        {
            return new
            {
                name,
                value = text,
                inline
            };
        }
    }
}
