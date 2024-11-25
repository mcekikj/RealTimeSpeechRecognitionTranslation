namespace RealTimeAI
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.CognitiveServices.Speech;
    using Azure.Storage.Blobs;
    using Microsoft.CognitiveServices.Speech.Audio;
    using Azure;
    using System.Collections.Generic;
    using Azure.AI.Translation.Text;
    using RealTimeAI.Configuration;

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            //Azure Speech Service configuration
            string speechKey = AIServicesConfiguration.AzureAISpeechKey;
            string serviceRegion = AIServicesConfiguration.AzureAISpeechServiceRegion;

            // Azure Translator configuration  
            string translatorKey = AIServicesConfiguration.AzureAITranslatorKey;
            string endpoint = AIServicesConfiguration.AzureAITranslatorEndpoint;
            string region = AIServicesConfiguration.AzureAITranslatorRegion;

            // Azure Blob Storage configuration  
            string blobConnectionString = AIServicesConfiguration.BlobStorageConnectionString;
            string containerName = AIServicesConfiguration.BlobStorageContainerName;
            string blobName = AIServicesConfiguration.BlobStorageBlobName;

            var speechConfig = SpeechConfig.FromSubscription(speechKey, serviceRegion);
            speechConfig.SpeechRecognitionLanguage = "mk-MK"; // Macedonian language code  
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            recognizer.Recognized += async (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine($"RECOGNIZED: {e.Result.Text}");
                    string translatedText = await TranslateText(translatorKey, endpoint, e.Result.Text, "en", region); // Translate to English  
                    await AppendToBlobStorage(blobConnectionString, containerName, blobName, translatedText);
                }
            };

            await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

            Console.WriteLine("Press any key to stop...");
            Console.ReadKey();

            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
        }

        public static async Task<string> TranslateText(string subscriptionKey, string endpoint, string text, string targetLanguage, string region)
        {

            AzureKeyCredential credential = new(subscriptionKey);
            TextTranslationClient client = new(credential, region);

            try
            {

                Response<IReadOnlyList<TranslatedTextItem>> response = await client.TranslateAsync(targetLanguage, text).ConfigureAwait(false);
                IReadOnlyList<TranslatedTextItem> translations = response.Value;
                TranslatedTextItem translation = translations.FirstOrDefault();

                Console.WriteLine($"Detected languages of the input text: {translation?.DetectedLanguage?.Language} with score: {translation?.DetectedLanguage?.Score}.");
                Console.WriteLine($"Text was translated to: '{translation?.Translations?.FirstOrDefault().To}' and the result is: '{translation?.Translations?.FirstOrDefault()?.Text}'.");

                return translation?.Translations?.FirstOrDefault()?.Text;
            }
            catch (RequestFailedException exception)
            {
                Console.WriteLine($"Error Code: {exception.ErrorCode}");
                Console.WriteLine($"Message: {exception.Message}");
            }

            return null;
        }

        static async Task AppendToBlobStorage(string connectionString, string containerName, string blobName, string text)
        {
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await containerClient.ExistsAsync())
            {
                await containerClient.CreateAsync();
            }

            var appendText = Encoding.UTF8.GetBytes(text + Environment.NewLine);

            if (await blobClient.ExistsAsync())
            {
                var existingBlob = await blobClient.DownloadContentAsync();
                var existingContent = existingBlob.Value.Content.ToArray();
                var combinedContent = new byte[existingContent.Length + appendText.Length];
                Buffer.BlockCopy(existingContent, 0, combinedContent, 0, existingContent.Length);
                Buffer.BlockCopy(appendText, 0, combinedContent, existingContent.Length, appendText.Length);

                await blobClient.UploadAsync(new BinaryData(combinedContent), true);
            }
            else
            {
                await blobClient.UploadAsync(new BinaryData(appendText), true);
            }
        }
    }
}